// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Owns explicitly selected exception subscriptions without owning a logger or changing exception mode.
/// </summary>
/// <remarks>
///  <para>
///   Attach before Run and dispose even if Run never starts. UI routing is installed after startup UI
///   activation and removed when stopping begins. It replaces the thread's previous exception handler
///   and built-in exception UI; the supplied handler owns application policy. Do not install competing
///   UI exception handlers during the run. Process-wide subscriptions end at ApplicationStopped.
///  </para>
///  <para>
///   Logging and fallback callbacks are synchronous and must not wait for UI work or shutdown. Ordinary
///   provider failures go to the required fallback; fallback failures are deferred to DiagnosticCompletion.
///   No task is marked observed and no exception object is logged unless explicitly enabled.
///  </para>
/// </remarks>
public sealed class WinFormsExceptionLogging : IDisposable
{
    private const int EventIdBase = 1000;
    private static readonly Action<ILogger, WinFormsDiagnosticSource, Phase, bool?, int, Exception?>[] s_messages =
    [
        .. Enum.GetValues<WinFormsDiagnosticSource>().Select(source => LoggerMessage.Define<WinFormsDiagnosticSource, Phase, bool?, int>(
                source <= WinFormsDiagnosticSource.ApplicationStopped ? LogLevel.Information : LogLevel.Error,
                new EventId(EventIdBase + (int)source, source.ToString()),
                "WinForms diagnostic {Source} during {Phase}; terminating: {IsTerminating}; thread: {ManagedThreadId}.",
                new LogDefineOptions { SkipEnabledCheck = true }))
    ];
    private static readonly ConditionalWeakTable<WinFormsApplication, WinFormsExceptionLogging> s_scopes = new();
    private readonly WinFormsApplication _application;
    private readonly ILogger _logger;
    private readonly ThreadExceptionEventHandler _handleThreadException;
    private readonly Action<WinFormsLoggingFailure> _reportLoggingFailure;
    private readonly IWinFormsExceptionEvents _events;
    private readonly bool _logAppDomainExceptions;
    private readonly bool _logUnobservedTaskExceptions;
    private readonly bool _includeExceptionDetails;
    private readonly Lock _gate = new();
    private readonly TaskCompletionSource _diagnosticCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<Exception> _diagnosticFailures = [];
    private Thread? _uiThread;
    private Phase _phase;
    private int _reporting;
    private bool _subscribed;
    private bool _uiAttached;
    private bool _disposed;

    private WinFormsExceptionLogging(
        WinFormsApplication application,
        ILogger logger,
        ThreadExceptionEventHandler handleThreadException,
        Action<WinFormsLoggingFailure> reportLoggingFailure,
        WinFormsExceptionLoggingOptions options,
        IWinFormsExceptionEvents events)
    {
        _application = application;
        _logger = logger;
        _handleThreadException = handleThreadException;
        _reportLoggingFailure = reportLoggingFailure;
        _events = events;
        _logAppDomainExceptions = options.LogAppDomainExceptions;
        _logUnobservedTaskExceptions = options.LogUnobservedTaskExceptions;
        _includeExceptionDetails = options.IncludeExceptionDetails;
    }

    /// <summary>
    ///  Attaches one opt-in logging scope to an application's lifetime.
    /// </summary>
    /// <param name="application">The application whose lifetime bounds the subscriptions.</param>
    /// <param name="logger">An application-selected logger, which remains caller-owned.</param>
    /// <param name="handleThreadException">The explicit application UI exception policy.</param>
    /// <param name="reportLoggingFailure">A separate, non-throwing diagnostic fallback that must not reuse this logger.</param>
    /// <param name="configure">Optional configuration copied before attaching any subscriptions.</param>
    /// <returns>A caller-owned logging scope.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="InvalidOperationException">The application already has an undisposed logging scope.</exception>
    public static WinFormsExceptionLogging Attach(
        WinFormsApplication application,
        ILogger logger,
        ThreadExceptionEventHandler handleThreadException,
        Action<WinFormsLoggingFailure> reportLoggingFailure,
        Action<WinFormsExceptionLoggingOptions>? configure = null)
        => Attach(application, logger, handleThreadException, reportLoggingFailure, configure, new WinFormsExceptionEvents());

    internal static WinFormsExceptionLogging Attach(
        WinFormsApplication application,
        ILogger logger,
        ThreadExceptionEventHandler handleThreadException,
        Action<WinFormsLoggingFailure> reportLoggingFailure,
        Action<WinFormsExceptionLoggingOptions>? configure,
        IWinFormsExceptionEvents events)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(handleThreadException);
        ArgumentNullException.ThrowIfNull(reportLoggingFailure);
        ArgumentNullException.ThrowIfNull(events);
        WinFormsExceptionLoggingOptions options = new();
        configure?.Invoke(options);
        WinFormsExceptionLogging scope = new(application, logger, handleThreadException, reportLoggingFailure, options, events);
        if (!s_scopes.TryAdd(application, scope))
        {
            throw new InvalidOperationException(LoggingSR.AlreadyAttached);
        }

        scope._subscribed = true;
        application.Lifetime.ApplicationStarted += scope.OnStarted;
        application.Lifetime.ApplicationStopping += scope.OnStopping;
        application.Lifetime.ApplicationStopped += scope.OnStopped;
        if (scope._logAppDomainExceptions)
        {
            events.UnhandledException += scope.OnUnhandledException;
        }

