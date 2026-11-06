// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Owns a built Windows Forms application's startup selection and lifetime notifications.
/// </summary>
/// <remarks>
///  <para>
///   The caller supplies the owning STA thread. Run starts one existing Windows Forms message loop
///   and disposes owned resources when it finishes. StopAsync can request shutdown from another thread.
///  </para>
/// </remarks>
public sealed class WinFormsApplication : IDisposable
{
    private readonly Thread _ownerThread;
    private IHost? _host;
    private IHostApplicationLifetime? _hostLifetime;
    private readonly Lock _stateLock = new();
    private readonly TaskCompletionSource _stopRequest = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _uiStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _runCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _startupCancellation = new();
    private readonly List<Exception> _exceptions = [];
    private readonly WinFormsApplicationFeatures? _features;
    private WinFormsShutdownReason? _shutdownReason;
    private Func<Task>? _saveSettings;
    private Task? _settingsSaveTask;
    private bool _featureShutdown;
    private WinFormsApplicationOptions? _options;
    private ApplicationContext? _context;
    private WinFormsUiScope? _uiScopes;
    private Application.ThreadContext? _threadContext;
    private Control? _dispatcher;
    private Task _startupCancellationTask = Task.CompletedTask;
    private ExceptionDispatchInfo? _runFailure;
    private Exception? _messageLoopFailure;
    private bool _activationAttempted;
    private bool _activating;
    private bool _running;
    private bool _loopEntered;
    private bool _stopRequested;
    private bool _stopPosted;
    private bool _uiStopping;
    private bool _cleanupStarted;
    private bool _disposed;
    private WinFormsSingleInstance? _singleInstance;
    private EventHandler<WinFormsNextInstanceRequest>? _nextInstance;
    private CancellationTokenSource? _activationStopping;

    internal void AttachSingleInstance(
        WinFormsSingleInstance? primary, EventHandler<WinFormsNextInstanceRequest>? nextInstance)
    {
        primary?.Claim(this);
        _singleInstance = primary;
        _nextInstance = nextInstance;
        if (primary is not null)
        {
            _activationStopping = new();
        }
    }

    /// <summary>
    ///  Marshals one bounded activation to the existing owner dispatcher, or rejects unavailable UI.
    /// </summary>
    internal async Task<WinFormsSingleInstanceResult> DispatchNextInstanceAsync(
        WinFormsNextInstanceRequest request, CancellationToken cancellationToken)
    {
        Control dispatcher;
        lock (_stateLock)
        {
            if (_stopRequested || _uiStopping || _cleanupStarted || _disposed)
            {
                return WinFormsSingleInstanceResult.Stopping;
            }

            if (!_loopEntered || _dispatcher is null)
            {
                return WinFormsSingleInstanceResult.NotReady;
            }

            dispatcher = _dispatcher;
        }

        TaskCompletionSource<WinFormsSingleInstanceResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int state = 0;
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _activationStopping!.Token);
        using CancellationTokenRegistration registration = deadline.Token.Register(() =>
        {
            // Cancel a queued callback, never replay or interrupt a callback which has begun.
            int previous = Interlocked.CompareExchange(ref state, 2, 0);
            completion.TrySetResult(previous != 0
                ? WinFormsSingleInstanceResult.Unknown
                : _activationStopping.IsCancellationRequested
                    ? WinFormsSingleInstanceResult.Stopping
                    : WinFormsSingleInstanceResult.Unavailable);
        });
        try
        {
            dispatcher.BeginInvoke(() =>
            {
                if (Interlocked.CompareExchange(ref state, 1, 0) != 0)
                {
                    return;
                }

                lock (_stateLock)
                {
                    if (_stopRequested || _uiStopping || _cleanupStarted || _disposed)
                    {
                        completion.TrySetResult(WinFormsSingleInstanceResult.Stopping);
                        return;
                    }
                }

                try
                {
                    _nextInstance!.Invoke(this, request);
                }
                catch (Exception exception) when (!exception.IsCriticalException())
                {
                    completion.TrySetResult(WinFormsSingleInstanceResult.HandlerFailed);
                    return;
                }

                try
                {
                    // Only the selected context's live Form; no global OpenForms search or forced foreground.
                    if (request.BringToForeground && !StopRequested && _context?.MainForm is { IsDisposed: false, Visible: true } form)
                    {
                        if (form.WindowState == FormWindowState.Minimized)
                        {
                            form.WindowState = FormWindowState.Normal;
                        }

                        form.Activate();
                    }

                    completion.TrySetResult(WinFormsSingleInstanceResult.Handled);
                }
                catch (Exception exception) when (!exception.IsCriticalException())
                {
                    completion.TrySetResult(WinFormsSingleInstanceResult.HandledFocusFailed);
                }
            });
        }
        catch (Exception exception) when (!exception.IsCriticalException())
        {
            completion.TrySetResult(WinFormsSingleInstanceResult.Stopping);
        }

