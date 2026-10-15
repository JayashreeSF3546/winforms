// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Represents a configured Windows Forms application.
/// </summary>
/// <remarks>
///  <para>
///   The thread calling <see cref="Run"/> owns the UI thread and message loop.
///   The application starts and stops an optional Generic Host on that thread's
///   lifecycle, but keeps asynchronous host shutdown from blocking the message
///   pump.
///  </para>
/// </remarks>
public sealed class WinFormsApplication : IDisposable
{
    private readonly Lock _stopLock = new();
    private readonly Lock _formScopesLock = new();
    private readonly List<WinFormsFormScope> _formScopes = [];
    private readonly List<Task> _formScopeDisposalTasks = [];
    private readonly TaskCompletionSource _hostStoppedSource =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _applicationStoppingSource =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private WinFormsApplicationOptions? _options;
    private IHost? _host;
    private IHostApplicationLifetime? _hostLifetime;
    private ApplicationContext? _applicationContext;
    private Control? _marshallingControl;
    private Thread? _uiThread;
    private Func<ApplicationContext, bool>? _previousExitThreadHandler;
    private CancellationTokenRegistration _applicationStoppingRegistration;
    private CancellationTokenRegistration _applicationStoppedRegistration;
    private Task? _hostStopTask;
    private Exception? _runtimeException;
    private int _runState;
    private int _messageLoopEntered;
    private int _allowThreadExit;
    private int _hostStopRequestedByApplication;
    private int _hostStartAttempted;
    private int _hostStopped;
    private int _hostOwnershipTransferred;
    private int _disposeRequested;
    private int _applicationStoppingDispatchScheduled;

    internal WinFormsApplication(WinFormsApplicationOptions options)
    {
        _options = options;
        _host = options.Host;
    }

    /// <summary>
    ///  Creates a builder for a Windows Forms application.
    /// </summary>
    /// <returns>A new application builder.</returns>
    public static WinFormsApplicationBuilder CreateBuilder()
        => WinFormsApplicationBuilder.CreateBuilder();

    /// <summary>
    ///  Creates a builder for a Windows Forms application using the specified
    ///  command-line arguments.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>A new application builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="args"/> is null.</exception>
    public static WinFormsApplicationBuilder CreateBuilder(string[] args)
        => WinFormsApplicationBuilder.CreateBuilder(args);

    /// <summary>
    ///  Creates a builder for a Windows Forms application using the specified
    ///  host settings.
    /// </summary>
    /// <param name="settings">The host settings.</param>
    /// <returns>A new application builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="settings"/> is null.</exception>
    public static WinFormsApplicationBuilder CreateBuilder(HostApplicationBuilderSettings settings)
        => WinFormsApplicationBuilder.CreateBuilder(settings);

    /// <summary>
    ///  Gets the lifetime notifications for this application.
    /// </summary>
    public WinFormsApplicationLifetime Lifetime { get; } = new();