        if (scope._logUnobservedTaskExceptions)
        {
            events.UnobservedTaskException += scope.OnUnobservedTaskException;
        }

        return scope;
    }

    /// <summary>
    ///  Gets a task completed by disposal, faulted only if the diagnostic fallback itself failed.
    /// </summary>
    /// <remarks>
    ///  <para>
    ///   Observe this task outside exception callbacks. It does not represent provider flushing or application recovery.
    ///  </para>
    /// </remarks>
    public Task DiagnosticCompletion => _diagnosticCompletion.Task;

    /// <summary>
    ///  Logs an exception propagated from Run without replacing it with an ordinary logging or fallback failure.
    /// </summary>
    /// <param name="exception">The original exception; application code must still propagate or explicitly handle it.</param>
    /// <remarks>
    ///  <para>
    ///   The logger must remain usable after Run. Use a caller-owned factory when host disposal would otherwise end it.
    ///  </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The logging scope is disposed.</exception>
    public void LogRunFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Report(WinFormsDiagnosticSource.RunFailure, exception, isTerminating: null, manual: true);
        }
    }

    /// <summary>
    ///  Removes subscriptions and completes DiagnosticCompletion without disposing logging providers.
    /// </summary>
    /// <exception cref="InvalidOperationException">UI routing is still attached and the caller is not its owning thread.</exception>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_uiAttached && Thread.CurrentThread != _uiThread)
            {
                throw new InvalidOperationException(LoggingSR.WrongThread);
            }

            Unsubscribe();
            _disposed = true;
            s_scopes.Remove(_application);
            CompleteDiagnostics();
        }
    }

    private void OnStarted(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            if (!_subscribed)
            {
                return;
            }

            _phase = Phase.Running;
            _uiThread = Thread.CurrentThread;
            _events.ThreadException += OnThreadException;
            _uiAttached = true;
            Report(WinFormsDiagnosticSource.ApplicationStarted);
        }
    }

    private void OnStopping(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            if (!_subscribed)
            {
                return;
            }

            DetachUi();
            _phase = Phase.Stopping;
            Report(WinFormsDiagnosticSource.ApplicationStopping);
        }
    }

    private void OnStopped(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            if (!_subscribed)
            {
                return;
            }

            _phase = Phase.Stopped;
            Report(WinFormsDiagnosticSource.ApplicationStopped);
            Unsubscribe();
        }
    }

    private void OnThreadException(object sender, ThreadExceptionEventArgs e)
    {
        Report(WinFormsDiagnosticSource.ThreadException, e.Exception);
        _handleThreadException(sender, e);
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        => Report(WinFormsDiagnosticSource.AppDomainUnhandledException, e.ExceptionObject as Exception, e.IsTerminating);

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        => Report(WinFormsDiagnosticSource.UnobservedTaskException, e.Exception);

    private void Report(
        WinFormsDiagnosticSource source,
        Exception? exception = null,
        bool? isTerminating = null,
        bool manual = false)
    {
        lock (_gate)
        {
            if (_disposed || (!manual && !_subscribed))
            {
                return;
            }

            _reporting++;
            try
            {
                LogLevel level = source <= WinFormsDiagnosticSource.ApplicationStopped ? LogLevel.Information : LogLevel.Error;
                if (_logger.IsEnabled(level))
                {
                    s_messages[(int)source](_logger, source, _phase, isTerminating, Environment.CurrentManagedThreadId,
                        _includeExceptionDetails ? exception : null);
                }
            }
            catch (Exception failure) when (failure is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
            {
                try
                {
                    _reportLoggingFailure(new WinFormsLoggingFailure(source, failure));
                }
                catch (Exception fallbackFailure) when (fallbackFailure is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
                {
                    _diagnosticFailures.Add(new AggregateException(failure, fallbackFailure));
                }
            }
            finally
            {
                _reporting--;
                CompleteDiagnostics();
            }
        }
    }

    private void Unsubscribe()
    {
        DetachUi();
        _application.Lifetime.ApplicationStarted -= OnStarted;
        _application.Lifetime.ApplicationStopping -= OnStopping;
        _application.Lifetime.ApplicationStopped -= OnStopped;
        if (_logAppDomainExceptions)
        {
            _events.UnhandledException -= OnUnhandledException;
        }

        if (_logUnobservedTaskExceptions)
        {
            _events.UnobservedTaskException -= OnUnobservedTaskException;
        }

        _subscribed = false;
    }

    private void DetachUi()
    {
        if (_uiAttached)
        {
            _events.ThreadException -= OnThreadException;
            _uiAttached = false;
        }
    }

    private void CompleteDiagnostics()
    {
        if (!_disposed || _reporting != 0)
        {
            return;
        }

        if (_diagnosticFailures.Count == 0)
        {
            _diagnosticCompletion.TrySetResult();
        }
        else
        {
            _diagnosticCompletion.TrySetException(_diagnosticFailures);
        }
    }

    /// <summary>
    ///  Describes the application's latest observed lifetime milestone, not the originating thread's ownership.
    /// </summary>
    private enum Phase
    {
        Created,
        Running,
        Stopping,
        Stopped
    }
}
