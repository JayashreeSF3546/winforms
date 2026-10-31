// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.WinForms;

namespace System.Windows.Forms.Tests;

/// <summary>
///  Verifies desktop execution boundaries while retaining the standard health service contracts.
/// </summary>
public class WinFormsHealthDiagnosticsTests
{
    [WinFormsTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CheckHealthAsync_DefersResolutionAndAwaitsScopedDisposal(bool cancelDuringDisposal)
    {
        int uiThread = Environment.CurrentManagedThreadId;
        TaskCompletionSource disposalStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseDisposal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int resolved = 0;
        using IHost host = BuildHost(services =>
        {
            services.AddScoped<HealthCheckService>(_ =>
            {
                Environment.CurrentManagedThreadId.Should().NotBe(uiThread);
                SynchronizationContext.Current.Should().BeNull();
                Interlocked.Increment(ref resolved);

                return new ScopedHealthService(
                    _ => Task.FromResult(new HealthReport(new Dictionary<string, HealthReportEntry>(), TimeSpan.Zero)),
                    async () =>
                    {
                        Environment.CurrentManagedThreadId.Should().NotBe(uiThread);
                        disposalStarted.SetResult();
                        await releaseDisposal.Task;
                    });
            });
            services.AddWinFormsHealthDiagnostics();
            services.AddWinFormsHealthDiagnostics();
        });
        var diagnostics = host.Services.GetRequiredService<WinFormsHealthDiagnostics>();
        resolved.Should().Be(0);
        host.Services.GetServices<WinFormsHealthDiagnostics>().Should().ContainSingle();

        Task<HealthReport> operation = diagnostics.CheckHealthAsync(cancellationToken: TestContext.Current.CancellationToken);
        await disposalStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        operation.IsCompleted.Should().BeFalse();
        if (cancelDuringDisposal)
        {
            host.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();
            operation.IsCompleted.Should().BeFalse();
        }

        releaseDisposal.SetResult();
        if (cancelDuringDisposal)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        }
        else
        {
            HealthReport report = await operation;
            report.Status.Should().Be(HealthStatus.Healthy);
        }

        resolved.Should().Be(1);
    }

    [WinFormsFact]
    public async Task CheckHealthAsync_StandardOptionsPredicatesAndCheckFactoriesRunOffUi()
    {
        int uiThread = Environment.CurrentManagedThreadId;
        int configured = 0;
        int executed = 0;
        using IHost host = BuildHost(services =>
        {
            services.Configure<HealthCheckServiceOptions>(_ =>
            {
                Environment.CurrentManagedThreadId.Should().NotBe(uiThread);
                configured++;
            });
            services.AddWinFormsHealthDiagnostics().Add(new HealthCheckRegistration(
                "local",
                _ =>
                {
                    Environment.CurrentManagedThreadId.Should().NotBe(uiThread);

                    return new DelegateHealthCheck(_ =>
                    {
                        Environment.CurrentManagedThreadId.Should().NotBe(uiThread);
                        SynchronizationContext.Current.Should().BeNull();
                        Interlocked.Increment(ref executed);

                        return Task.FromResult(HealthCheckResult.Healthy());
                    });
                },
                failureStatus: null,
                tags: ["diagnostic"]));
        });
        var diagnostics = host.Services.GetRequiredService<WinFormsHealthDiagnostics>();
        configured.Should().Be(0);

        HealthReport report = await diagnostics.CheckHealthAsync(registration =>
        {
            Environment.CurrentManagedThreadId.Should().NotBe(uiThread);

            return registration.Tags.Contains("diagnostic");
        }, TestContext.Current.CancellationToken);

        report.Status.Should().Be(HealthStatus.Healthy);
        configured.Should().Be(1);
        executed.Should().Be(1);
    }

