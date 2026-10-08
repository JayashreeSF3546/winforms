// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.WinForms;

namespace System.Windows.Forms.Tests;

/// <summary>
///  Tests Generic Host background services and health checks in WinForms applications.
/// </summary>
public class WinFormsHostedServicesTests
{
    [Fact]
    public void Run_StartsAndStopsHostedServicesOffTheUiThread()
    {
        RunOnStaThread(() =>
        {
            int uiThreadId = Environment.CurrentManagedThreadId;
            HostedServiceProbe hostedService = new();
            WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder();
            builder.Services.AddSingleton<IHostedService>(hostedService);
            using Form form = new();
            form.Shown += (_, _) =>
            {
                hostedService.UiContext = SynchronizationContext.Current;
                form.Close();
            };
            using WinFormsApplication application = builder
                .UseStartupForm(form)
                .Build();

            application.Run();

            Assert.NotEqual(uiThreadId, hostedService.StartThreadId);
            Assert.NotEqual(uiThreadId, hostedService.StopThreadId);
            Assert.Equal(["Started", "Stopped"], hostedService.Events);
            Assert.True(hostedService.UiCallbackProcessed.Task.IsCompletedSuccessfully);
        });
    }

    [Fact]
    public void Run_HostedServiceStartupFailurePreventsShowingTheStartupForm()
    {
        RunOnStaThread(() =>
        {
            bool formWasShown = false;
            WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder();
            builder.Services.AddSingleton<IHostedService, StartupFailureService>();
            using Form form = new();
            form.Shown += (_, _) => formWasShown = true;
            using WinFormsApplication application = builder
                .UseStartupForm(form)
                .Build();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(application.Run);

            Assert.Equal("Hosted service startup failed.", exception.Message);
            Assert.False(formWasShown);
        });
    }

    [Fact]
    public void Run_CancelsAndAwaitsBackgroundServiceBeforeExiting()
    {
        RunOnStaThread(() =>
        {
            using BackgroundServiceProbe hostedService = new();
            WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder();
            builder.Services.AddSingleton<IHostedService>(hostedService);
            using Form form = new();
            form.Shown += (_, _) => form.Close();
            using WinFormsApplication application = builder
                .UseStartupForm(form)
                .Build();

            application.Run();

            Assert.True(hostedService.StoppingToken.IsCancellationRequested);
            Assert.True(hostedService.ExecutionStopped.Task.IsCompletedSuccessfully);
        });
    }

    [Fact]
    public void HealthChecks_AreRegisteredAndExecutedOnlyWhenRequested()
    {
        RunOnStaThread(() =>
        {
            HealthCheckRunner? runner = null;
            int healthCheckRunCount = 0;
            WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder();
            builder.Services
                .AddHealthChecks()
                .AddCheck("ready", () =>
                {
                    healthCheckRunCount++;

                    return HealthCheckResult.Healthy("Ready.");
                });
            builder.Services.AddSingleton<IHostedService>(services =>
                runner = new(services.GetRequiredService<HealthCheckService>()));
            using Form form = new();
            form.Shown += (_, _) => form.Close();
            using WinFormsApplication application = builder
                .UseStartupForm(form)
                .Build();

            application.Run();

            Assert.NotNull(runner);
            Assert.Equal(HealthStatus.Healthy, runner.Report?.Status);
            Assert.Equal("Ready.", runner.Report?.Entries["ready"].Description);
            Assert.Equal(1, healthCheckRunCount);
        });
    }

    [Fact]
    public void HealthChecks_UnhealthyReportDoesNotPreventApplicationStartup()
    {
        RunOnStaThread(() =>
        {
            HealthCheckRunner? runner = null;
            WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder();
            builder.Services
                .AddHealthChecks()
                .AddCheck("offline", () => HealthCheckResult.Unhealthy("Offline."));
            builder.Services.AddSingleton<IHostedService>(services =>
                runner = new(services.GetRequiredService<HealthCheckService>()));
            using Form form = new();
            form.Shown += (_, _) => form.Close();
            using WinFormsApplication application = builder
                .UseStartupForm(form)
                .Build();

            application.Run();

            Assert.NotNull(runner);
            Assert.Equal(HealthStatus.Unhealthy, runner.Report?.Status);
            Assert.Equal("Offline.", runner.Report?.Entries["offline"].Description);
        });
    }

    [Fact]
    public void HealthChecks_ExceptionIsCapturedAsUnhealthyReport()
    {
        RunOnStaThread(() =>
        {
            HealthCheckRunner? runner = null;
            WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder();
            builder.Services
                .AddHealthChecks()
                .AddCheck(
                    "throws",
                    () => throw new InvalidOperationException("Health check failed."));
            builder.Services.AddSingleton<IHostedService>(services =>
                runner = new(services.GetRequiredService<HealthCheckService>()));
            using Form form = new();
            form.Shown += (_, _) => form.Close();
            using WinFormsApplication application = builder
                .UseStartupForm(form)
                .Build();

            application.Run();

            Assert.NotNull(runner);
            Assert.Equal(HealthStatus.Unhealthy, runner.Report?.Status);
            Assert.IsType<InvalidOperationException>(runner.Report?.Entries["throws"].Exception);
        });
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    /// <summary>
    ///  Records the Generic Host callback threads and order.
    /// </summary>
    private sealed class HostedServiceProbe : IHostedService
    {
        internal TaskCompletionSource UiCallbackProcessed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal List<string> Events { get; } = [];

        internal int StartThreadId { get; private set; }

        internal int StopThreadId { get; private set; }

        internal SynchronizationContext? UiContext { get; set; }

        /// <inheritdoc/>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            StartThreadId = Environment.CurrentManagedThreadId;
            Events.Add("Started");

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopThreadId = Environment.CurrentManagedThreadId;
            Events.Add("Stopped");
            SynchronizationContext uiContext = UiContext
                ?? throw new InvalidOperationException("The WinForms synchronization context was not captured.");
            uiContext.Post(
                static state => ((TaskCompletionSource)state!).TrySetResult(),
                UiCallbackProcessed);

            return UiCallbackProcessed.Task.WaitAsync(cancellationToken);
        }
    }

    /// <summary>
    ///  Fails host startup to verify the UI loop is not entered.
    /// </summary>
    private sealed class StartupFailureService : IHostedService
    {
        /// <inheritdoc/>
        public Task StartAsync(CancellationToken cancellationToken)
            => Task.FromException(new InvalidOperationException("Hosted service startup failed."));

        /// <inheritdoc/>
        public Task StopAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    /// <summary>
    ///  Observes the cancellation token and completion of background execution.
    /// </summary>
    private sealed class BackgroundServiceProbe : BackgroundService
    {
        internal CancellationToken StoppingToken { get; private set; }

        internal TaskCompletionSource ExecutionStopped { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <inheritdoc/>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            StoppingToken = stoppingToken;

            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            finally
            {
                ExecutionStopped.TrySetResult();
            }
        }
    }

    /// <summary>
    ///  Executes health checks when the host starts.
    /// </summary>
    private sealed class HealthCheckRunner(HealthCheckService healthCheckService) : IHostedService
    {
        internal HealthReport? Report { get; private set; }

        /// <inheritdoc/>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            Report = await healthCheckService.CheckHealthAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public Task StopAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
