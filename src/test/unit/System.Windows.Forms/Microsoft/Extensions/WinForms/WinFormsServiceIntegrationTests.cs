// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.WinForms;

namespace System.Windows.Forms.Tests;

/// <summary>
///  Exercises service composition and owned asynchronous work through complete real-host application runs.
/// </summary>
[Collection("Sequential")]
[Trait("Category", "ServiceIntegration")]
public class WinFormsServiceIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_CustomContainerAndConfigurationExtensions_PreserveValidationAndOwnership(bool valid)
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("WinFormsServiceIntegration-");
        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "appsettings.json"),
                """{"WinForms14961":{"Value":10}}""");
            RunOnSta(() =>
            {
                ContainerFactory factory = new();
                int activated = 0;
                WinFormsApplication? application = null;
                IHostBuilder hostBuilder = new HostBuilder()
                    .UseServiceProviderFactory(factory)
                    .ConfigureWinFormsConfiguration(
                        ["--hostBuilder:reloadConfigOnChange=false", "--WinForms14961:Value=20"])
                    .UseContentRoot(directory.FullName)
                    .UseEnvironment(Environments.Production)
                    .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                        [new("WinForms14961:Value", valid ? "30" : "0")]))
                    .ConfigureServices((context, services) => services
                        .AddOptions<WinFormsConfigurationTests.ConfigurationOptions>()
                        .Bind(context.Configuration.GetSection("WinForms14961"))
                        .Validate(options => options.Value > 0, "The extension value must be positive.")
                        .ValidateOnStart())
                    .ConfigureContainer<ContainerBuilder>((_, builder) =>
                        builder.Services.AddSingleton<OwnedDependency>());
                using (application = WinFormsApplication.CreateBuilder()
                    .UseHost(hostBuilder)
                    .UseApplicationContext(() =>
                    {
                        application!.Host!.Services.GetRequiredService<IHost>().Should().BeSameAs(application.Host);
                        application.Host.Services.GetRequiredService<IOptions<WinFormsConfigurationTests.ConfigurationOptions>>()
                            .Value.Value.Should().Be(30);
                        activated++;

                        return new ApplicationContext();
                    }).Build())
                {
                    var dependency = application.Host!.Services.GetRequiredService<OwnedDependency>();
                    application.Lifetime.ApplicationStarted += (_, _) => _ = application.StopAsync();
                    if (valid)
                    {
                        application.Run(TestContext.Current.CancellationToken);
                    }
                    else
                    {
                        Assert.Throws<OptionsValidationException>(() => application.Run(TestContext.Current.CancellationToken))
                            .Failures.Should().Contain("The extension value must be positive.");
                    }

                    dependency.DisposeCount.Should().Be(1);
                    activated.Should().Be(valid ? 1 : 0);
                }

                factory.BuilderCount.Should().Be(1);
                factory.ProviderCount.Should().Be(1);
            });
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_ExplicitAsyncLoggingFlush_DrainsBeforeProviderDisposalAndPreservesFailure(bool failFlush)
    {
        RunOnSta(() =>
        {
            InvalidOperationException uiFailure = new("Handled UI failure.");
            InvalidOperationException flushFailure = new("Application-owned flush failed.");
            BufferedProvider provider = new(failFlush ? flushFailure : null);
            TaskCompletionSource returned = new(TaskCreationOptions.RunContinuationsAsynchronously);
            ApplicationContext? context = null;
            int handled = 0;
            List<WinFormsLoggingFailure> loggingFailures = [];
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseApplicationContext(() => context = new ApplicationContext())
                .UseHost(new HostBuilder().ConfigureServices((_, services) =>
                {
                    services.AddSingleton<ILoggerProvider>(_ => provider);
                    services.AddSingleton<IHostedService>(_ => new CallbackHostedService(
                        stop: provider.FlushAsync));
                }))
                .Build();
            ILogger logger = application.Host!.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Integration");
            using WinFormsExceptionLogging logging = WinFormsExceptionLogging.Attach(application, logger,
                (_, arguments) =>
                {
                    arguments.Exception.Should().BeSameAs(uiFailure);
                    handled++;
                    context!.ExitThread();
                }, loggingFailures.Add);
            application.Lifetime.ApplicationStarted += (_, _) =>
                SynchronizationContext.Current!.Post(_ => Application.OnThreadException(uiFailure), null);
            Task observer = Task.Run(async () =>
            {
                try
                {
                    await provider.FlushEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
                    await application.UiCompletion.WaitAsync(TestContext.Current.CancellationToken);
                    provider.DisposeCount.Should().Be(0);
                    returned.Task.IsCompleted.Should().BeFalse();
                }
                finally
                {
                    provider.ReleaseFlush.TrySetResult();
                }
            });

            try
            {
                if (failFlush)
                {
                    Assert.Throws<InvalidOperationException>(() => application.Run(TestContext.Current.CancellationToken))
                        .Should().BeSameAs(flushFailure);
                }
                else
                {
                    application.Run(TestContext.Current.CancellationToken);
                }
            }
            finally
            {
                returned.TrySetResult();
            }

            observer.GetAwaiter().GetResult();
            logging.Dispose();
            logging.DiagnosticCompletion.GetAwaiter().GetResult();
            handled.Should().Be(1);
            loggingFailures.Should().BeEmpty();
            provider.FlushCount.Should().Be(1);
            provider.DisposeCount.Should().Be(1);
            provider.Persisted.Should().Equal(
                WinFormsDiagnosticSource.ApplicationStarted, WinFormsDiagnosticSource.ThreadException,
                WinFormsDiagnosticSource.ApplicationStopping, WinFormsDiagnosticSource.ApplicationStopped);
        });
    }

    [Fact]
    public void Run_HostedSettings_UpgradeConcurrentWritesAndExplicitRecoveryPersistAcrossApplications()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("WinFormsHostedSettings-");
        string primary = Path.Combine(directory.FullName, "settings.json");
        string backup = primary + ".bak";
        try
        {
            File.WriteAllText(primary, """{"schemaVersion":1,"revision":3,"settings":{"Value":5}}""");
            RunSettingsHost(directory.FullName, async (settings, token) =>
            {
                await Assert.ThrowsAsync<InvalidDataException>(() => settings.LoadAsync(token));
                UserSettingsCommit upgrade = await settings.UpgradeAsync(token);
                await upgrade.NotificationCompletion;
                upgrade.SchemaVersion.Should().Be(2);
                (await settings.LoadAsync(token)).Value.Should().Be(12);
                UserSettingsCommit[] writes = await Task.WhenAll(
                    settings.SaveAsync(new() { Value = 20 }, token),
                    settings.SaveAsync(new() { Value = 30 }, token));
                await Task.WhenAll(writes.Select(commit => commit.NotificationCompletion));
                UserSettingsCommit latest = writes.MaxBy(commit => commit.Revision)!;
                writes.Select(commit => commit.Revision).Distinct().Should().HaveCount(2);
                (await settings.LoadAsync(token)).Value.Should().Be(latest.Settings.GetProperty("Value").GetInt32());
                Directory.GetFiles(directory.FullName, "*.tmp").Should().BeEmpty();
            });

            using JsonDocument previous = JsonDocument.Parse(File.ReadAllText(backup));
            int expectedRecovery = previous.RootElement.GetProperty("settings").GetProperty("Value").GetInt32();
            File.WriteAllText(primary, "{ broken");
            RunSettingsHost(directory.FullName, async (settings, token) =>
            {
                (await Assert.ThrowsAsync<InvalidDataException>(() => settings.LoadAsync(token)))
                    .InnerException.Should().BeAssignableTo<JsonException>();
                File.ReadAllText(primary).Should().Be("{ broken");
                UserSettingsCommit recovered = await settings.RecoverAsync(token);
                await recovered.NotificationCompletion;
                (await settings.LoadAsync(token)).Value.Should().Be(expectedRecovery);
                recovered.SchemaVersion.Should().Be(2);
                Directory.GetFiles(directory.FullName, "*.tmp").Should().BeEmpty();
            });
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Run_BackgroundHealthCancellation_JoinsAsyncCheckScopeBeforeOwnedProviderDisposal()
    {
        RunOnSta(() =>
        {
            PendingCheckResource resource = new();
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseApplicationContext(() => new ApplicationContext())
                .UseHost(new HostBuilder().ConfigureServices((_, services) =>
                {
                    services.Configure<HostOptions>(options => options.ShutdownTimeout = Timeout.InfiniteTimeSpan);
                    services.AddScoped(_ => resource);
                    services.AddWinFormsHealthDiagnostics().AddCheck<ResourceHealthCheck>("pending");
                    services.AddHostedService<HealthWorker>();
                    services.AddSingleton(_ => new OwnedDependency(() => resource.Disposed.Should().BeTrue()));
                }))
                .Build();
            var dependency = application.Host!.Services.GetRequiredService<OwnedDependency>();
            Task? shutdown = null;
            application.Lifetime.ApplicationStarted += (_, _) => shutdown = Task.Run(async () =>
            {
                await resource.CheckEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
                Task stop = application.Host.StopAsync(CancellationToken.None);
                try
                {
                    await resource.DisposeEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
                    await application.UiCompletion.WaitAsync(TestContext.Current.CancellationToken);
                    stop.IsCompleted.Should().BeFalse();
                    resource.Disposed.Should().BeFalse();
                    dependency.DisposeCount.Should().Be(0);
                }
                finally
                {
                    resource.ReleaseDisposal.TrySetResult();
                }

                await stop;
            });

            application.Run(TestContext.Current.CancellationToken);
            shutdown!.GetAwaiter().GetResult();
            resource.Disposed.Should().BeTrue();
            dependency.DisposeCount.Should().Be(1);
        });
    }

    private static void RunSettingsHost(
        string root, Func<IUserSettingsService<SettingsModel>, CancellationToken, Task> startup)
    {
        RunOnSta(() =>
        {
            bool startupCompleted = false;
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseApplicationContext(() =>
                {
                    startupCompleted.Should().BeTrue();

                    return new ApplicationContext();
                })
                .UseHost(new HostBuilder().ConfigureServices((_, services) =>
                {
                    services.AddJsonUserSettings("integration-product", 2, SettingsJsonContext.Default.SettingsModel,
                        () => new SettingsModel { Value = 7 }, options =>
                        {
                            options.StorageDirectory = root;
                            options.Migrations.Add(1, _ =>
                            {
                                using JsonDocument document = JsonDocument.Parse("""{"Value":12}""");

                                return document.RootElement.Clone();
                            });
                        });
                    services.AddSingleton<IHostedService>(provider => new CallbackHostedService(async token =>
                    {
                        await startup(provider.GetRequiredService<IUserSettingsService<SettingsModel>>(), token);
                        startupCompleted = true;
                    }));
                }))
                .Build();
            application.Lifetime.ApplicationStarted += (_, _) => _ = application.StopAsync();
            application.Run(TestContext.Current.CancellationToken);
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
        thread.Join(TimeSpan.FromSeconds(60)).Should().BeTrue("the coordinated application must finish");
        failure?.Throw();
    }

    /// <summary>
    ///  Supplies a custom builder through the standard container-factory extension hooks.
    /// </summary>
    private sealed class ContainerBuilder(IServiceCollection services)
    {
        internal IServiceCollection Services { get; } = services;
    }

    /// <summary>
    ///  Verifies factory consumption without adding an unrelated third-party container dependency.
    /// </summary>
    private sealed class ContainerFactory : IServiceProviderFactory<ContainerBuilder>
    {
        internal int BuilderCount { get; private set; }
        internal int ProviderCount { get; private set; }

        public ContainerBuilder CreateBuilder(IServiceCollection services)
        {
            BuilderCount++;

            return new ContainerBuilder(services);
        }

        public IServiceProvider CreateServiceProvider(ContainerBuilder builder)
        {
            ProviderCount++;

            return builder.Services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
        }
    }

    /// <summary>
    ///  Records final provider-owned disposal and optional dependent-resource assertions.
    /// </summary>
    private sealed class OwnedDependency(Action? onDispose = null) : IDisposable
    {
        private int _disposeCount;
        internal int DisposeCount => Volatile.Read(ref _disposeCount);

        public void Dispose()
        {
            onDispose?.Invoke();
            Interlocked.Increment(ref _disposeCount);
        }
    }

    /// <summary>
    ///  Uses standard hosted-service callbacks for explicit application startup and flush participation.
    /// </summary>
    private sealed class CallbackHostedService(
        Func<CancellationToken, Task>? start = null,
        Func<CancellationToken, Task>? stop = null) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
            => start?.Invoke(cancellationToken) ?? Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken)
            => stop?.Invoke(cancellationToken) ?? Task.CompletedTask;
    }

    /// <summary>
    ///  Models an application's explicit asynchronous flush and synchronous final-disposal flush.
    /// </summary>
    private sealed class BufferedProvider(Exception? flushFailure) : ILoggerProvider, ILogger
    {
        private readonly ConcurrentQueue<WinFormsDiagnosticSource> _pending = new();
        private int _disposeCount;
        internal ConcurrentQueue<WinFormsDiagnosticSource> Persisted { get; } = new();
        internal TaskCompletionSource FlushEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseFlush { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int FlushCount { get; private set; }
        internal int DisposeCount => Volatile.Read(ref _disposeCount);

        public ILogger CreateLogger(string categoryName) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ObjectDisposedException.ThrowIf(DisposeCount != 0, this);
            if (state is IEnumerable<KeyValuePair<string, object?>> values)
            {
                foreach ((string key, object? value) in values)
                {
                    if (key == "Source" && value is WinFormsDiagnosticSource source)
                    {
                        _pending.Enqueue(source);
                    }
                }
            }
        }

        internal async Task FlushAsync(CancellationToken token)
        {
            FlushCount++;
            FlushEntered.TrySetResult();
            await ReleaseFlush.Task.WaitAsync(token);
            if (flushFailure is not null)
            {
                throw flushFailure;
            }

            Drain();
        }

        public void Dispose()
        {
            Drain();
            Interlocked.Increment(ref _disposeCount);
        }

        private void Drain()
        {
            while (_pending.TryDequeue(out WinFormsDiagnosticSource source))
            {
                Persisted.Enqueue(source);
            }
        }
    }

    /// <summary>
    ///  Keeps a check's scope alive until cancellation and asynchronous disposal are explicitly joined.
    /// </summary>
    private sealed class PendingCheckResource : IAsyncDisposable
    {
        internal TaskCompletionSource CheckEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource DisposeEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseDisposal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Disposed { get; private set; }

        internal async Task<HealthCheckResult> CheckAsync(CancellationToken token)
        {
            CheckEntered.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);

            return HealthCheckResult.Healthy();
        }

        public async ValueTask DisposeAsync()
        {
            DisposeEntered.TrySetResult();
            await ReleaseDisposal.Task;
            Disposed = true;
        }
    }

    /// <summary>
    ///  Resolves an asynchronous resource through the standard per-check service scope.
    /// </summary>
    private sealed class ResourceHealthCheck(PendingCheckResource resource) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
            => resource.CheckAsync(cancellationToken);
    }

    /// <summary>
    ///  Owns its health task through the standard BackgroundService execution and shutdown contract.
    /// </summary>
    private sealed class HealthWorker(
        WinFormsHealthDiagnostics diagnostics, IHostApplicationLifetime lifetime) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await diagnostics.CheckHealthAsync(cancellationToken: stoppingToken);
            }
            catch (OperationCanceledException) when (
                stoppingToken.IsCancellationRequested || lifetime.ApplicationStopping.IsCancellationRequested)
            {
            }
        }
    }
}