        return await completion.Task.ConfigureAwait(false);
    }

    internal WinFormsApplication(
        WinFormsApplicationOptions options,
        IHost? host = null,
        WinFormsApplicationFeatures? features = null)
    {
        _ownerThread = options.OwnerThread;
        _options = options;
        _host = host;
        _features = features;
        _hostLifetime = host?.Services.GetRequiredService<IHostApplicationLifetime>();
        Lifetime = new WinFormsApplicationLifetime();
    }

    /// <summary>
    ///  Creates a builder on the current thread without creating UI resources.
    /// </summary>
    /// <returns>A new builder.</returns>
    public static WinFormsApplicationBuilder CreateBuilder()
        => new();

    /// <summary>
    ///  Gets this application's lifetime notifications.
    /// </summary>
    public WinFormsApplicationLifetime Lifetime { get; }

    /// <summary>
    ///  Gets the coordinated host, or null when no host builder was selected.
    /// </summary>
    /// <remarks>
    ///  <para>
    ///   Run owns host startup. Host StopAsync requests shutdown and joins one shared stop operation;
    ///   its token cancels only the caller's wait. Do not block the UI thread waiting for shutdown.
    ///   Host disposal has the same owner-thread and inactive-runtime requirements as application disposal.
    ///  </para>
    /// </remarks>
    public IHost? Host => _host;

    internal bool IsRunning => Volatile.Read(ref _running);

    internal void SetHost(WinFormsApplicationHost host)
    {
        _hostLifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        _host = host;
    }

    /// <summary>
    ///  Runs the application on its owning thread.
    /// </summary>
    /// <remarks>
    ///  <para>
    ///   This method blocks until the message loop and coordinated cleanup finish. The application runs once.
    ///  </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The application is disposed.</exception>
    /// <exception cref="InvalidOperationException">
    ///  The caller is not the owning STA, another loop or runner is active, startup has already been
    ///  activated, or automatic WinForms synchronization-context installation is unavailable.
    /// </exception>
    public void Run()
        => Run(CancellationToken.None);

    /// <summary>
    ///  Runs the application until it exits or cancellation requests shutdown.
    /// </summary>
    /// <param name="cancellationToken">A token that requests shutdown without abandoning cleanup.</param>
    /// <exception cref="ObjectDisposedException">The application is disposed.</exception>
    /// <exception cref="InvalidOperationException">The application cannot start on the current thread or context.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested. An accepted run finishes cleanup first.</exception>
    public void Run(CancellationToken cancellationToken)
    {
        VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_running || _activationAttempted)
        {
            throw new InvalidOperationException(SR.CantNestMessageLoops);
        }

        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            throw new InvalidOperationException(SR.ThreadMustBeSTA);
        }

        SynchronizationContext? previousContext = SynchronizationContext.Current;
        if (!WindowsFormsSynchronizationContext.AutoInstall
            || (previousContext is not null
                && previousContext.GetType() != typeof(SynchronizationContext)
                && previousContext is not WindowsFormsSynchronizationContext)
            || previousContext is WindowsFormsSynchronizationContext { IsUsableOnCurrentThread: false })
        {
            throw new InvalidOperationException(SR.WinFormsApplication_SynchronizationContextUnavailable);
        }

        cancellationToken.ThrowIfCancellationRequested();
        Application.ThreadContext threadContext = Application.ThreadContext.FromCurrent();
        if (threadContext.ApplicationRunner is not null || threadContext.GetMessageLoop())
        {
            throw new InvalidOperationException(SR.CantNestMessageLoops);
        }

        _threadContext = threadContext;
        _running = true;
        threadContext.ApplicationRunner = this;
        WindowsFormsSynchronizationContext? installedContext = null;
        Task? hostTask = null;
        using CancellationTokenRegistration registration = cancellationToken.Register(
            static state => ((WinFormsApplication)state!).RequestStop(WinFormsShutdownReason.RunTokenCancellation), this);

        try
        {
            if (_host is not null && !StopRequested)
            {
                TaskCompletionSource hostStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
                hostTask = Task.Run(() => CoordinateHostAsync(hostStarted), CancellationToken.None);
                try
                {
                    hostStarted.Task.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) when (StopRequested)
                {
                    // Committed shutdown may cancel host startup before UI activation.
                }
            }

            if (!StopRequested)
            {
                WindowsFormsSynchronizationContext.InstallIfNeeded();
                if (!ReferenceEquals(SynchronizationContext.Current, previousContext))
                {
                    installedContext = (WindowsFormsSynchronizationContext)SynchronizationContext.Current!;
                }

                lock (_stateLock)
                {
                    _dispatcher = threadContext.MarshallingControl;
                }

                if (!StopRequested)
                {
                    if (_features is not null)
                    {
                        WinFormsStartingEventArgs starting = new((ReadOnlyCollection<string>)_features.CommandLine!);
                        _features.Starting?.Invoke(this, starting);
                        if (starting.Cancel)
                        {
                            RequestStop();
                        }
                    }
                }

                if (!StopRequested)
                {
                    ActivateStartup();
                    if (!StopRequested)
                    {
                        Lifetime.NotifyStarted();
                        lock (_stateLock)
                        {
                            _loopEntered = !_stopRequested;
                        }

                        if (_loopEntered)
                        {
                            Application.Run(_context!);
                        }
                    }
                }
            }
        }
        catch (Exception exception) when (!exception.IsCriticalException())
        {
            if (!ReferenceEquals(exception, _messageLoopFailure))
            {
                RecordLifecycleFailure(exception, WinFormsLifecyclePhase.Startup);
            }
        }
        finally
        {
            NotifyStopping();
            CaptureFailure(() => _singleInstance?.StopForApplicationAsync(this).GetAwaiter().GetResult());
            lock (_stateLock)
            {
                _cleanupStarted = true;
                _dispatcher = null;
            }

            CaptureFailure(() => threadContext.Dispose(postQuit: false));
            threadContext.ApplicationRunner = null;
            _threadContext = null;
            CaptureFailure(DisposeStartupResources);
            if (installedContext is not null)
            {
                CaptureFailure(() => WindowsFormsSynchronizationContext.Uninstall(turnOffAutoInstall: false));
                CaptureFailure(installedContext.Dispose);
            }

            SynchronizationContext.SetSynchronizationContext(previousContext);
            _uiStopped.TrySetResult();
            CaptureFailure(() => CompleteSettingsSaveAsync().GetAwaiter().GetResult());
            if (hostTask is not null)
            {
                CaptureFailure(() => hostTask.GetAwaiter().GetResult());
            }
            else if (_host is WinFormsApplicationHost stoppedHost)
            {
                CaptureFailure(() => stoppedHost.StopAsync(CancellationToken.None).GetAwaiter().GetResult());
            }

            CaptureFailure(() => _startupCancellationTask.GetAwaiter().GetResult());
            CaptureFailure(Lifetime.NotifyStopped);
            if (_host is not null)
            {
                CaptureFailure(DisposeHost);
            }

            lock (_stateLock)
            {
                if (cancellationToken.IsCancellationRequested && _exceptions.Count == 0)
                {
                    _exceptions.Add(new OperationCanceledException(cancellationToken));
                }
            }

            CompleteRun();
        }

        _runFailure?.Throw();
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>
    ///  Requests application-wide shutdown and asynchronously waits for this application's cleanup.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels only the wait, not the shutdown request.</param>
    /// <returns>A task that completes after cleanup, or reports a run failure or canceled wait.</returns>
    /// <remarks>
    ///  <para>
    ///   May be called from any thread. Do not synchronously wait on the owning UI thread.
    ///   A request before Run is retained until Run or Dispose completes. Unlike a user close,
    ///   this committed shutdown request cannot be vetoed by a form-closing handler.
    ///  </para>
    /// </remarks>
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        RequestStop();
        return WaitForStopAsync(cancellationToken);
    }

    internal ApplicationContext? StartupContext => _context;

    internal Task UiCompletion => _uiStopped.Task;

    // Both message-loop engines can see WM_QUIT and release windows before returning.
    // Opt-in callbacks must run at normal root-pump return, not during message dispatch.
    internal bool DeferThreadWindowDisposal
        => _features is not null && _loopEntered && !_featureShutdown
            && _messageLoopFailure is null && !_cleanupStarted;

    /// <summary>
    ///  Retains an escaping root-loop failure before the engine records teardown failures.
    /// </summary>
    internal void RecordMessageLoopFailure(Exception exception, bool pumpStarted)
    {
        if (_features is not null)
        {
            _messageLoopFailure = exception;
            RecordLifecycleFailure(exception, pumpStarted ? WinFormsLifecyclePhase.MessageLoop : WinFormsLifecyclePhase.Startup);
        }
    }

    /// <summary>
    ///  Participates at normal root-pump return, before the existing engine releases UI resources.
    /// </summary>
    internal void CompleteFeatureShutdown()
    {
        if (_features is null || _featureShutdown)
        {
            return;
        }

        _featureShutdown = true;
        NotifyStopping();
        WinFormsShutdownReason reason;
        lock (_stateLock)
        {
            if (_exceptions.Count != 0)
            {
                return;
            }

            reason = _shutdownReason ?? WinFormsShutdownReason.UserExit;
        }

        try
        {
            _features.Shutdown?.Invoke(this, new WinFormsShutdownEventArgs(reason));
            if (_features.PrepareSettingsSave is { } prepare)
            {
                _saveSettings = prepare(this);
                ArgumentNullException.ThrowIfNull(_saveSettings);
            }
        }
        catch (Exception exception) when (!exception.IsCriticalException())
        {
            RecordLifecycleFailure(exception, WinFormsLifecyclePhase.Shutdown);
        }
    }

    /// <summary>
    ///  Joins one UI-free persistence operation before coordinated host stop and provider disposal.
    /// </summary>
    internal async Task CompleteSettingsSaveAsync()
    {
        await _uiStopped.Task.ConfigureAwait(false);
        if (_saveSettings is null)
        {
            return;
        }

        Task save;
        lock (_stateLock)
        {
            save = _settingsSaveTask ??= Task.Run(async () =>
            {
                try
                {
                    if (_saveSettings is { } persist)
                    {
                        await persist().ConfigureAwait(false);
                    }
                }
                catch (Exception exception) when (!exception.IsCriticalException())
                {
                    RecordException(exception);
                }
            });
        }

        await save.ConfigureAwait(false);
    }

    private void RecordLifecycleFailure(Exception exception, WinFormsLifecyclePhase phase)
    {
        RecordException(exception);
        if (_features?.ReportFailure is { } report)
        {
            // Report once, on the owner thread. A reporting failure must not replace the original.
            CaptureFailure(() => report(exception, phase));
        }
    }

    private bool StopRequested
    {
        get
        {
            lock (_stateLock)
            {
                return _stopRequested;
            }
        }
    }

    internal ApplicationContext ActivateStartup()
    {
        VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_activationAttempted)
        {
            throw new InvalidOperationException(SR.WinFormsApplication_StartupAlreadyActivated);
        }

        _activationAttempted = true;
        _activating = true;
        try
        {
            WinFormsApplicationOptions options = _options!;
            if (options.StartupForm is { } form)
            {
                WinFormsApplicationOptions.ValidateStartupForm(form);
                _context = new ApplicationContext(form);
            }
            else if (options.ServiceFormFactory is { } factory)
            {
                IServiceProvider services = _host?.Services
                    ?? throw new InvalidOperationException(SR.WinFormsApplication_ServiceActivationRequiresHost);
                _context = new ApplicationContext(WinFormsServiceActivation.Create(services, factory));
            }
            else if (options.ScopedContextFactory is { } scopedFactory)
            {
                IServiceProvider services = _host?.Services
                    ?? throw new InvalidOperationException(SR.WinFormsApplication_ServiceActivationRequiresHost);
                _uiScopes = new WinFormsUiScope(services, () => StopRequested);
                _context = _uiScopes.CreateContext(scopedFactory);
            }
            else
            {
                ApplicationContext context = options.ContextFactory!()
                    ?? throw new InvalidOperationException(SR.WinFormsApplication_NullContext);

                if (context.MainForm is { } mainForm)
                {
                    WinFormsApplicationOptions.ValidateStartupForm(mainForm);
                }

                _context = context;
            }

            _options = null;
            return _context;
        }
        finally
        {
            _activating = false;
        }
    }

    /// <summary>
    ///  Disposes an unrun application's resources, or does nothing after completed cleanup.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///  The calling thread does not own the application, or startup activation or execution is in progress.
    /// </exception>
    public void Dispose()
    {
        VerifyAccess();
        if (_disposed)
        {
            return;
        }

        if (_activating || _running)
        {
            throw new InvalidOperationException(SR.WinFormsApplication_ActivationInProgress);
        }

        try
        {
            CaptureFailure(() => _singleInstance?.StopForApplicationAsync(this).GetAwaiter().GetResult());
            CaptureFailure(DisposeStartupResources);
            _uiStopped.TrySetResult();
            if (_host is WinFormsApplicationHost stoppedHost)
            {
                CaptureFailure(() => stoppedHost.StopAsync(CancellationToken.None).GetAwaiter().GetResult());
            }

            if (_host is not null)
            {
                CaptureFailure(DisposeHost);
            }

            CaptureFailure(() => _startupCancellationTask.GetAwaiter().GetResult());
        }
        catch (Exception exception) when (!exception.IsCriticalException())
        {
            RecordException(exception);
        }
        finally
        {
            _uiStopped.TrySetResult();
            CompleteRun();
        }

        _runFailure?.Throw();
    }

    private void DisposeHost()
        => Task.Run(() =>
        {
            if (_host is WinFormsApplicationHost adapter)
            {
                adapter.DisposeHost();
            }
            else
            {
                _host!.Dispose();
            }
        }).GetAwaiter().GetResult();

    private void DisposeStartupResources()
    {
        ApplicationContext? context = _context;
        Form? startupForm = _options?.StartupForm;
        _context = null;
        _options = null;

        if (context is not null)
        {
            DisposeContext(context);
        }
        else if (startupForm is { IsDisposed: false })
        {
            CaptureFailure(startupForm.Dispose);
        }

        if (_uiScopes is { } scopes)
        {
            _uiScopes = null;
            CaptureFailure(scopes.Dispose);
        }
    }

    internal void DisposeContext(ApplicationContext? context)
    {
        if (ReferenceEquals(_context, context))
        {
            _context = null;
        }

        if (context is not null)
        {
            Form? mainForm = context.MainForm;
            CaptureFailure(context.Dispose);
            if (mainForm is { IsDisposed: false })
            {
                CaptureFailure(mainForm.Dispose);
            }
        }
    }

    internal void NotifyStopping()
    {
        if (Thread.CurrentThread != _ownerThread)
        {
            RequestStop();
            return;
        }

        lock (_stateLock)
        {
            // Host stop echoes must not queue another exit after UI teardown has committed.
            _uiStopping = true;
        }

        SignalStopRequest();
        _activationStopping?.Cancel();
        _uiScopes?.BeginStopping();
        CaptureFailure(Lifetime.NotifyStopping);
    }

    internal void CaptureFailure(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (!exception.IsCriticalException())
        {
            RecordException(exception);
        }
    }

    internal void RecordException(Exception exception)
    {
        lock (_stateLock)
        {
            _exceptions.Add(exception);
        }
    }

    private void SignalStopRequest()
    {
        lock (_stateLock)
        {
            if (_disposed || _stopRequested)
            {
                return;
            }

            _stopRequested = true;
            _activationStopping?.Cancel();
            _startupCancellationTask = _startupCancellation.CancelAsync();
            _stopRequest.TrySetResult();
        }
    }

    internal void RequestStop()
        => RequestStop(WinFormsShutdownReason.RequestedStop);

    private void RequestStop(WinFormsShutdownReason reason)
    {
        lock (_stateLock)
        {
            if (!_uiStopping && !_cleanupStarted && !_disposed)
            {
                _shutdownReason ??= reason;
            }
        }

        SignalStopRequest();
        Control? dispatcher;
        lock (_stateLock)
        {
            if (!_loopEntered || _uiStopping || _cleanupStarted || _disposed || _stopPosted || _dispatcher is null)
            {
                return;
            }

            _stopPosted = true;
            dispatcher = _dispatcher;
        }

        try
        {
            dispatcher.BeginInvoke(StopOnUiThread);
        }
        catch (Exception exception) when (!exception.IsCriticalException())
        {
            lock (_stateLock)
            {
                if (!_uiStopping && !_cleanupStarted && !_disposed)
                {
                    _exceptions.Add(exception);
                    _threadContext?.PostQuit();
                }
            }
        }
    }

    private void StopOnUiThread()
    {
        lock (_stateLock)
        {
            if (_uiStopping || _cleanupStarted || _disposed)
            {
                return;
            }
        }

        NotifyStopping();
        try
        {
            Application.Exit(new CancelEventArgs());
        }
        catch (Exception exception) when (!exception.IsCriticalException())
        {
            RecordException(exception);
        }
        finally
        {
            if (_threadContext is { PostedQuit: false } threadContext)
            {
                threadContext.PostQuit();
            }
        }
    }

    private async Task WaitForStopAsync(CancellationToken cancellationToken)
    {
        await _runCompleted.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        _runFailure?.Throw();
    }

    private async Task CoordinateHostAsync(TaskCompletionSource hostStarted)
    {
        CancellationTokenRegistration registration = default;
        try
        {
            registration = _hostLifetime!.ApplicationStopping.Register(
                static state => ((WinFormsApplication)state!).RequestStop(), this);
            await _host!.StartAsync(_startupCancellation.Token).ConfigureAwait(false);
            hostStarted.TrySetResult();
        }
        catch (Exception exception)
        {
            hostStarted.TrySetException(exception);
            if (exception.IsCriticalException())
            {
                throw;
            }
        }

        try
        {
            await _stopRequest.Task.ConfigureAwait(false);
            CaptureFailure(_hostLifetime!.StopApplication);
            await _uiStopped.Task.ConfigureAwait(false);
            try
            {
                await _host!.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) when (!exception.IsCriticalException())
            {
                RecordException(exception);
            }
        }
        finally
        {
            registration.Dispose();
        }
    }

    private void CompleteRun()
    {
        lock (_stateLock)
        {
            _running = false;
            _disposed = true;
            _startupCancellation.Dispose();
            _activationStopping?.Dispose();
            _singleInstance = null;
            _nextInstance = null;
            _runFailure = _exceptions.Count switch
            {
                0 => null,
                1 => ExceptionDispatchInfo.Capture(_exceptions[0]),
                _ => ExceptionDispatchInfo.Capture(new AggregateException(_exceptions))
            };
        }

        _runCompleted.TrySetResult();
    }

    private void VerifyAccess()
    {
        if (Thread.CurrentThread != _ownerThread)
        {
            throw new InvalidOperationException(SR.WinFormsApplication_WrongThread);
        }
    }
}
