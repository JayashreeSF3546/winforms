// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.WinForms;

namespace System.Windows.Forms.Tests;

/// <summary>
///  Verifies shutdown ordering against the real Generic Host rather than a synthetic host lifetime.
/// </summary>
[Collection("Sequential")]
public class WinFormsApplicationHostingTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(1, true)]
    public void Run_RealHost_CoordinatesStopOnceAndPublishesStoppedAfterUiCleanup(int source, bool concurrentServices)
    {
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
                        .Select(_ => host.StopAsync(CancellationToken.None))));
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
        thread.Join();
        failure?.Throw();
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
