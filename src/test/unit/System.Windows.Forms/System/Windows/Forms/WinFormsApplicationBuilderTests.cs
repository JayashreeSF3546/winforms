// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using System.Drawing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.WinForms;
using Microsoft.DotNet.RemoteExecutor;

namespace System.Windows.Forms.Tests;

public class WinFormsApplicationBuilderTests
{
    private static readonly TimeSpan s_waitTimeout = TimeSpan.FromSeconds(30);

    [WinFormsFact]
    public void Constructor_UsesConventionalDefaults()
    {
        WinFormsApplicationBuilder builder = new();

        Assert.True(builder.Options.EnableVisualStyles);
        Assert.Equal(HighDpiMode.SystemAware, builder.Options.HighDpiMode);
        Assert.False(builder.Options.UseCompatibleTextRenderingDefault);
        Assert.NotNull(builder.Services);
        Assert.NotNull(builder.Configuration);
        Assert.NotNull(builder.Logging);
        Assert.NotNull(builder.Environment);
    }

    [WinFormsFact]
    public void UseStartupFormAndUseApplicationContext_AreMutuallyExclusive()
    {
        WinFormsApplicationBuilder builder = new();
        builder.UseStartupForm<BuilderTestForm>();

        Assert.Throws<InvalidOperationException>(
            () => builder.UseApplicationContext<BuilderTestApplicationContext>());
    }

    [WinFormsFact]
    public void UseApplicationContextAndUseStartupForm_AreMutuallyExclusive()
    {
        WinFormsApplicationBuilder builder = new();
        builder.UseApplicationContext<BuilderTestApplicationContext>();

        Assert.Throws<InvalidOperationException>(
            () => builder.UseStartupForm<BuilderTestForm>());
    }

    [WinFormsFact]
    public void Build_CanOnlyBeCalledOnce()
    {
        WinFormsApplicationBuilder builder = new();
        using WinFormsApplication application = builder.Build();

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [WinFormsFact]
    public void Build_Dispose_DoesNotStartMessageLoop()
    {
        WinFormsApplicationBuilder builder = new()
        {
            Options =
            {
                EnableVisualStyles = false
            }
        };

        using WinFormsApplication application = builder.Build();

        Assert.NotNull(application.Services);
        Assert.NotNull(application.Lifetime);
    }

    [WinFormsFact]
    public void ConfigurationMethods_UpdateOptionsAndReturnSameBuilder()
    {
        WinFormsApplicationBuilder builder = new();
        using Font defaultFont = new("Courier New", 9f);

        Assert.Same(builder, builder.UseHighDpiMode(HighDpiMode.PerMonitorV2));
        Assert.Same(builder, builder.UseVisualStyles(false));
        Assert.Same(builder, builder.UseCompatibleTextRenderingDefault(true));
        Assert.Same(builder, builder.UseDefaultFont(defaultFont));
        Assert.Same(builder, builder.UseColorMode(SystemColorMode.Dark));

        Assert.Equal(HighDpiMode.PerMonitorV2, builder.Options.HighDpiMode);
        Assert.False(builder.Options.EnableVisualStyles);
        Assert.True(builder.Options.UseCompatibleTextRenderingDefault);
        Assert.Same(defaultFont, builder.Options.DefaultFont);
        Assert.Equal(SystemColorMode.Dark, builder.Options.ColorMode);
    }

    [WinFormsFact]
    public void UseStartupForm_NullForm_ThrowsArgumentNullException()
    {
        WinFormsApplicationBuilder builder = new();

        Assert.Throws<ArgumentNullException>("startupForm", () => builder.UseStartupForm((Form)null!));
    }

    [WinFormsFact]
    public void UseDefaultFont_NullFont_ThrowsArgumentNullException()
    {
        WinFormsApplicationBuilder builder = new();

        Assert.Throws<ArgumentNullException>("defaultFont", () => builder.UseDefaultFont(null!));
    }

    [WinFormsFact]
    public void UseApplicationContext_NullFactory_ThrowsArgumentNullException()
    {
        WinFormsApplicationBuilder builder = new();

        Assert.Throws<ArgumentNullException>(
            () => builder.UseApplicationContext((Func<IServiceProvider, ApplicationContext>)null!));
    }

    [WinFormsFact]
    public void Build_SnapshotsOptionsSoLaterBuilderChangesAreNotObserved()
    {
        WinFormsApplicationBuilder builder = new();
        builder
            .UseVisualStyles(false)
            .UseHighDpiMode(HighDpiMode.PerMonitorV2)
            .UseCompatibleTextRenderingDefault(true);

        using WinFormsApplication application = builder.Build();
        WinFormsApplicationOptions options = application.Services.GetRequiredService<WinFormsApplicationOptions>();

        builder.Options.EnableVisualStyles = true;
        builder.Options.HighDpiMode = HighDpiMode.DpiUnaware;
        builder.Options.UseCompatibleTextRenderingDefault = false;

        Assert.NotSame(builder.Options, options);
        Assert.False(options.EnableVisualStyles);
        Assert.Equal(HighDpiMode.PerMonitorV2, options.HighDpiMode);
        Assert.True(options.UseCompatibleTextRenderingDefault);
    }

    [WinFormsFact]
    public void Current_TracksMostRecentlyBuiltApplicationAndIsClearedOnDispose()
    {
        WinFormsApplication first = new WinFormsApplicationBuilder().Build();
        try
        {
            Assert.Same(first, WinFormsApplication.Current);

            WinFormsApplication second = new WinFormsApplicationBuilder().Build();
            try
            {
                Assert.Same(second, WinFormsApplication.Current);
            }
            finally
            {
                second.Dispose();
            }

            Assert.Null(WinFormsApplication.Current);
        }
        finally
        {
            first.Dispose();
        }
    }

    [WinFormsFact]
    public async Task Dispose_IsIdempotentAndDisposedApplicationRejectsLifetimeCalls()
    {
        WinFormsApplicationBuilder builder = new();
        WinFormsApplication application = builder.Build();

        application.Dispose();
        application.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => application.RunAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => application.StartAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => application.StopAsync());
    }

