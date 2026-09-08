// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Windows.Forms;
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Owns the single WinForms UI thread and coordinates it with Generic Host shutdown.
/// </summary>
internal sealed partial class WinFormsHostedService : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly IHostApplicationLifetime _hostLifetime;
    private readonly WinFormsApplicationLifetime _applicationLifetime;
    private readonly WinFormsApplicationOptions _options;
    private readonly ILogger<WinFormsHostedService> _logger;
    private readonly TaskCompletionSource<object?> _uiStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<object?> _uiStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _syncRoot = new();
    private Thread? _uiThread;
    private WindowsFormsSynchronizationContext? _synchronizationContext;
    private ApplicationContext? _applicationContext;
    private int _stopPosted;

    internal WinFormsHostedService(
        IServiceProvider services,
        IHostApplicationLifetime hostLifetime,
        WinFormsApplicationLifetime applicationLifetime,
        WinFormsApplicationOptions options,
        ILogger<WinFormsHostedService> logger)
    {
        _services = services;
        _hostLifetime = hostLifetime;
        _applicationLifetime = applicationLifetime;
        _options = options;
        _logger = logger;
    }

    internal Exception? Exception { get; private set; }

    internal void SetStartupForm<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TForm>()
        where TForm : Form
    {
        lock (_syncRoot)
        {
            if (_uiThread is not null)
            {
                throw new InvalidOperationException("The startup form cannot be changed after the host has started.");
            }

            _options.SetStartupFormFactory(serviceProvider =>
                ActivatorUtilities.CreateInstance<TForm>(serviceProvider));
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_syncRoot)
        {
            if (_uiThread is not null)
            {
                throw new InvalidOperationException("The WinForms lifetime can only be started once.");
            }

            _uiThread = new Thread(RunMessageLoop)
            {
                IsBackground = false,
                Name = "Windows Forms UI Thread"
            };
            _uiThread.SetApartmentState(ApartmentState.STA);
            _uiThread.Start();
        }

        await _uiStarted.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        PostExitThread();
        await _uiStopped.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private void RunMessageLoop()
    {
        try
        {
            InitializeApplication();

            _synchronizationContext = new WindowsFormsSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(_synchronizationContext);
            _applicationLifetime.SynchronizationContext = _synchronizationContext;

            _applicationContext = CreateApplicationContext();
            Application.ApplicationExit += OnApplicationExit;
            _hostLifetime.ApplicationStopping.Register(PostExitThread);
            _uiStarted.TrySetResult(null);

            Application.Run(_applicationContext);
        }
        catch (Exception exception)
        {
            Exception = exception;
            LogUiThreadFailure(exception);
            _uiStarted.TrySetException(exception);
            _uiStopped.TrySetException(exception);
            _hostLifetime.StopApplication();
        }
        finally
        {
            Application.ApplicationExit -= OnApplicationExit;
            _applicationLifetime.SynchronizationContext = null;
            _uiStopped.TrySetResult(null);
        }
    }

    private ApplicationContext CreateApplicationContext()
    {
        if (_options.ApplicationContextFactory is not null)
        {
            ApplicationContext context = _options.ApplicationContextFactory(_services);
            return context ?? throw new InvalidOperationException(
                "The configured application context factory returned null.");
        }

        if (_options.StartupFormFactory is not null)
        {
            Form form = _options.StartupFormFactory(_services);
            return form is null
                ? throw new InvalidOperationException("The configured startup form factory returned null.")
                : new ApplicationContext(form);
        }

        return new ApplicationContext();
    }

    private void InitializeApplication()
    {
        if (_options.ColorMode is { } colorMode)
        {
            Application.SetColorMode(colorMode);
        }

        if (_options.EnableVisualStyles)
        {
            Application.EnableVisualStyles();
        }

        Application.SetCompatibleTextRenderingDefault(_options.UseCompatibleTextRenderingDefault);
        Application.SetHighDpiMode(_options.HighDpiMode);

        if (_options.DefaultFont is { } defaultFont)
        {
            Application.SetDefaultFont(defaultFont);
        }
    }

    private void OnApplicationExit(object? sender, EventArgs e)
        => _hostLifetime.StopApplication();

    private void PostExitThread()
    {
        if (Interlocked.Exchange(ref _stopPosted, 1) != 0)
        {
            return;
        }

        WindowsFormsSynchronizationContext? synchronizationContext = _synchronizationContext;
        if (synchronizationContext is null)
        {
            return;
        }

        if (SynchronizationContext.Current == synchronizationContext)
        {
            Application.ExitThread();
            return;
        }

        synchronizationContext.Post(static state => Application.ExitThread(), null);
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "The WinForms UI thread failed.")]
    private partial void LogUiThreadFailure(Exception exception);
}