    /// <summary>
    ///  Starts the configured host and runs the Windows Forms message loop.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///  No startup form or application context was configured, or this
    ///  application has already been run.
    /// </exception>
    public void Run()
    {
        WinFormsApplicationOptions options = Options;
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeRequested) != 0, this);

        if (Interlocked.CompareExchange(ref _runState, 1, 0) != 0)
        {
            throw new InvalidOperationException("A WinForms application can only be run once.");
        }

        if (_hostStopTask is not null)
        {
            _runState = 2;
            throw new InvalidOperationException("A stopped WinForms application cannot be run.");
        }

        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            _runState = 2;
            throw new InvalidOperationException("The WinForms application must run on an STA thread.");
        }

        if (options.StartupObjectThread is not null
            && !ReferenceEquals(options.StartupObjectThread, Thread.CurrentThread))
        {
            _runState = 2;
            throw new InvalidOperationException(
                "A supplied startup form or application context must be used on its creating thread.");
        }

        bool autoInstall = WindowsFormsSynchronizationContext.AutoInstall;
        SynchronizationContext? originalSynchronizationContext = SynchronizationContext.Current;
        Application.ThreadContext? originalThreadContext = Application.ThreadContext.Current;
        Application.ThreadContext? threadContext = null;
        bool installedSynchronizationContext = false;
        Exception? failure = null;
        WinFormsExceptionLogging? exceptionLogging = null;
        ApplicationContext? applicationContext = null;
        Form? startupForm = null;

        _uiThread = Thread.CurrentThread;

        try
        {
            if (options.EnableExceptionLogging)
            {
                IServiceProvider services = (_host ?? throw new InvalidOperationException(
                    "Exception logging requires a configured Generic Host.")).Services;
                ILogger<WinFormsApplication> logger =
                    services.GetService(typeof(ILogger<WinFormsApplication>)) as ILogger<WinFormsApplication>
                    ?? throw new InvalidOperationException(
                        "The configured Generic Host does not provide ILogger<WinFormsApplication>.");
                exceptionLogging = new WinFormsExceptionLogging(logger);
                exceptionLogging.Start();
            }

            WindowsFormsSynchronizationContext.AutoInstall = true;
            WindowsFormsSynchronizationContext.InstallIfNeeded();
            installedSynchronizationContext =
                originalSynchronizationContext is not WindowsFormsSynchronizationContext
                && SynchronizationContext.Current is WindowsFormsSynchronizationContext;
            threadContext = Application.ThreadContext.FromCurrent();
            _marshallingControl = threadContext.MarshallingControl;

            applicationContext = CreateApplicationContext(options);
            _applicationContext = applicationContext;
            startupForm = applicationContext.MainForm;
            _previousExitThreadHandler = applicationContext.ExitThreadHandler;
            applicationContext.ExitThreadHandler = OnExitThreadRequested;

            InitializeHostLifetime();
            if (_host is null)
            {
                Lifetime.NotifyApplicationStarted();
            }
            else
            {
                applicationContext.MainForm = null;
                StartHost(applicationContext, startupForm);
            }

            bool enterMessageLoop;
            lock (_stopLock)
            {
                enterMessageLoop = Volatile.Read(ref _allowThreadExit) == 0;
                if (enterMessageLoop)
                {
                    Volatile.Write(ref _messageLoopEntered, 1);
                }
            }

            if (enterMessageLoop)
            {
                Application.Run(applicationContext);
            }
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            NotifyApplicationStopping();
            threadContext ??= Application.ThreadContext.Current;

            if (applicationContext is not null)
            {
                if (threadContext?.IsDisposed != true
                    && applicationContext.MainForm is null
                    && startupForm is not null)
                {
                    applicationContext.MainForm = startupForm;
                }

                applicationContext.ExitThreadHandler = _previousExitThreadHandler;
                _applicationContext = null;

                if (ReferenceEquals(threadContext?.ApplicationContext, applicationContext)
                    && threadContext?.IsDisposed == false)
                {
                    try
                    {
                        threadContext.Dispose(postQuit: false, disposing: true);
                    }
                    catch (Exception exception)
                    {
                        failure = CombineFailures(failure, exception);
                    }
                }
                else if (threadContext?.IsDisposed != true)
                {
                    try
                    {
                        applicationContext.Dispose();
                    }
                    catch (Exception exception)
                    {
                        failure = CombineFailures(failure, exception);
                    }
                }
            }

            if (originalThreadContext is null && threadContext is { IsDisposed: false })
            {
                try
                {
                    threadContext.Dispose(postQuit: false, disposing: true);
                }
                catch (Exception exception)
                {
                    failure = CombineFailures(failure, exception);
                }
            }

            try
            {
                DisposeActivatedForms();
            }
            catch (Exception exception)
            {
                failure = CombineFailures(failure, exception);
            }

            try
            {
                AwaitFormScopeDisposals();
            }
            catch (Exception exception)
            {
                failure = CombineFailures(failure, exception);
            }

            if (_hostStopTask is not null
                || (Volatile.Read(ref _hostStartAttempted) != 0
                    && Volatile.Read(ref _hostStopped) == 0))
            {
                try
                {
                    StopHostAfterUnexpectedLoopExit();
                }
                catch (Exception exception)
                {
                    failure = CombineFailures(failure, exception);
                }
            }

            try
            {
                Lifetime.NotifyApplicationStopped();
            }
            catch (Exception exception)
            {
                failure = CombineFailures(failure, exception);
            }

            _applicationStoppingRegistration.Dispose();
            _applicationStoppedRegistration.Dispose();

            if (installedSynchronizationContext)
            {
                WindowsFormsSynchronizationContext.Uninstall(turnOffAutoInstall: false);
            }

            if (!ReferenceEquals(SynchronizationContext.Current, originalSynchronizationContext))
            {
                SynchronizationContext.SetSynchronizationContext(originalSynchronizationContext);
            }

            WindowsFormsSynchronizationContext.AutoInstall = autoInstall;

            failure = CombineFailures(failure, Interlocked.Exchange(ref _runtimeException, null));

            if (exceptionLogging is not null)
            {
                try
                {
                    if (failure is not null)
                    {
                        exceptionLogging.LogApplicationFailure(failure);
                    }
                }
                catch (Exception exception)
                {
                    failure = CombineFailures(failure, exception);
                }
                finally
                {
                    try
                    {
                        exceptionLogging.Dispose();
                    }
                    catch (Exception exception)
                    {
                        failure = CombineFailures(failure, exception);
                    }
                }
            }

            try
            {
                DisposeHost();
            }
            catch (Exception exception)
            {
                failure = CombineFailures(failure, exception);
            }

            if (Volatile.Read(ref _disposeRequested) != 0)
            {
                Interlocked.Exchange(ref _options, null);
            }

            Volatile.Write(ref _runState, 2);
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    /// <summary>
    ///  Stops the associated host and exits the application's message loop.
    /// </summary>
    /// <param name="cancellationToken">
    ///  A token that can cancel the host's cooperative shutdown operation.
    /// </param>
    /// <returns>A task that completes after the host stop request completes.</returns>
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _runState) == 2)
        {
            return Task.CompletedTask;
        }

        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeRequested) != 0, this);
        Task stopTask = EnsureHostStopStarted(cancellationToken);

        return cancellationToken.CanBeCanceled
            ? stopTask.WaitAsync(cancellationToken)
            : stopTask;
    }

    /// <summary>
    ///  Creates a modeless form using a new activation scope.
    /// </summary>
    /// <typeparam name="TForm">The type of form to create.</typeparam>
    /// <param name="factory">A factory that creates the form using its activation services.</param>
    /// <returns>
    ///  The activated form. Its scope remains alive until the form is disposed;
    ///  the application disposes forms still open when its message loop ends.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The application has been disposed.</exception>
    /// <exception cref="InvalidOperationException">
    ///  The application is not running on its UI thread, its host cannot create
    ///  scopes, or the factory returned null.
    /// </exception>
    public TForm CreateForm<TForm>(Func<IServiceProvider, TForm> factory)
        where TForm : Form
    {
        ArgumentNullException.ThrowIfNull(factory);
        EnsureActivationThread();

        return ActivateForm(factory, out _);
    }

    /// <summary>
    ///  Shows a modal form using a new activation scope and disposes both the
    ///  form and scope when the modal operation completes.
    /// </summary>
    /// <typeparam name="TForm">The type of form to create.</typeparam>
    /// <param name="factory">A factory that creates the form using its activation services.</param>
    /// <param name="owner">The window that owns the modal form, or null.</param>
    /// <returns>The dialog result returned by <see cref="Form.ShowDialog(IWin32Window?)"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The application has been disposed.</exception>
    /// <exception cref="InvalidOperationException">
    ///  The application is not running on its UI thread, its host cannot create
    ///  scopes, or the factory returned null.
    /// </exception>
    public DialogResult ShowDialog<TForm>(
        Func<IServiceProvider, TForm> factory,
        IWin32Window? owner = null)
        where TForm : Form
    {
        ArgumentNullException.ThrowIfNull(factory);
        EnsureActivationThread();

        TForm form = ActivateForm(factory, out WinFormsFormScope formScope);
        DialogResult result = DialogResult.None;
        Exception? failure = null;

        try
        {
            result = form.ShowDialog(owner);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        try
        {
            formScope.DisposeForm();
            formScope.DisposalTask.GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            failure = CombineFailures(failure, exception);
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return result;
    }

    /// <summary>
    ///  Stops the application if it is running and releases the associated host,
    ///  unless shutdown was initiated directly through a supplied host.
    /// </summary>
    /// <remarks>
    ///  <para>
    ///   The host is stopped and disposed when the message loop exits.
    ///   Disposing an application that has not run also releases its host.
    ///  </para>
    /// </remarks>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeRequested, 1) != 0)
        {
            return;
        }

        if (Volatile.Read(ref _runState) == 1)
        {
            _ = EnsureApplicationStoppingNotified();
            _ = EnsureHostStopStarted(CancellationToken.None);
            return;
        }

        try
        {
            DisposeHost();
        }
        finally
        {
            Interlocked.Exchange(ref _options, null);
        }
    }

    internal WinFormsApplicationOptions Options
        => _options ?? throw new ObjectDisposedException(nameof(WinFormsApplication));

    private ApplicationContext CreateApplicationContext(WinFormsApplicationOptions options)
    {
        if (options.StartupServiceFormFactory is not null)
        {
            return new ApplicationContext(ActivateForm(options.StartupServiceFormFactory, out _));
        }

        if (options.StartupFormFactory is not null)
        {
            return new ApplicationContext(options.StartupFormFactory());
        }

        if (options.StartupForm is not null)
        {
            return new ApplicationContext(options.StartupForm);
        }

        if (options.ApplicationContextFactory is not null)
        {
            return options.ApplicationContextFactory()
                ?? throw new InvalidOperationException("The application context factory returned null.");
        }

        return options.ApplicationContext
            ?? throw new InvalidOperationException(
                "Configure a startup form or application context before running the application.");
    }

    private TForm ActivateForm<TForm>(
        Func<IServiceProvider, TForm> factory,
        out WinFormsFormScope formScope)
        where TForm : Form
    {
        formScope = CreateFormScope();
        TForm? form = null;

        try
        {
            form = factory(formScope.Services)
                ?? throw new InvalidOperationException("The form factory returned null.");
            formScope.Attach(form);

            return form;
        }
        catch (Exception activationException)
        {
            Exception failure = activationException;

            if (form is not null)
            {
                try
                {
                    form.Dispose();
                }
                catch (Exception exception)
                {
                    failure = CombineFailures(failure, exception)!;
                }
            }

            try
            {
                formScope.Dispose();
                formScope.DisposalTask.GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                failure = CombineFailures(failure, exception)!;
            }

            ExceptionDispatchInfo.Capture(failure).Throw();
            throw;
        }
    }

    private WinFormsFormScope CreateFormScope()
    {
        IServiceScope? serviceScope = null;
        IServiceProvider services = EmptyServiceProvider.Instance;

        if (_host is not null)
        {
            IServiceProvider rootServices = _host.Services;

            if (rootServices.GetService(typeof(IServiceScopeFactory)) is not IServiceScopeFactory scopeFactory)
            {
                throw new InvalidOperationException(
                    "The configured host service provider does not support creating activation scopes.");
            }

            serviceScope = scopeFactory.CreateScope()
                ?? throw new InvalidOperationException("The service scope factory returned null.");

            try
            {
                services = serviceScope.ServiceProvider
                    ?? throw new InvalidOperationException("The service scope returned a null service provider.");
            }
            catch (Exception activationException)
            {
                Exception failure = activationException;

                try
                {
                    WinFormsFormScope.DisposeServiceScopeAsync(serviceScope).GetAwaiter().GetResult();
                }
                catch (Exception disposalException)
                {
                    failure = CombineFailures(failure, disposalException)!;
                }

                ExceptionDispatchInfo.Capture(failure).Throw();
                throw;
            }
        }

        WinFormsFormScope formScope = new(serviceScope, services, OnFormScopeDisposed);

        lock (_formScopesLock)
        {
            _formScopes.Add(formScope);
        }

        return formScope;
    }

    private void EnsureActivationThread()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeRequested) != 0, this);

        if (Volatile.Read(ref _runState) != 1)
        {
            throw new InvalidOperationException("Forms can only be activated while the application is running.");
        }

        if (!ReferenceEquals(Thread.CurrentThread, _uiThread))
        {
            throw new InvalidOperationException("Forms must be activated on the application UI thread.");
        }
    }

    private void OnFormScopeDisposed(WinFormsFormScope formScope)
    {
        lock (_formScopesLock)
        {
            _formScopes.Remove(formScope);
            _formScopeDisposalTasks.Add(formScope.DisposalTask);
        }
    }

    private void DisposeActivatedForms()
    {
        WinFormsFormScope[] formScopes;

        lock (_formScopesLock)
        {
            formScopes = [.. _formScopes];
        }

        Exception? failure = null;

        foreach (WinFormsFormScope formScope in formScopes)
        {
            try
            {
                formScope.DisposeForm();
            }
            catch (Exception exception)
            {
                failure = CombineFailures(failure, exception);
            }
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private void AwaitFormScopeDisposals()
    {
        Task[] disposalTasks;

        lock (_formScopesLock)
        {
            disposalTasks = [.. _formScopeDisposalTasks];
            _formScopeDisposalTasks.Clear();
        }

        Task.WhenAll(disposalTasks).GetAwaiter().GetResult();
    }

    private void InitializeHostLifetime()
    {
        if (_host is null)
        {
            return;
        }

        _hostLifetime = _host.Services.GetService(typeof(IHostApplicationLifetime))
            as IHostApplicationLifetime;

        if (_hostLifetime is null)
        {
            return;
        }

        _applicationStoppingRegistration = _hostLifetime.ApplicationStopping.Register(
            static state => ((WinFormsApplication)state!).OnHostStopping(),
            this);
        _applicationStoppedRegistration = _hostLifetime.ApplicationStopped.Register(
            static state => ((WinFormsApplication)state!).OnHostStopped(),
            this);

        if (_hostLifetime.ApplicationStopped.IsCancellationRequested)
        {
            Volatile.Write(ref _hostStopped, 1);
            if (_options?.HostWasSupplied == true)
            {
                Volatile.Write(ref _hostOwnershipTransferred, 1);
            }

            _hostStoppedSource.TrySetResult();
        }
    }

    private void StartHost(ApplicationContext applicationContext, Form? startupForm)
    {
        if (_hostLifetime?.ApplicationStopped.IsCancellationRequested == true
            || _hostLifetime?.ApplicationStopping.IsCancellationRequested == true)
        {
            throw new InvalidOperationException("The configured Generic Host is already stopping or has stopped.");
        }

        Volatile.Write(ref _hostStartAttempted, 1);

        _marshallingControl!.BeginInvoke(
            (Action)(() => StartHostAsync(applicationContext, startupForm)));
    }

    private async void StartHostAsync(ApplicationContext applicationContext, Form? startupForm)
    {
        try
        {
            if (_hostLifetime?.ApplicationStarted.IsCancellationRequested != true)
            {
                await _host!.StartAsync().ConfigureAwait(true);
            }

            Lifetime.NotifyApplicationStarted();

            if (Volatile.Read(ref _allowThreadExit) == 0 && startupForm is not null)
            {
                applicationContext.MainForm = startupForm;
                startupForm.Visible = true;
            }
        }
        catch (Exception exception)
        {
            RecordRuntimeException(exception);
            _ = EnsureHostStopStarted(CancellationToken.None);
        }
    }

    private bool OnExitThreadRequested(ApplicationContext context)
    {
        if (_previousExitThreadHandler is not null
            && !_previousExitThreadHandler(context))
        {
            return false;
        }

        if (Volatile.Read(ref _allowThreadExit) != 0)
        {
            return true;
        }

        NotifyApplicationStopping();

        if (_host is null)
        {
            AllowThreadExit();
            return true;
        }

        if (Volatile.Read(ref _hostStopped) != 0
            || _hostLifetime?.ApplicationStopped.IsCancellationRequested == true)
        {
            AllowThreadExit();
            return true;
        }

        _ = EnsureHostStopStarted(CancellationToken.None);

        return false;
    }

    private void OnHostStopping()
    {
        _ = EnsureApplicationStoppingNotified();
    }

    private void OnHostStopped()
    {
        lock (_stopLock)
        {
            Volatile.Write(ref _hostStopped, 1);
            _hostStoppedSource.TrySetResult();

            if (_hostStopTask is null
                && _host is not null
                && _options?.HostWasSupplied == true)
            {
                Volatile.Write(ref _hostOwnershipTransferred, 1);
            }
        }

        if (Volatile.Read(ref _hostStopRequestedByApplication) == 0)
        {
            AllowThreadExit();
            RequestThreadExit();
        }
    }

    private void NotifyApplicationStopping()
    {
        try
        {
            Lifetime.NotifyApplicationStopping();
        }
        catch (Exception exception)
        {
            RecordRuntimeException(exception);
        }
        finally
        {
            _applicationStoppingSource.TrySetResult();
        }
    }

    private Task EnsureApplicationStoppingNotified()
    {
        if (_applicationStoppingSource.Task.IsCompleted)
        {
            return _applicationStoppingSource.Task;
        }

        if (_uiThread is null || ReferenceEquals(Thread.CurrentThread, _uiThread))
        {
            NotifyApplicationStopping();
            return _applicationStoppingSource.Task;
        }

        if (Interlocked.CompareExchange(ref _applicationStoppingDispatchScheduled, 1, 0) == 0
            && _marshallingControl is { IsDisposed: false, IsHandleCreated: true } marshallingControl)
        {
            try
            {
                marshallingControl.BeginInvoke((Action)NotifyApplicationStopping);
            }
            catch (Exception exception)
            {
                RecordRuntimeException(exception);
            }
        }

        return _applicationStoppingSource.Task;
    }

    private Task EnsureHostStopStarted(CancellationToken cancellationToken)
    {
        Task applicationStoppingTask = EnsureApplicationStoppingNotified();

        lock (_stopLock)
        {
            if (_hostStopTask is not null)
            {
                return _hostStopTask;
            }

            if (_host is null)
            {
                Volatile.Write(ref _hostStopped, 1);
                Volatile.Write(ref _allowThreadExit, 1);
                _hostStoppedSource.TrySetResult();
                _hostStopTask = Task.CompletedTask;
                RequestThreadExit();
                return _hostStopTask;
            }

            Volatile.Write(ref _hostStopRequestedByApplication, 1);
            _hostStopTask = StopHostAfterStoppingAsync(
                applicationStoppingTask,
                cancellationToken);
            return _hostStopTask;
        }
    }

    private async Task StopHostAfterStoppingAsync(
        Task applicationStoppingTask,
        CancellationToken cancellationToken)
    {
        await applicationStoppingTask.ConfigureAwait(false);
        await Task.Run(
            () => StopHostAsync(cancellationToken),
            CancellationToken.None).ConfigureAwait(false);
    }

    private async Task StopHostAsync(CancellationToken cancellationToken)
    {
        Exception? failure = null;

        try
        {
            if (_host is not null)
            {
                await _host.StopAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            failure = exception;
            RecordRuntimeException(exception);
        }
        finally
        {
            lock (_stopLock)
            {
                Volatile.Write(ref _hostStopped, 1);
                _hostStoppedSource.TrySetResult();
            }

            AllowThreadExit();
            RequestThreadExit();
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private void AllowThreadExit()
    {
        lock (_stopLock)
        {
            Volatile.Write(ref _allowThreadExit, 1);
        }
    }

    private void StopHostAfterUnexpectedLoopExit()
    {
        EnsureHostStopStarted(CancellationToken.None).GetAwaiter().GetResult();
    }

    private void RequestThreadExit()
    {
        ApplicationContext? context = _applicationContext;

        if (Volatile.Read(ref _messageLoopEntered) == 0)
        {
            return;
        }

        if (context is null)
        {
            return;
        }

        try
        {
            if (ReferenceEquals(Thread.CurrentThread, _uiThread))
            {
                context.ExitThread();
            }
            else
            {
                _marshallingControl!.BeginInvoke(context.ExitThread);
            }
        }
        catch (Exception exception)
        {
            RecordRuntimeException(exception);
        }
    }

    private void RecordRuntimeException(Exception exception)
    {
        lock (_stopLock)
        {
            _runtimeException = CombineFailures(_runtimeException, exception);
        }
    }

    private static Exception? CombineFailures(Exception? first, Exception? second)
    {
        if (first is null)
        {
            return second;
        }

        if (second is null || ReferenceEquals(first, second))
        {
            return first;
        }

        return new AggregateException(first, second);
    }

    private void DisposeHost()
    {
        IHost? host = Interlocked.Exchange(ref _host, null);

        try
        {
            if (Volatile.Read(ref _hostOwnershipTransferred) == 0)
            {
                host?.Dispose();
            }
        }
        finally
        {
            _options?.Host = null;
        }
    }

    /// <summary>
    ///  Provides no services when the application is not associated with a host.
    /// </summary>
    private sealed class EmptyServiceProvider : IServiceProvider
    {
        /// <summary>
        ///  Gets the shared empty service provider.
        /// </summary>
        internal static EmptyServiceProvider Instance { get; } = new();

        /// <summary>
        ///  Returns null because no services are registered without a host.
        /// </summary>
        /// <param name="serviceType">The requested service type.</param>
        /// <returns>Always null.</returns>
        public object? GetService(Type serviceType) => null;
    }
}
