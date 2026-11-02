// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.WinForms;

namespace System.Windows.Forms.Tests;

/// <summary>
///  Exercises explicit exception policy, privacy, subscription ownership, and diagnostic failure boundaries.
/// </summary>
[Collection("Sequential")]
public class WinFormsExceptionLoggingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Routing_OptionsSnapshot_PreservesOriginalPolicyAndControlsDisclosure(bool includeDetails)
    {
        using WinFormsApplication application = CreateApplication();
        RecordingLogger logger = new();
        ExceptionEvents events = new();
        InvalidOperationException expected = new("private-message");
        expected.Data["secret"] = "private-data";
        object sender = new();
        ThreadExceptionEventArgs arguments = new(expected);
        WinFormsExceptionLoggingOptions? configured = null;
        using WinFormsExceptionLogging scope = Attach(application, logger, events, (actualSender, actualArguments) =>
        {
            actualSender.Should().BeSameAs(sender);
            actualArguments.Should().BeSameAs(arguments);
            throw expected;
        }, options =>
        {
            options.IncludeExceptionDetails = includeDetails;
            configured = options;
        });
        configured!.IncludeExceptionDetails = !includeDetails;
        configured.LogAppDomainExceptions = true;
        configured.LogUnobservedTaskExceptions = true;
        events.GlobalSubscriptions.Should().Be(0);
        events.UiSubscriptions.Should().Be(0);
        application.Lifetime.NotifyStarted();

        Assert.Throws<InvalidOperationException>(() => events.RaiseThreadException(sender, arguments)).Should().BeSameAs(expected);

        Entry entry = logger.Entries.Last();
        entry.Exception.Should().BeSameAs(includeDetails ? expected : null);
        entry.State["Source"].Should().Be(WinFormsDiagnosticSource.ThreadException);
        (entry.State["Phase"]?.ToString()).Should().Be("Running");
        entry.State["IsTerminating"].Should().BeNull();
        entry.Text.Should().NotContain("private-message").And.NotContain("private-data");
        entry.State.Values.Should().NotContain(expected);
        scope.Dispose();
        scope.DiagnosticCompletion.IsCompletedSuccessfully.Should().BeTrue();
        events.UiSubscriptions.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Routing_ProviderAndFallbackFailure_CannotReplaceOriginalUiException(bool fallbackFails)
    {
        using WinFormsApplication application = CreateApplication();
        RecordingLogger logger = new();
        ExceptionEvents events = new();
        InvalidOperationException original = new("Original UI failure.");
        IOException providerFailure = new("Provider failed.");
        ArgumentException fallbackFailure = new("Fallback failed.");
        List<WinFormsLoggingFailure> failures = [];
        using WinFormsExceptionLogging scope = WinFormsExceptionLogging.Attach(application, logger,
            (_, _) => throw original, failure =>
            {
                failures.Add(failure);
                if (fallbackFails)
                {
                    throw fallbackFailure;
                }
            }, configure: null, events);
        application.Lifetime.NotifyStarted();
        logger.Failure = providerFailure;

        Assert.Throws<InvalidOperationException>(() => events.RaiseThreadException(this, new(original))).Should().BeSameAs(original);

        failures.Should().ContainSingle();
        failures[0].Exception.Should().BeSameAs(providerFailure);
        failures[0].Source.Should().Be(WinFormsDiagnosticSource.ThreadException);
        scope.DiagnosticCompletion.IsCompleted.Should().BeFalse();
        scope.Dispose();
        if (fallbackFails)
        {
            scope.DiagnosticCompletion.IsFaulted.Should().BeTrue();
            scope.DiagnosticCompletion.Exception!.Flatten().InnerExceptions.Should().Equal(providerFailure, fallbackFailure);
        }
        else
        {
            scope.DiagnosticCompletion.IsCompletedSuccessfully.Should().BeTrue();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Routing_DisabledOrFailingFilter_StillInvokesPolicy(bool filterFails)
    {
        using WinFormsApplication application = CreateApplication();
        RecordingLogger logger = new() { Enabled = false };
        ExceptionEvents events = new();
        int handled = 0;
        List<WinFormsLoggingFailure> failures = [];
        using WinFormsExceptionLogging scope = WinFormsExceptionLogging.Attach(application, logger,
            (_, _) => handled++, failures.Add, configure: null, events);
        application.Lifetime.NotifyStarted();
        logger.FilterFailure = filterFails ? new InvalidOperationException("Filter failed.") : null;

        events.RaiseThreadException(this, new(new InvalidOperationException()));

        handled.Should().Be(1);
        logger.Entries.Should().BeEmpty();
        failures.Should().HaveCount(filterFails ? 1 : 0);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void GlobalReports_AreExplicit_PreserveTerminationAndDoNotObserveTasks(bool enabled, bool terminating)
    {
        using WinFormsApplication application = CreateApplication();
        RecordingLogger logger = new();
        ExceptionEvents events = new();
        using WinFormsExceptionLogging scope = Attach(application, logger, events, configure: options =>
        {
            options.LogAppDomainExceptions = enabled;
            options.LogUnobservedTaskExceptions = enabled;
        });
        InvalidOperationException original = new("private-global-message");
        UnobservedTaskExceptionEventArgs taskArguments = new(new AggregateException(original));

        events.RaiseUnhandledException(new(original, terminating));
        events.RaiseUnobservedTaskException(taskArguments);

        taskArguments.Observed.Should().BeFalse();
        logger.Entries.Should().HaveCount(enabled ? 2 : 0);
        if (enabled)
        {
            Entry clr = logger.Entries.First();
            clr.State["IsTerminating"].Should().Be(terminating);
            (clr.State["Phase"]?.ToString()).Should().Be("Created");
            foreach (Entry entry in logger.Entries)
            {
                entry.Exception.Should().BeNull();
                entry.Text.Should().NotContain("private-global-message");
            }

            logger.Entries.Last().State["IsTerminating"].Should().BeNull();
        }

        scope.Dispose();
        events.GlobalSubscriptions.Should().Be(0);
    }

    [Fact]
    public void OverlappingApplications_OwnOnlyTheirSubscriptions_AndIgnoreCapturedCallbacksAfterDisposal()
    {
        using WinFormsApplication firstApplication = CreateApplication();
        using WinFormsApplication secondApplication = CreateApplication();
        RecordingLogger first = new();
        RecordingLogger second = new();
        ExceptionEvents events = new();
        using WinFormsExceptionLogging firstScope = Attach(firstApplication, first, events, configure: EnableGlobals);
        using WinFormsExceptionLogging secondScope = Attach(secondApplication, second, events, configure: EnableGlobals);
        UnhandledExceptionEventHandler captured = events.UnhandledHandlers!;
        UnhandledExceptionEventArgs arguments = new(new InvalidOperationException(), false);

        events.RaiseUnhandledException(arguments);
        firstScope.Dispose();
        events.RaiseUnhandledException(arguments);

        first.Entries.Should().ContainSingle();
        second.Entries.Should().HaveCount(2);
        events.GlobalSubscriptions.Should().Be(2);
        secondScope.Dispose();
        captured(this, arguments);
        first.Entries.Should().ContainSingle();
        second.Entries.Should().HaveCount(2);
        events.GlobalSubscriptions.Should().Be(0);
    }

    [Fact]
    public void Stopped_UnsubscribesBeforeProviderDisposal_ButPermitsExplicitRunFailureReporting()
    {
        using WinFormsApplication application = CreateApplication();
        RecordingLogger logger = new();
        ExceptionEvents events = new();
        using WinFormsExceptionLogging scope = Attach(application, logger, events, configure: EnableGlobals);
        application.Lifetime.NotifyStarted();
        application.Lifetime.NotifyStopped();

        events.UiSubscriptions.Should().Be(0);
        events.GlobalSubscriptions.Should().Be(0);
        scope.DiagnosticCompletion.IsCompleted.Should().BeFalse();
        scope.LogRunFailure(new InvalidOperationException("private-run-failure"));
        logger.Entries.Last().State["Source"].Should().Be(WinFormsDiagnosticSource.RunFailure);
        logger.Entries.Last().Exception.Should().BeNull();
        scope.Dispose();
        scope.DiagnosticCompletion.IsCompletedSuccessfully.Should().BeTrue();
        Assert.Throws<ObjectDisposedException>(() => scope.LogRunFailure(new InvalidOperationException()));
    }

    [Fact]
    public void Dispose_OnWrongThread_RejectsWithoutRemovingUiOrGlobalHandlers()
    {
        using WinFormsApplication application = CreateApplication();
        ExceptionEvents events = new();
        using WinFormsExceptionLogging scope = Attach(application, new RecordingLogger(), events, configure: EnableGlobals);
        application.Lifetime.NotifyStarted();
        Exception? failure = null;
        Thread worker = new(() => failure = Record.Exception(scope.Dispose));
        worker.Start();
        worker.Join(TimeSpan.FromSeconds(30)).Should().BeTrue();

        failure.Should().BeOfType<InvalidOperationException>();
        events.UiSubscriptions.Should().Be(1);
        events.GlobalSubscriptions.Should().Be(2);
        scope.DiagnosticCompletion.IsCompleted.Should().BeFalse();
        scope.Dispose();
        events.UiSubscriptions.Should().Be(0);
        events.GlobalSubscriptions.Should().Be(0);
    }

    [Fact]
    public void ReentrantDispose_FromProvider_DefersFallbackFailureCompletionUntilCallbackFinishes()
    {
        using WinFormsApplication application = CreateApplication();
        RecordingLogger logger = new() { Failure = new IOException("Provider failed.") };
        ExceptionEvents events = new();
        ArgumentException fallbackFailure = new("Fallback failed.");
        using WinFormsExceptionLogging scope = WinFormsExceptionLogging.Attach(application, logger,
            (_, _) => { }, _ => throw fallbackFailure, EnableGlobals, events);
        logger.BeforeLog = () =>
        {
            scope.Dispose();
            scope.DiagnosticCompletion.IsCompleted.Should().BeFalse();
        };

        events.RaiseUnhandledException(new(new InvalidOperationException(), true));

        events.GlobalSubscriptions.Should().Be(0);
        scope.DiagnosticCompletion.IsFaulted.Should().BeTrue();
        scope.DiagnosticCompletion.Exception!.Flatten().InnerExceptions.Should().Equal(logger.Failure, fallbackFailure);
    }

    [WinFormsFact]
    public async Task Dispose_DrainsAnInFlightGlobalReport_AndStopsFutureReports()
    {
        using WinFormsApplication application = CreateApplication();
        RecordingLogger logger = new();
        ExceptionEvents events = new();
        using WinFormsExceptionLogging scope = Attach(application, logger, events, configure: EnableGlobals);
        using ManualResetEventSlim release = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource attemptingDispose = new(TaskCreationOptions.RunContinuationsAsynchronously);
        logger.BeforeLog = () =>
        {
            entered.TrySetResult();
            release.Wait(TimeSpan.FromSeconds(30)).Should().BeTrue();
        };
        CancellationToken token = TestContext.Current.CancellationToken;
        Task report = Task.Run(() => events.RaiseUnhandledException(new(new InvalidOperationException(), false)), token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
            Task disposing = Task.Run(() =>
            {
                attemptingDispose.TrySetResult();
                scope.Dispose();
            }, token);
            await attemptingDispose.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
            disposing.IsCompleted.Should().BeFalse();
            release.Set();
            await Task.WhenAll(report, disposing).WaitAsync(TimeSpan.FromSeconds(30), token);
        }
        finally
        {
            release.Set();
        }

        events.RaiseUnhandledException(new(new InvalidOperationException(), false));
        logger.Entries.Should().ContainSingle();
        await scope.DiagnosticCompletion.WaitAsync(token);
    }

    [Fact]
    public void Attach_DuplicateOrFailedConfiguration_DoesNotAddSubscriptions()
    {
        using WinFormsApplication application = CreateApplication();
        RecordingLogger logger = new();
        ExceptionEvents events = new();
        InvalidOperationException expected = new("Configuration failed.");
        Assert.Throws<InvalidOperationException>(() => Attach(application, logger, events, configure: _ => throw expected))
            .Should().BeSameAs(expected);
        events.GlobalSubscriptions.Should().Be(0);
        using WinFormsExceptionLogging first = Attach(application, logger, events, configure: EnableGlobals);
        Assert.Throws<InvalidOperationException>(() => Attach(application, logger, events, configure: EnableGlobals));
        events.GlobalSubscriptions.Should().Be(2);
        first.Dispose();
        using WinFormsExceptionLogging replacement = Attach(application, logger, events);
    }

    [Fact]
    public void DisposedBeforeStartup_DoesNotReplaceExistingUiPolicyOrReactivate()
    {
        using WinFormsApplication application = CreateApplication();
        RecordingLogger logger = new();
        ExceptionEvents events = new();
        int previousHandled = 0;
        events.ThreadException += (_, _) => previousHandled++;
        using WinFormsExceptionLogging scope = Attach(application, logger, events, configure: EnableGlobals);
        scope.Dispose();
        application.Lifetime.NotifyStarted();
        events.RaiseThreadException(this, new(new InvalidOperationException()));
        application.Lifetime.NotifyStopped();

        previousHandled.Should().Be(1);
        logger.Entries.Should().BeEmpty();
        events.GlobalSubscriptions.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealRun_UiRoutingAndProviderTeardown_PreserveStartupFailure(bool failStartup)
    {
        RunOnThread(() =>
        {
            RecordingProvider provider = new();
            InvalidOperationException original = new("Startup failed.");
            int handled = 0;
            List<WinFormsLoggingFailure> failures = [];
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseHost(new HostBuilder().ConfigureServices(services => services.AddSingleton<ILoggerProvider>(_ => provider)))
                .UseApplicationContext(() =>
                {
                    if (failStartup)
                    {
                        throw original;
                    }

                    ApplicationContext context = new();
                    SynchronizationContext.Current!.Post(_ =>
                    {
                        Application.OnThreadException(original);
                        context.ExitThread();
                    }, null);
                    return context;
                }).Build();
            ILogger logger = application.Host!.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Application");
            using WinFormsExceptionLogging scope = WinFormsExceptionLogging.Attach(application, logger,
                (_, e) =>
                {
                    e.Exception.Should().BeSameAs(original);
                    handled++;
                }, failures.Add, EnableGlobals);
            if (failStartup)
            {
                Action run = () =>
                {
                    try
                    {
                        application.Run(TestContext.Current.CancellationToken);
                    }
                    catch (InvalidOperationException exception)
                    {
                        scope.LogRunFailure(exception);
                        throw;
                    }
                };
                Assert.Throws<InvalidOperationException>(run).Should().BeSameAs(original);
            }
            else
            {
                application.Run(TestContext.Current.CancellationToken);
            }

            handled.Should().Be(failStartup ? 0 : 1);
            provider.Disposed.Should().BeTrue();
            provider.SourcesAtDisposal.Should().Equal(failStartup
                ? [WinFormsDiagnosticSource.ApplicationStopping, WinFormsDiagnosticSource.ApplicationStopped]
                : [WinFormsDiagnosticSource.ApplicationStarted, WinFormsDiagnosticSource.ThreadException,
                    WinFormsDiagnosticSource.ApplicationStopping, WinFormsDiagnosticSource.ApplicationStopped]);
            scope.Dispose();
            scope.DiagnosticCompletion.IsCompletedSuccessfully.Should().BeTrue();
        });
    }

    private static WinFormsApplication CreateApplication()
        => WinFormsApplication.CreateBuilder().UseApplicationContext(() => new ApplicationContext()).Build();

    private static WinFormsExceptionLogging Attach(
        WinFormsApplication application,
        RecordingLogger logger,
        ExceptionEvents events,
        ThreadExceptionEventHandler? handler = null,
        Action<WinFormsExceptionLoggingOptions>? configure = null)
        => WinFormsExceptionLogging.Attach(application, logger, handler ?? ((_, _) => { }),
            failure => throw new InvalidOperationException("Unexpected logging failure.", failure.Exception), configure, events);

    private static void EnableGlobals(WinFormsExceptionLoggingOptions options)
    {
        options.LogAppDomainExceptions = true;
        options.LogUnobservedTaskExceptions = true;
    }

    private static void RunOnThread(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(60)).Should().BeTrue();
        failure?.Throw();
    }

    /// <summary>
    ///  Reproduces UI replacement semantics and exposes deterministic process-wide event snapshots.
    /// </summary>
    private sealed class ExceptionEvents : IWinFormsExceptionEvents
    {
        private ThreadExceptionEventHandler? _ui;
        public UnhandledExceptionEventHandler? UnhandledHandlers { get; private set; }
        private EventHandler<UnobservedTaskExceptionEventArgs>? _tasks;

        public event ThreadExceptionEventHandler? ThreadException
        {
            add => _ui = value;
            remove => _ui -= value;
        }

        public event UnhandledExceptionEventHandler? UnhandledException
        {
            add => UnhandledHandlers += value;
            remove => UnhandledHandlers -= value;
        }

        public event EventHandler<UnobservedTaskExceptionEventArgs>? UnobservedTaskException
        {
            add => _tasks += value;
            remove => _tasks -= value;
        }

        public int UiSubscriptions => _ui?.GetInvocationList().Length ?? 0;
        public int GlobalSubscriptions
            => (UnhandledHandlers?.GetInvocationList().Length ?? 0) + (_tasks?.GetInvocationList().Length ?? 0);

        public void RaiseThreadException(object sender, ThreadExceptionEventArgs arguments) => _ui?.Invoke(sender, arguments);
        public void RaiseUnhandledException(UnhandledExceptionEventArgs arguments) => UnhandledHandlers?.Invoke(this, arguments);
        public void RaiseUnobservedTaskException(UnobservedTaskExceptionEventArgs arguments) => _tasks?.Invoke(this, arguments);
    }

    /// <summary>
    ///  Captures structured logger state without formatting exception objects.
    /// </summary>
    private sealed record Entry(Exception? Exception, Dictionary<string, object?> State, string Text);

    /// <summary>
    ///  Simulates provider/filter failures and coordinated in-flight logging.
    /// </summary>
    private sealed class RecordingLogger : ILogger
    {
        public ConcurrentQueue<Entry> Entries { get; } = new();
        public Exception? Failure { get; set; }
        public Exception? FilterFailure { get; set; }
        public Action? BeforeLog { get; set; }
        public bool Enabled { get; set; } = true;
        public bool Disposed { get; set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            if (FilterFailure is { } failure)
            {
                ExceptionDispatchInfo.Throw(failure);
            }

            return Enabled;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            BeforeLog?.Invoke();
            if (Failure is { } failure)
            {
                ExceptionDispatchInfo.Throw(failure);
            }

            var values = state as IEnumerable<KeyValuePair<string, object?>>;
            Entries.Enqueue(new(exception, values?.ToDictionary() ?? [], formatter(state, exception)));
        }
    }

    /// <summary>
    ///  Records whether provider disposal follows the application's last lifetime diagnostic.
    /// </summary>
    private sealed class RecordingProvider : ILoggerProvider
    {
        private readonly RecordingLogger _logger = new();
        public bool Disposed => _logger.Disposed;
        public WinFormsDiagnosticSource[] SourcesAtDisposal { get; private set; } = [];
        public ILogger CreateLogger(string categoryName) => _logger;

        public void Dispose()
        {
            SourcesAtDisposal =
            [
                .. _logger.Entries
                .Where(entry => entry.State.ContainsKey("Source"))
                .Select(entry => (WinFormsDiagnosticSource)entry.State["Source"]!)
            ];
            _logger.Disposed = true;
        }
    }
}