    [Fact]
    public async Task CheckHealthAsync_PreservesSelectedOfflineAndFailureReportsWithoutStoppingHost()
    {
        InvalidOperationException expected = new("Dependency unavailable.");
        using IHost host = BuildHost(services =>
        {
            services.AddWinFormsHealthDiagnostics()
                .AddCheck("offline", new DelegateHealthCheck(_ =>
                    Task.FromResult(HealthCheckResult.Degraded("Offline mode."))), tags: ["diagnostic"])
                .AddCheck("failed", new DelegateHealthCheck(_ => throw expected), tags: ["diagnostic"])
                .AddCheck("not-selected", new DelegateHealthCheck(_ =>
                    throw new InvalidOperationException("An unselected check ran.")), tags: ["startup"]);
        });
        var diagnostics = host.Services.GetRequiredService<WinFormsHealthDiagnostics>();

        HealthReport report = await diagnostics.CheckHealthAsync(
            registration => registration.Tags.Contains("diagnostic"), TestContext.Current.CancellationToken);

        report.Entries.Keys.Should().BeEquivalentTo(["offline", "failed"]);
        report.Entries["offline"].Status.Should().Be(HealthStatus.Degraded);
        report.Entries["failed"].Status.Should().Be(HealthStatus.Unhealthy);
        report.Entries["failed"].Exception.Should().BeSameAs(expected);
        host.Services.GetRequiredService<IHostApplicationLifetime>()
            .ApplicationStopping.IsCancellationRequested.Should().BeFalse();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CheckHealthAsync_CallerOrHostCancellationWaitsForNonCooperativeWork(
        bool hostCancellation, bool cooperative)
    {
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenSource caller = new();
        using IHost host = BuildHost(services => services.AddWinFormsHealthDiagnostics()
            .AddCheck("pending", new DelegateHealthCheck(async token =>
            {
                entered.SetResult();
                await release.Task.WaitAsync(cooperative ? token : CancellationToken.None);

                return HealthCheckResult.Healthy();
            })));
        var diagnostics = host.Services.GetRequiredService<WinFormsHealthDiagnostics>();
        Task<HealthReport> operation = diagnostics.CheckHealthAsync(cancellationToken: caller.Token);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);

        if (hostCancellation)
        {
            host.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();
        }
        else
        {
            caller.Cancel();
        }

        if (!cooperative)
        {
            operation.IsCompleted.Should().BeFalse();
        }

        release.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        host.Services.GetRequiredService<IHostApplicationLifetime>()
            .ApplicationStopping.IsCancellationRequested.Should().Be(hostCancellation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CheckHealthAsync_PreCanceledRequestDoesNotActivateHealthServices(bool hostCancellation)
    {
        int activated = 0;
        using CancellationTokenSource caller = new();
        using IHost host = BuildHost(services =>
        {
            services.AddScoped<HealthCheckService>(_ =>
            {
                activated++;
                throw new InvalidOperationException("Health services must not be activated.");
            });
            services.AddWinFormsHealthDiagnostics();
        });
        var diagnostics = host.Services.GetRequiredService<WinFormsHealthDiagnostics>();
        if (hostCancellation)
        {
            host.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();
        }
        else
        {
            caller.Cancel();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            diagnostics.CheckHealthAsync(cancellationToken: caller.Token));
        activated.Should().Be(0);
    }

    [Fact]
    public async Task CheckHealthAsync_RegistrationTimeoutRemainsAReportNotCallerCancellation()
    {
        using IHost host = BuildHost(services => services.AddWinFormsHealthDiagnostics()
            .AddCheck("timeout", new DelegateHealthCheck(async token =>
            {
                await Task.Delay(Timeout.Infinite, token);

                return HealthCheckResult.Healthy();
            }), failureStatus: HealthStatus.Degraded, timeout: TimeSpan.FromMilliseconds(50)));
        var diagnostics = host.Services.GetRequiredService<WinFormsHealthDiagnostics>();

        HealthReport report = await diagnostics.CheckHealthAsync(cancellationToken: TestContext.Current.CancellationToken);

        report.Entries["timeout"].Status.Should().Be(HealthStatus.Degraded);
        report.Entries["timeout"].Exception.Should().BeAssignableTo<OperationCanceledException>();
        host.Services.GetRequiredService<IHostApplicationLifetime>()
            .ApplicationStopping.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task CheckHealthAsync_InfrastructureFailurePropagatesAfterScopeDisposal()
    {
        InvalidOperationException expected = new("Custom health infrastructure failed.");
        bool disposed = false;
        using IHost host = BuildHost(services =>
        {
            services.AddScoped<HealthCheckService>(_ => new ScopedHealthService(
                _ => Task.FromException<HealthReport>(expected),
                () =>
                {
                    disposed = true;

                    return ValueTask.CompletedTask;
                }));
            services.AddWinFormsHealthDiagnostics();
        });
        var diagnostics = host.Services.GetRequiredService<WinFormsHealthDiagnostics>();

        (await Assert.ThrowsAsync<InvalidOperationException>(() =>
            diagnostics.CheckHealthAsync(cancellationToken: TestContext.Current.CancellationToken)))
            .Should().BeSameAs(expected);
        disposed.Should().BeTrue();
    }

    private static IHost BuildHost(Action<IServiceCollection> configure)
        => new HostBuilder().ConfigureServices((_, services) => configure(services)).Build();

    /// <summary>
    ///  Supplies application check work through the standard health check contract.
    /// </summary>
    private sealed class DelegateHealthCheck(Func<CancellationToken, Task<HealthCheckResult>> check) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
            => check(cancellationToken);
    }

    /// <summary>
    ///  Exercises application replacements and asynchronous operation-scope disposal.
    /// </summary>
    private sealed class ScopedHealthService(
        Func<CancellationToken, Task<HealthReport>> check,
        Func<ValueTask> dispose) : HealthCheckService, IAsyncDisposable
    {
        public override Task<HealthReport> CheckHealthAsync(
            Func<HealthCheckRegistration, bool>? predicate, CancellationToken cancellationToken)
            => check(cancellationToken);

        public ValueTask DisposeAsync() => dispose();
    }
}
