// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.WinForms;

namespace System.Windows.Forms.Tests;

/// <summary>
///  Verifies shutdown ordering against the real Generic Host rather than a synthetic host lifetime.
/// </summary>
[Collection("Sequential")]
public class WinFormsApplicationHostingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_ExplicitHealthReadiness_PrecedesUiAndExcludesOfflineDiagnostics(bool ready)
    {
        RunOnSta(() =>
        {
            int uiThread = Environment.CurrentManagedThreadId;
            int activated = 0;
            int checkedReadiness = 0;
            int optionalChecks = 0;
            InvalidOperationException expected = new("Required local readiness failed.");
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseApplicationContext(() =>
                {
                    checkedReadiness.Should().Be(1);
                    activated++;

                    return new ApplicationContext();
                })
                .UseHost(new HostBuilder().ConfigureServices((_, services) =>
                {
                    services.AddWinFormsHealthDiagnostics()
                        .AddCheck("required-local", () =>
                        {
                            Environment.CurrentManagedThreadId.Should().NotBe(uiThread);
                            SynchronizationContext.Current.Should().BeNull();
                            activated.Should().Be(0);
                            checkedReadiness++;

                            return ready ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy();
                        }, tags: ["startup"])
                        .AddCheck("offline-diagnostic", () =>
                        {
                            Interlocked.Increment(ref optionalChecks);

                            return HealthCheckResult.Degraded("Optional offline dependency.");
                        }, tags: ["diagnostic"]);
                    services.AddSingleton<IHostedService>(provider => new HealthReadinessService(
                        provider.GetRequiredService<WinFormsHealthDiagnostics>(), expected));
                }))
                .Build();
            application.Lifetime.ApplicationStarted += (_, _) => _ = application.StopAsync();

            if (ready)
            {
                application.Run(TestContext.Current.CancellationToken);
            }
            else
            {
                Assert.Throws<InvalidOperationException>(() => application.Run(TestContext.Current.CancellationToken))
                    .Should().BeSameAs(expected);
            }

            activated.Should().Be(ready ? 1 : 0);
            checkedReadiness.Should().Be(1);
            optionalChecks.Should().Be(0);
        });
    }

    [Fact]
    public void Run_HostedLifecycleAndUnusedHealthRegistration_PreserveUiOrdering()
    {
        RunOnSta(() =>
        {
            int uiThread = Environment.CurrentManagedThreadId;
            int unusedChecks = 0;
            List<string> events = [];
            Form form = new();
            WinFormsApplication? application = null;
            IHostBuilder hostBuilder = new HostBuilder().ConfigureServices((_, services) =>
            {
                services.AddWinFormsHealthDiagnostics().AddCheck("unused", () =>
                {
                    Interlocked.Increment(ref unusedChecks);

                    return HealthCheckResult.Unhealthy("Registration must not execute diagnostics.");
                });
                services.AddSingleton<IHostedService>(new RecordingLifecycleService(phase =>
                {
                    Environment.CurrentManagedThreadId.Should().NotBe(uiThread);
                    SynchronizationContext.Current.Should().BeNull();
                    if (phase is "stopping" or "stop" or "stopped")
                    {
                        form.IsDisposed.Should().BeTrue();
                        application!.UiCompletion.IsCompleted.Should().BeTrue();
                    }

                    events.Add(phase);
                }));
            });
            using (application = WinFormsApplication.CreateBuilder()
                .UseApplicationContext(() =>
                {
                    events.Add("activate");

                    return new ApplicationContext(form);
                })
                .UseHost(hostBuilder)
                .Build())
            {
                form.Disposed += (_, _) => events.Add("dispose-ui");
                application.Lifetime.ApplicationStarted += (_, _) => _ = application.StopAsync();
                application.Run(TestContext.Current.CancellationToken);
            }

            events.Should().Equal("starting", "start", "started", "activate", "dispose-ui", "stopping", "stop", "stopped");
            unusedChecks.Should().Be(0);
        });
    }

    [Theory]
    [InlineData("10")]
    [InlineData("0")]
    [InlineData("not-an-integer")]
    public void Run_ConfiguredOptions_ValidatesBeforeDeferredUiAndCleansUpFailures(string value)
    {
        RunOnSta(() =>
        {
            RecordingService service = new();
            int activated = 0;
            int validated = 0;
            IHostBuilder hostBuilder = new HostBuilder()
                .ConfigureWinFormsConfiguration(["--hostBuilder:reloadConfigOnChange=false"])
                .UseContentRoot(AppContext.BaseDirectory)
                .UseEnvironment(Environments.Production)
                .ConfigureAppConfiguration(configuration =>
                    configuration.AddInMemoryCollection([new("WinForms14957:Value", value)]))
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton<IHostedService>(_ => service);
                    services.AddOptions<WinFormsConfigurationTests.ConfigurationOptions>()
                        .Bind(context.Configuration.GetSection("WinForms14957"))
                        .Validate(options =>
                        {
                            activated.Should().Be(0);
                            SynchronizationContext.Current.Should().BeNull();
                            validated++;
                            return options.Value > 0;
                        }, "'WinForms14957:Value' must be greater than zero.")
                        .ValidateOnStart();
                });
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseHost(hostBuilder)
                .UseApplicationContext(() =>
                {
                    validated.Should().Be(1);
                    activated++;
                    return new ApplicationContext();
                })
                .Build();
            application.Lifetime.ApplicationStarted += (_, _) => _ = application.StopAsync();

            if (value == "10")
            {
                application.Run(TestContext.Current.CancellationToken);
                activated.Should().Be(1);
                service.StartCount.Should().Be(1);
            }
            else
            {
                if (value == "0")
                {
                    OptionsValidationException failure = Assert.Throws<OptionsValidationException>(
                        () => application.Run(TestContext.Current.CancellationToken));
                    failure.OptionsType.Should().Be(typeof(WinFormsConfigurationTests.ConfigurationOptions));
                    failure.Failures.Should().Contain("'WinForms14957:Value' must be greater than zero.");
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => application.Run(TestContext.Current.CancellationToken))
                        .Message.Should().Contain("WinForms14957:Value");
                }

                activated.Should().Be(0);
                service.StartCount.Should().Be(0);
            }

            service.DisposeCount.Should().Be(1);
            application.UiCompletion.IsCompleted.Should().BeTrue();
        });
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, false, false)]
    [InlineData(2, false, false)]
    [InlineData(1, true, false)]
    [InlineData(0, false, true)]
    [InlineData(1, false, true)]
    [InlineData(2, false, true)]
    [InlineData(1, true, true)]
    public void Run_RealHost_CoordinatesStopOnceAndPublishesStoppedAfterUiCleanup(
        int source,
        bool concurrentServices,
        bool componentManager)
    {
        using AppContextSwitchScope scope = new("Switch.System.Windows.Forms.EnableMsoComponentManager", componentManager);
        RunOnSta(() =>
        {
            RecordingService service = new();
            IHostBuilder hostBuilder = new HostBuilder()
                .ConfigureServices((_, services) =>
                {
                    services.AddSingleton<IHostedService>(_ => service);
                    services.Configure<HostOptions>(options =>
                    {
                        options.ServicesStopConcurrently = concurrentServices;
                        options.ShutdownTimeout = TimeSpan.Zero;
                    });
                });
            Form form = new();
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseStartupForm(form)
                .UseHost(hostBuilder)
                .Build();
            IHost host = application.Host!;
            var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
            host.Services.GetRequiredService<IHost>().Should().BeSameAs(host);
            service.StartCount.Should().Be(0);
            Task? stop = null;
            bool hostStoppedAfterUi = false;
            bool winFormsStoppedAfterHost = false;
            bool userCloseCanceled = false;
            using CancellationTokenRegistration registration = lifetime.ApplicationStopped.Register(() =>
                hostStoppedAfterUi = form.IsDisposed && application.UiCompletion.IsCompleted);
            application.Lifetime.ApplicationStopped += (_, _) =>
                winFormsStoppedAfterHost = lifetime.ApplicationStopped.IsCancellationRequested;
            form.FormClosing += (_, e) =>
            {
                userCloseCanceled = true;
                e.Cancel = true;
            };
            form.Shown += (_, _) => form.BeginInvoke(() =>
            {
                if (source == 0)
                {
                    stop = application.StopAsync();
                }
                else if (source == 1)
                {
                    stop = Task.Run(() => Task.WhenAll(Enumerable.Range(0, 8)
                        .Select(index => index % 2 == 0
                            ? host.StopAsync(CancellationToken.None)
                            : application.StopAsync())));
                }
                else
                {
                    lifetime.StopApplication();
                }
            });

            application.Run();
            stop?.GetAwaiter().GetResult();

            service.StartCount.Should().Be(1);
            service.StopCount.Should().Be(1);
            service.DisposeCount.Should().Be(1);
            hostStoppedAfterUi.Should().BeTrue();
            winFormsStoppedAfterHost.Should().BeTrue();
            userCloseCanceled.Should().BeTrue();
            form.IsDisposed.Should().BeTrue();
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Run_FormClose_StopsRealHostOnlyAfterCloseIsAccepted(bool cancelFirstClose, bool componentManager)
    {
        using AppContextSwitchScope scope = new("Switch.System.Windows.Forms.EnableMsoComponentManager", componentManager);
        RunOnSta(() =>
        {
            RecordingService service = new();
            Form form = new();
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseStartupForm(form)
                .UseHost(new HostBuilder().ConfigureServices((_, services) => services.AddSingleton<IHostedService>(_ => service)))
                .Build();
            var lifetime = application.Host!.Services.GetRequiredService<IHostApplicationLifetime>();
            bool cancelClose = cancelFirstClose;
            bool stoppedAfterVeto = false;
            bool hostStoppedAfterUi = false;
            int stopping = 0;
            int closing = 0;
            application.Lifetime.ApplicationStopping += (_, _) => stopping++;
            using CancellationTokenRegistration registration = lifetime.ApplicationStopped.Register(() =>
                hostStoppedAfterUi = form.IsDisposed && application.UiCompletion.IsCompleted);
            form.FormClosing += (_, e) =>
            {
                closing++;
                e.Cancel = cancelClose;
            };
            form.Shown += (_, _) => form.BeginInvoke(() =>
            {
                form.Close();
                if (cancelFirstClose)
                {
                    stoppedAfterVeto = lifetime.ApplicationStopping.IsCancellationRequested || stopping != 0;
                    cancelClose = false;
                    form.BeginInvoke(form.Close);
                }
            });

            application.Run();

            stoppedAfterVeto.Should().BeFalse();
            closing.Should().Be(cancelFirstClose ? 2 : 1);
            stopping.Should().Be(1);
            hostStoppedAfterUi.Should().BeTrue();
            service.StopCount.Should().Be(1);
            service.DisposeCount.Should().Be(1);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_BackgroundServiceFailure_RequestsStopAndWaitsForUiCleanup(bool componentManager)
    {
        using AppContextSwitchScope scope = new("Switch.System.Windows.Forms.EnableMsoComponentManager", componentManager);
        RunOnSta(() =>
        {
            InvalidOperationException expected = new("Background service failed.");
            TaskCompletionSource releaseFailure = new(TaskCreationOptions.RunContinuationsAsynchronously);
            FailingBackgroundService service = new(releaseFailure.Task, expected);
            Form form = new();
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseStartupForm(form)
                .UseHost(new HostBuilder().ConfigureServices((_, services) =>
                {
                    services.AddSingleton<IHostedService>(_ => service);
                    services.Configure<HostOptions>(options =>
                        options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost);
                }))
                .Build();
            var lifetime = application.Host!.Services.GetRequiredService<IHostApplicationLifetime>();
            bool hostStoppedAfterUi = false;
            int stopping = 0;
            using CancellationTokenRegistration registration = lifetime.ApplicationStopped.Register(() =>
                hostStoppedAfterUi = form.IsDisposed && application.UiCompletion.IsCompleted);
            application.Lifetime.ApplicationStopping += (_, _) => stopping++;
            form.Shown += (_, _) => form.BeginInvoke(() => releaseFailure.SetResult());

            application.Run();

            service.ExecuteTask!.Exception!.InnerException.Should().BeSameAs(expected);
            stopping.Should().Be(1);
            hostStoppedAfterUi.Should().BeTrue();
            service.StopCount.Should().Be(1);
            service.DisposeCount.Should().Be(1);
        });
    }

    [Fact]
    public void HostStopAsync_CanceledWait_DoesNotReleaseUiBarrier()
    {
        RunOnSta(() =>
        {
            RecordingService service = new();
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseApplicationContext(() => new ApplicationContext())
                .UseHost(new HostBuilder().ConfigureServices((_, services) => services.AddSingleton<IHostedService>(_ => service)))
                .Build();
            var lifetime = application.Host!.Services.GetRequiredService<IHostApplicationLifetime>();
            bool stoppedWhileUiActive = false;
            Task? canceledWait = null;
            application.Lifetime.ApplicationStarted += (_, _) =>
            {
                canceledWait = application.Host.StopAsync(new CancellationToken(canceled: true));
                stoppedWhileUiActive = lifetime.ApplicationStopped.IsCancellationRequested;
            };

            application.Run();

            Assert.ThrowsAny<OperationCanceledException>(() => canceledWait!.GetAwaiter().GetResult());
            stoppedWhileUiActive.Should().BeFalse();
            lifetime.ApplicationStopped.IsCancellationRequested.Should().BeTrue();
            service.StopCount.Should().Be(1);
        });
    }

    [Fact]
    public void HostedStopBarrier_CanceledToken_WaitsForUiNotTotalCompletion()
    {
        RunOnSta(() =>
        {
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseApplicationContext(() => new ApplicationContext())
                .UseHost(new HostBuilder())
                .Build();
            WinFormsHostStopBarrier barrier = application.Host!.Services
                .GetServices<IHostedService>()
                .OfType<WinFormsHostStopBarrier>()
                .Single();

            Task stop = barrier.StopAsync(new CancellationToken(canceled: true));
            stop.IsCompleted.Should().BeFalse();
            application.Run();
            stop.GetAwaiter().GetResult();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HostStopAsync_BeforeRun_CompletesAfterRunOrDisposeWithoutActivatingUi(bool dispose)
    {
        RunOnSta(() =>
        {
            int activated = 0;
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseApplicationContext(() =>
                {
                    activated++;
                    return new ApplicationContext();
                })
                .UseHost(new HostBuilder())
                .Build();
            var lifetime = application.Host!.Services.GetRequiredService<IHostApplicationLifetime>();

            Task stop = application.Host.StopAsync(CancellationToken.None);
            stop.IsCompleted.Should().BeFalse();
            if (dispose)
            {
                application.Dispose();
            }
            else
            {
                application.Run();
            }

            stop.GetAwaiter().GetResult();
            activated.Should().Be(0);
            lifetime.ApplicationStopped.IsCancellationRequested.Should().BeTrue();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_RealHostFailure_PreservesFailureAndDisposesOwnedResources(bool startupFailure)
    {
        RunOnSta(() =>
        {
            InvalidOperationException expected = new("Host service failed.");
            RecordingService service = new()
            {
                StartFailure = startupFailure ? expected : null,
                StopFailure = startupFailure ? null : expected
            };
            int activated = 0;
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseApplicationContext(() =>
                {
                    activated++;
                    return new ApplicationContext();
                })
                .UseHost(new HostBuilder().ConfigureServices((_, services) => services.AddSingleton<IHostedService>(_ => service)))
                .Build();
            application.Lifetime.ApplicationStarted += (_, _) => _ = application.StopAsync();
            var lifetime = application.Host!.Services.GetRequiredService<IHostApplicationLifetime>();
            bool stoppedAfterUi = false;
            using CancellationTokenRegistration registration = lifetime.ApplicationStopped.Register(() =>
                stoppedAfterUi = application.UiCompletion.IsCompleted);

            Assert.Throws<InvalidOperationException>(() => application.Run(TestContext.Current.CancellationToken))
                .Should().BeSameAs(expected);

            activated.Should().Be(startupFailure ? 0 : 1);
            stoppedAfterUi.Should().BeTrue();
            service.DisposeCount.Should().Be(1);
        });
    }

    [Fact]
    public void Host_StartBeforeRunIsRejectedAndUnrunDisposalDoesNotRaiseWinFormsEvents()
    {
        RunOnSta(() =>
        {
            using Form form = new();
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseStartupForm(form)
                .UseHost(new HostBuilder())
                .Build();
            int stopped = 0;
            application.Lifetime.ApplicationStopped += (_, _) => stopped++;

            Assert.Throws<InvalidOperationException>(() => application.Host!.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            application.Host!.Dispose();

            form.IsDisposed.Should().BeTrue();
            stopped.Should().Be(0);
            application.Host.Dispose();
        });
    }

    [Fact]
    public void Run_HostStopDuringStartup_CancelsStartupAndDoesNotActivateUi()
    {
        RunOnSta(() =>
        {
            Task? hostStop = null;
            int activated = 0;
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseApplicationContext(() =>
                {
                    activated++;
                    return new ApplicationContext();
                })
                .UseHost(new HostBuilder().ConfigureServices((_, services) =>
                    services.AddSingleton<IHostedService>(provider => new StartupStoppingService(
                        provider.GetRequiredService<IHost>(), stop => hostStop = stop))))
                .Build();
            int stopped = 0;
            application.Lifetime.ApplicationStopped += (_, _) => stopped++;

            application.Run();
            hostStop!.GetAwaiter().GetResult();

            activated.Should().Be(0);
            stopped.Should().Be(1);
        });
    }

    [Fact]
    public void UseHost_ValidatesSelectionAndDoesNotTransferFormOnHostBuildFailure()
    {
        RunOnSta(() =>
        {
            using Form form = new();
            WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder().UseStartupForm(form);
            Assert.Throws<ArgumentNullException>(() => builder.UseHost(null!));
            InvalidOperationException expected = new("Build failed.");
            builder.UseHost(new HostBuilder().ConfigureServices((_, _) => throw expected));
            Assert.Throws<InvalidOperationException>(() => builder.UseHost(new HostBuilder()));

            Assert.Throws<InvalidOperationException>(() => builder.Build()).Should().BeSameAs(expected);

            form.IsDisposed.Should().BeFalse();
            Assert.Throws<InvalidOperationException>(() => builder.Build());
        });
    }

    /// <summary>
    ///  Applies an explicit application-owned local readiness policy before startup UI activation.
    /// </summary>
    private sealed class HealthReadinessService(
        WinFormsHealthDiagnostics diagnostics, Exception failure) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            HealthReport report = await diagnostics.CheckHealthAsync(
                registration => registration.Tags.Contains("startup"), cancellationToken);
            if (report.Entries.Count == 0 || report.Status != HealthStatus.Healthy)
            {
                throw failure;
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    /// <summary>
    ///  Records the real host's lifecycle callbacks without installing another lifetime mechanism.
    /// </summary>
    private sealed class RecordingLifecycleService(Action<string> record) : IHostedLifecycleService
    {
        public Task StartingAsync(CancellationToken cancellationToken)
            => Record("starting");

        public Task StartAsync(CancellationToken cancellationToken)
            => Record("start");

        public Task StartedAsync(CancellationToken cancellationToken)
            => Record("started");

        public Task StoppingAsync(CancellationToken cancellationToken)
            => Record("stopping");

        public Task StopAsync(CancellationToken cancellationToken)
            => Record("stop");

        public Task StoppedAsync(CancellationToken cancellationToken)
            => Record("stopped");

        private Task Record(string phase)
        {
            record(phase);

            return Task.CompletedTask;
        }
    }

    private static void RunOnSta(Action action)
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
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(60)).Should().BeTrue("the caller-owned STA must finish shutdown");
        failure?.Throw();
    }

    /// <summary>
    ///  Fails only after dispatched UI work releases the background operation.
    /// </summary>
    private sealed class FailingBackgroundService(Task releaseFailure, Exception failure) : BackgroundService
    {
        internal int StopCount { get; private set; }
        internal int DisposeCount { get; private set; }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await releaseFailure.WaitAsync(stoppingToken);
            throw failure;
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            return base.StopAsync(cancellationToken);
        }

        public override void Dispose()
        {
            DisposeCount++;
            base.Dispose();
        }
    }

    /// <summary>
    ///  Requests host shutdown during startup and cooperatively waits for startup cancellation.
    /// </summary>
    private sealed class StartupStoppingService(IHost host, Action<Task> observeStop) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            observeStop(host.StopAsync(CancellationToken.None));
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    /// <summary>
    ///  Records actual hosted-service callbacks and optionally fails startup or shutdown.
    /// </summary>
    private sealed class RecordingService : IHostedService, IDisposable
    {
        internal int StartCount { get; private set; }
        internal int StopCount { get; private set; }
        internal int DisposeCount { get; private set; }
        internal Exception? StartFailure { get; init; }
        internal Exception? StopFailure { get; init; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            StartCount++;
            if (StartFailure is not null)
            {
                throw StartFailure;
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            if (StopFailure is not null)
            {
                throw StopFailure;
            }

            return Task.CompletedTask;
        }

        public void Dispose() => DisposeCount++;
    }
}