    [WinFormsFact]
    public void UseWindowsFormsLifetime_NullBuilder_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            "builder",
            () => ((IHostBuilder)null!).UseWindowsFormsLifetime<BuilderTestForm>());
        Assert.Throws<ArgumentNullException>(
            "builder",
            () => ((IHostApplicationBuilder)null!).UseWindowsFormsLifetime<BuilderTestForm>());
        Assert.Throws<ArgumentNullException>(
            "builder",
            () => ((IHostApplicationBuilder)null!).UseWindowsFormsLifetime<BuilderTestApplicationContext>(
                applicationContextFactory: null));
    }

    [WinFormsFact]
    public void AddWindowsFormsLifetime_NullServices_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            "services",
            () => ((IServiceCollection)null!).AddWindowsFormsLifetime());
        Assert.Throws<ArgumentNullException>(
            "services",
            () => ((IServiceCollection)null!).AddWindowsFormsLifetime<BuilderTestForm>());
        Assert.Throws<ArgumentNullException>(
            "services",
            () => ((IServiceCollection)null!).AddWindowsFormsLifetime<BuilderTestApplicationContext>(
                applicationContextFactory: null));
    }

    [WinFormsFact]
    public void UseWindowsFormsLifetime_OnHostApplicationBuilder_RegistersSingleCoordinator()
    {
        HostApplicationBuilder hostBuilder = Host.CreateEmptyApplicationBuilder(settings: null);
        hostBuilder.Services.AddLogging();
        hostBuilder.UseWindowsFormsLifetime<BuilderTestForm>(options => options.EnableVisualStyles = false);

        using IHost host = hostBuilder.Build();

        Assert.Single(host.Services.GetServices<IHostedService>());
        Assert.NotNull(host.Services.GetRequiredService<WinFormsApplicationLifetime>());
        Assert.False(host.Services.GetRequiredService<WinFormsApplicationOptions>().EnableVisualStyles);
    }

    [WinFormsFact]
    public void UseWindowsFormsLifetime_OnHostBuilder_RegistersSingleCoordinator()
    {
        IHostBuilder hostBuilder = new HostBuilder()
            .ConfigureServices(services => services.AddLogging())
            .UseWindowsFormsLifetime<BuilderTestForm>(options => options.HighDpiMode = HighDpiMode.PerMonitorV2);

        using IHost host = hostBuilder.Build();

        Assert.Single(host.Services.GetServices<IHostedService>());
        Assert.NotNull(host.Services.GetRequiredService<WinFormsApplicationLifetime>());
        Assert.Equal(
            HighDpiMode.PerMonitorV2,
            host.Services.GetRequiredService<WinFormsApplicationOptions>().HighDpiMode);
    }

    [Fact]
    public void Run_UsesDedicatedStaThreadAndPropagatesFormShutdown()
    {
        RemoteExecutor.Invoke(RunFormAndAssertLifetimeEvents).Dispose();
    }

    [Fact]
    public void Run_UsesCustomApplicationContext()
    {
        RemoteExecutor.Invoke(RunContextAndAssertLifetimeEvents).Dispose();
    }

    [Fact]
    public void Run_AppliesApplicationInitializationBeforeStartupFormIsCreated()
    {
        RemoteExecutor.Invoke(RunAndAssertInitializationOrder).Dispose();
    }

    [Fact]
    public void Run_PublishesWindowsFormsSynchronizationContextOnUiThread()
    {
        RemoteExecutor.Invoke(RunAndAssertUiThreadContract).Dispose();
    }

    [Fact]
    public void Run_FormClose_RequestsHostShutdownExactlyOnce()
    {
        RemoteExecutor.Invoke(RunAndAssertSingleShutdownRequest).Dispose();
    }

    [Fact]
    public void Run_HostStopApplication_ExitsMessageLoop()
    {
        RemoteExecutor.Invoke(RunAndAssertHostInitiatedShutdown).Dispose();
    }

    [Fact]
    public void Run_HostedService_ObservesCancellationOnShutdown()
    {
        RemoteExecutor.Invoke(RunAndAssertHostedWorkCancellation).Dispose();
    }

    [Fact]
    public void Run_StartupFactoryThrows_FailsStartupAndReleasesUiThread()
    {
        RemoteExecutor.Invoke(RunAndAssertStartupFailure).Dispose();
    }

    [Fact]
    public void ApplicationRun_WithHostingAssemblyLoaded_RemainsConventional()
    {
        RemoteExecutor.Invoke(RunConventionalMessageLoopAndAssertUnchangedBehavior).Dispose();
    }

    private static void RunFormAndAssertLifetimeEvents()
    {
        List<string> events = [];
        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder();
        builder.UseStartupForm<LifetimeTestForm>();

        using WinFormsApplication application = builder.Build();
        application.Lifetime.ApplicationStarted += (_, _) =>
        {
            application.Lifetime.SynchronizationContext!.Post(
                static _ =>
                {
                    Assert.Equal(ApartmentState.STA, Thread.CurrentThread.GetApartmentState());
                    Form form = Assert.IsAssignableFrom<Form>(Application.OpenForms[0]);
                    form.Close();
                },
                null);
            events.Add("started");
        };
        application.Lifetime.ApplicationStopping += (_, _) => events.Add("stopping");
        application.Lifetime.ApplicationStopped += (_, _) => events.Add("stopped");

        application.Run();

        Assert.Equal(["started", "stopping", "stopped"], events);
    }

    private static void RunContextAndAssertLifetimeEvents()
    {
        List<string> events = [];
        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder();
        builder.UseApplicationContext<ExitingApplicationContext>();

        using WinFormsApplication application = builder.Build();
        application.Lifetime.ApplicationStarted += (_, _) =>
        {
            application.Lifetime.SynchronizationContext!.Post(
                static _ =>
                {
                    Assert.Equal(ApartmentState.STA, Thread.CurrentThread.GetApartmentState());
                    Application.ExitThread();
                },
                null);
            events.Add("started");
        };
        application.Lifetime.ApplicationStopping += (_, _) => events.Add("stopping");
        application.Lifetime.ApplicationStopped += (_, _) => events.Add("stopped");

        application.Run();

        Assert.Equal(["started", "stopping", "stopped"], events);
    }

    private static void RunAndAssertInitializationOrder()
    {
        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder();
        builder
            .UseVisualStyles()
            .UseHighDpiMode(HighDpiMode.SystemAware)
            .UseDefaultFont(new Font("Courier New", 9f))
            .UseStartupForm<InitializationProbeForm>();

        using WinFormsApplication application = builder.Build();
        application.Lifetime.ApplicationStarted += (_, _) =>
            application.Lifetime.SynchronizationContext!.Post(
                static _ => Application.ExitThread(),
                null);

        application.Run();

        // Application initialization must be complete before the startup form - and therefore any
        // control handle - is created.
        Assert.True(InitializationProbeForm.Constructed);
        Assert.True(InitializationProbeForm.UseVisualStylesAtConstruction);
        Assert.Equal("Courier New", InitializationProbeForm.FontFamilyNameAtConstruction);
        Assert.Equal(HighDpiMode.SystemAware, InitializationProbeForm.HighDpiModeAtConstruction);
        Assert.False(InitializationProbeForm.HandleCreatedAtConstruction);
        Assert.True(InitializationProbeForm.HandleWasCreated);
        Assert.True(InitializationProbeForm.UseVisualStylesAtHandleCreation);
    }

    private static void RunAndAssertUiThreadContract()
    {
        int callerThreadId = Environment.CurrentManagedThreadId;
        int uiThreadId = 0;
        string? uiThreadName = null;
        ApartmentState uiApartmentState = ApartmentState.Unknown;
        bool messageLoopOnUiThread = false;
        bool winFormsContextPublished = false;
        bool winFormsContextCurrentOnUiThread = false;

        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder();
        using WinFormsApplication application = builder.Build();
        application.Lifetime.ApplicationStarted += (_, _) =>
        {
            SynchronizationContext context = application.Lifetime.SynchronizationContext!;
            winFormsContextPublished = context is WindowsFormsSynchronizationContext;

            using ManualResetEventSlim posted = new();

            // Post from a thread-pool thread to verify that hosted work marshals to the UI thread.
            Task.Run(() => context.Post(
                _ =>
                {
                    uiThreadId = Environment.CurrentManagedThreadId;
                    uiThreadName = Thread.CurrentThread.Name;
                    uiApartmentState = Thread.CurrentThread.GetApartmentState();
                    messageLoopOnUiThread = Application.MessageLoop;
                    winFormsContextCurrentOnUiThread =
                        SynchronizationContext.Current is WindowsFormsSynchronizationContext;
                    posted.Set();
                },
                null));

            Assert.True(posted.Wait(s_waitTimeout));
            context.Post(static _ => Application.ExitThread(), null);
        };

        application.Run();

        Assert.True(winFormsContextPublished);
        Assert.True(winFormsContextCurrentOnUiThread);
        Assert.True(messageLoopOnUiThread);
        Assert.Equal(ApartmentState.STA, uiApartmentState);
        Assert.Equal("Windows Forms UI Thread", uiThreadName);
        Assert.NotEqual(callerThreadId, uiThreadId);

        // The synchronization context is released once the UI loop has returned, so late work
        // cannot be marshalled to a dead UI thread.
        Assert.Null(application.Lifetime.SynchronizationContext);
    }

    private static void RunAndAssertSingleShutdownRequest()
    {
        int startedCount = 0;
        int stoppingCount = 0;
        int stoppedCount = 0;

        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder();
        builder.UseStartupForm<LifetimeTestForm>();

        using WinFormsApplication application = builder.Build();
        application.Lifetime.ApplicationStarted += (_, _) =>
        {
            Interlocked.Increment(ref startedCount);
            application.Lifetime.SynchronizationContext!.Post(
                static _ =>
                {
                    Form form = Assert.IsAssignableFrom<Form>(Application.OpenForms[0]);
                    Assert.True(form.IsHandleCreated);
                    form.Close();
                },
                null);
        };
        application.Lifetime.ApplicationStopping += (_, _) => Interlocked.Increment(ref stoppingCount);
        application.Lifetime.ApplicationStopped += (_, _) => Interlocked.Increment(ref stoppedCount);

        application.Run();

        // Closing the startup form must request host shutdown exactly once, even though both
        // Application.ApplicationExit and host stopping participate in the handshake.
        Assert.Equal(1, startedCount);
        Assert.Equal(1, stoppingCount);
        Assert.Equal(1, stoppedCount);
        Assert.Empty(Application.OpenForms);
        Assert.True(application.Lifetime.ApplicationStoppedToken.IsCancellationRequested);
    }

    private static void RunAndAssertHostInitiatedShutdown()
    {
        List<string> events = [];
        using ManualResetEventSlim started = new();

        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder();
        using WinFormsApplication application = builder.Build();
        application.Lifetime.ApplicationStarted += (_, _) =>
        {
            events.Add("started");
            started.Set();
        };
        application.Lifetime.ApplicationStopping += (_, _) => events.Add("stopping");
        application.Lifetime.ApplicationStopped += (_, _) => events.Add("stopped");

        // Request shutdown from outside the UI thread, which is what a host-initiated stop does.
        Task stopRequest = Task.Run(() =>
        {
            Assert.True(started.Wait(s_waitTimeout));
            application.Lifetime.StopApplication();
        });

        application.Run();
        stopRequest.GetAwaiter().GetResult();

        Assert.Equal(["started", "stopping", "stopped"], events);
        Assert.True(application.Lifetime.ApplicationStoppedToken.IsCancellationRequested);
        Assert.Null(application.Lifetime.SynchronizationContext);
    }

    private static void RunAndAssertHostedWorkCancellation()
    {
        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder();
        builder.Services.AddHostedService<CancellationProbeService>();

        using WinFormsApplication application = builder.Build();
        application.Lifetime.ApplicationStarted += (_, _) =>
        {
            Assert.True(CancellationProbeService.Started.Wait(s_waitTimeout));
            application.Lifetime.StopApplication();
        };

        application.Run();

        Assert.True(CancellationProbeService.Cancelled.Wait(s_waitTimeout));
        Assert.True(CancellationProbeService.CancellationObserved);
    }

    private static void RunAndAssertStartupFailure()
    {
        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder();
        builder.Logging.AddProvider(new CapturingLoggerProvider());
        builder.UseApplicationContext(
            _ => throw new InvalidOperationException("Startup failure probe."));

        using WinFormsApplication application = builder.Build();

        // Startup must fail instead of reporting success, and the UI thread must terminate.
        // A retained foreground UI thread would prevent this process from exiting.
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(application.Run);

        Assert.Equal("Startup failure probe.", exception.Message);
        Assert.Empty(Application.OpenForms);
        Assert.Null(application.Lifetime.SynchronizationContext);

        // The failure is surfaced through the logging pipeline as an error entry that carries the
        // original exception.
        Assert.Contains(
            CapturingLoggerProvider.Errors,
            entry => entry.Contains("The WinForms UI thread failed.", StringComparison.Ordinal)
                && entry.Contains("Startup failure probe.", StringComparison.Ordinal));
    }

    private static void RunConventionalMessageLoopAndAssertUnchangedBehavior()
    {
        // The hosting assembly is loaded in this process, but it must not affect a conventional
        // Application.Run application that does not opt in.
        Assert.Null(WinFormsApplication.Current);

        Exception? threadException = null;
        int openFormsAfterRun = -1;

        Thread uiThread = new(() =>
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                using Form form = new();
                form.Shown += static (sender, _) => ((Form)sender!).Close();
                Application.Run(form);

                openFormsAfterRun = Application.OpenForms.Count;
            }
            catch (Exception exception)
            {
                threadException = exception;
            }
        });

        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();
        Assert.True(uiThread.Join(s_waitTimeout));

        Assert.Null(threadException);
        Assert.Equal(0, openFormsAfterRun);
        Assert.Null(WinFormsApplication.Current);
    }

    private sealed class BuilderTestForm : Form
    {
    }

    private sealed class BuilderTestApplicationContext : ApplicationContext
    {
    }

    private sealed class LifetimeTestForm : Form
    {
    }

    private sealed class ExitingApplicationContext : ApplicationContext
    {
    }

    /// <summary>
    ///  Records the application state observed while the startup form is created.
    /// </summary>
    private sealed class InitializationProbeForm : Form
    {
        public InitializationProbeForm()
        {
            Constructed = true;
            UseVisualStylesAtConstruction = Application.UseVisualStyles;
            HighDpiModeAtConstruction = Application.HighDpiMode;
            FontFamilyNameAtConstruction = Font.FontFamily.Name;
            HandleCreatedAtConstruction = IsHandleCreated;
        }

        internal static bool Constructed { get; private set; }

        internal static bool UseVisualStylesAtConstruction { get; private set; }

        internal static HighDpiMode HighDpiModeAtConstruction { get; private set; }

        internal static string? FontFamilyNameAtConstruction { get; private set; }

        internal static bool HandleCreatedAtConstruction { get; private set; }

        internal static bool HandleWasCreated { get; private set; }

        internal static bool UseVisualStylesAtHandleCreation { get; private set; }

        protected override void OnHandleCreated(EventArgs e)
        {
            HandleWasCreated = true;
            UseVisualStylesAtHandleCreation = Application.UseVisualStyles;
            base.OnHandleCreated(e);
        }
    }

    /// <summary>
    ///  Captures error-level diagnostics emitted by the hosting layer.
    /// </summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        internal static ConcurrentQueue<string> Errors { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger();

        public void Dispose()
        {
        }

        private sealed class CapturingLogger : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel))
                {
                    Errors.Enqueue($"{formatter(state, exception)} {exception?.Message}");
                }
            }
        }
    }

    /// <summary>
    ///  Records whether hosted background work observes cancellation during shutdown.
    /// </summary>
    private sealed class CancellationProbeService : BackgroundService
    {
        internal static ManualResetEventSlim Started { get; } = new();

        internal static ManualResetEventSlim Cancelled { get; } = new();

        internal static bool CancellationObserved { get; private set; }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Started.Set();

            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                Cancelled.Set();
            }
        }
    }
}
