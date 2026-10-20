// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.WinForms;

namespace WinFormsApplicationBuilder.Benchmarks;

/// <summary>
///  Measures configuration, settings, logging, hosted-service, and health-check overhead.
/// </summary>
internal static class InfrastructureBenchmarks
{
    private const int DefaultIterations = 30;
    private const int WarmupIterations = 5;
    private const int LogEventsPerFlush = 100;
    private const int LogEventsPerBatch = 1_000;
    private static readonly Action<ILogger, int, Exception?> s_logEvent =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(1, "BenchmarkEvent"),
            "Infrastructure benchmark event {EventNumber}.");

    /// <summary>
    ///  Runs the service-infrastructure benchmarks in an isolated temporary directory.
    /// </summary>
    /// <param name="args">Benchmark arguments, including the optional iteration count.</param>
    internal static void Run(string[] args)
    {
        int iterations = GetIterationCount(args);
        string workingDirectory = Path.Combine(
            Path.GetTempPath(),
            "WinFormsApplicationBuilderBenchmarks",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);

        try
        {
            string configurationPath = Path.Combine(workingDirectory, "appsettings.json");
            File.WriteAllText(
                configurationPath,
                """
                {
                  "Benchmark": {
                    "ApplicationName": "Infrastructure benchmark",
                    "ServiceIntervalSeconds": 5
                  }
                }
                """);

            Console.WriteLine("Infrastructure measurements");
            Console.WriteLine($"Iterations per operation: {iterations}");
            Console.WriteLine($"Runtime: {RuntimeInformation.FrameworkDescription}");
            Console.WriteLine($"OS: {RuntimeInformation.OSDescription}");
            Console.WriteLine(
                $"Architecture: {RuntimeInformation.ProcessArchitecture}; processors: {Environment.ProcessorCount}");
            Console.WriteLine();

            MeasureConfigurationLoad(configurationPath, iterations);

            using ConfigurationRoot configuration = BuildConfiguration(workingDirectory);
            MeasureOptionsBinding(configuration, iterations);

            string settingsPath = Path.Combine(workingDirectory, "user-settings.json");
            JsonUserSettingsService settingsService = CreateSettingsService(settingsPath);
            SaveSettingsAsync(settingsService, new BenchmarkSettings { Sequence = 0 })
                .GetAwaiter()
                .GetResult();
            MeasureSettingsLoad(settingsService, iterations);
            MeasureSettingsSave(settingsService, iterations);
            MeasurePersistenceResourceStability(settingsService, iterations);

            MeasureFailureRecovery(workingDirectory, iterations);

            MeasureLogging(iterations);
            MeasureProviderFlush(workingDirectory, iterations);
            MeasureHostBuild(configurationPath, iterations);
            MeasureHostedServiceLifecycle(configurationPath, iterations);
            MeasureHealthCheckExecution(settingsService, iterations);
            Console.WriteLine();
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    private static ConfigurationRoot BuildConfiguration(string contentRoot)
        => (ConfigurationRoot)new ConfigurationBuilder()
            .SetBasePath(contentRoot)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();

    private static void MeasureConfigurationLoad(string configurationPath, int iterations)
        => MeasureSync(
            "Configuration JSON load",
            iterations,
            _ =>
            {
                using ConfigurationRoot configuration = BuildConfiguration(
                    Path.GetDirectoryName(configurationPath)
                        ?? throw new InvalidOperationException("The configuration file has no parent directory."));
                GC.KeepAlive(configuration["Benchmark:ApplicationName"]);
            });

    private static void MeasureOptionsBinding(ConfigurationRoot configuration, int iterations)
        => MeasureSync(
            "Options bind + first resolution",
            iterations,
            _ =>
            {
                ServiceCollection services = new();
                services
                    .AddOptions<BenchmarkOptions>()
                    .Bind(configuration.GetSection("Benchmark"));

                using ServiceProvider provider = services.BuildServiceProvider();
                BenchmarkOptions options = provider.GetRequiredService<IOptions<BenchmarkOptions>>().Value;
                if (options.ServiceIntervalSeconds != 5)
                {
                    throw new InvalidOperationException("Options binding returned an unexpected value.");
                }
            });

    private static void MeasureSettingsLoad(JsonUserSettingsService settingsService, int iterations)
        => MeasureAsync(
            "User settings load",
            iterations,
            async iteration =>
            {
                BenchmarkSettings settings = await settingsService
                    .LoadAsync(new BenchmarkSettings())
                    .ConfigureAwait(false);
                GC.KeepAlive(settings);
            });

    private static void MeasureSettingsSave(JsonUserSettingsService settingsService, int iterations)
        => MeasureAsync(
            "User settings atomic save",
            iterations,
            iteration => SaveSettingsAsync(
                settingsService,
                new BenchmarkSettings { Sequence = iteration }));

    private static void MeasurePersistenceResourceStability(
        JsonUserSettingsService settingsService,
        int iterations)
    {
        for (int iteration = 0; iteration < WarmupIterations; iteration++)
        {
            RunPersistenceCycleAsync(settingsService, iteration)
                .GetAwaiter()
                .GetResult();
        }

        ProcessSnapshot previous = CaptureProcessSnapshot();
        int cyclesPerCheckpoint = Math.Max(1, (int)Math.Ceiling(iterations / 4.0));
        int completedCycles = 0;

        Console.WriteLine("User settings save/load resource stability (post-GC process deltas):");

        while (completedCycles < iterations)
        {
            int checkpointCycles = Math.Min(cyclesPerCheckpoint, iterations - completedCycles);

            for (int iteration = 0; iteration < checkpointCycles; iteration++)
            {
                RunPersistenceCycleAsync(settingsService, completedCycles + iteration)
                    .GetAwaiter()
                    .GetResult();
            }

            completedCycles += checkpointCycles;
            ProcessSnapshot current = CaptureProcessSnapshot();
            PrintResourceDelta($"  after {completedCycles} cycles", previous, current);
            previous = current;
        }
    }

    private static void MeasureFailureRecovery(string workingDirectory, int iterations)
    {
        RecoveryFixture[] fixtures = new RecoveryFixture[WarmupIterations + iterations];
        for (int index = 0; index < fixtures.Length; index++)
        {
            string settingsPath = Path.Combine(workingDirectory, $"recovery-{index}", "user-settings.json");
            string settingsDirectory = Path.GetDirectoryName(settingsPath)
                ?? throw new InvalidOperationException("The recovery settings file has no parent directory.");
            Directory.CreateDirectory(settingsDirectory);
            JsonUserSettingsService settingsService = CreateSettingsService(settingsPath);
            SaveSettingsAsync(settingsService, new BenchmarkSettings { Sequence = 0 })
                .GetAwaiter()
                .GetResult();
            SaveSettingsAsync(settingsService, new BenchmarkSettings { Sequence = 1 })
                .GetAwaiter()
                .GetResult();
            fixtures[index] = new RecoveryFixture(settingsService, settingsPath);
        }

        int nextFixture = 0;
        MeasureAsync(
            "Settings fault injection + backup recovery",
            iterations,
            async _ =>
            {
                RecoveryFixture fixture = fixtures[nextFixture++];
                File.WriteAllText(fixture.SettingsPath, "{ invalid JSON");
                BenchmarkSettings recovered = await fixture.SettingsService
                    .LoadAsync(new BenchmarkSettings())
                    .ConfigureAwait(false);
                if (recovered.Sequence != 0)
                {
                    throw new InvalidOperationException("Settings recovery returned an unexpected value.");
                }
            });
    }

    private static void MeasureLogging(int iterations)
    {
        ILogger nullLogger = NullLogger.Instance;
        using ILoggerFactory loggerFactory = LoggerFactory.Create(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Information);
            logging.AddProvider(new DiscardingLoggerProvider());
        });
        ILogger providerLogger = loggerFactory.CreateLogger("InfrastructureBenchmark");

        MeasureSync(
            $"Logging / NullLogger ({LogEventsPerBatch} events per batch)",
            iterations,
            _ => LogBatch(nullLogger));
        MeasureSync(
            $"Logging / provider dispatch ({LogEventsPerBatch} events per batch)",
            iterations,
            _ => LogBatch(providerLogger));
    }

    private static void MeasureProviderFlush(string workingDirectory, int iterations)
    {
        List<double> elapsedMicroseconds = new(iterations);

        for (int iteration = 0; iteration < WarmupIterations; iteration++)
        {
            _ = FlushBufferedProvider(workingDirectory, iteration);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        ProcessSnapshot before = CaptureProcessSnapshot();
        long allocatedBytesBefore = GC.GetTotalAllocatedBytes(precise: true);

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            elapsedMicroseconds.Add(FlushBufferedProvider(workingDirectory, iteration + WarmupIterations));
        }

        long allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBytesBefore;
        ProcessSnapshot after = CaptureProcessSnapshot();
        PrintMeasurement("Buffered file logger flush (100 events)", elapsedMicroseconds, allocatedBytes, iterations);
        PrintResourceDelta("  process resource delta", before, after);
    }

    private static void MeasureHostBuild(string configurationPath, int iterations)
        => MeasureSync(
            "Generic Host build (configuration + options + hosted service)",
            iterations,
            _ =>
            {
                using IHost host = BuildBenchmarkHost(configurationPath);
            });

    private static void MeasureHostedServiceLifecycle(string configurationPath, int iterations)
    {
        List<double> startupMicroseconds = new(iterations);
        List<double> shutdownMicroseconds = new(iterations);

        for (int iteration = 0; iteration < WarmupIterations; iteration++)
        {
            RunHostLifecycleAsync(configurationPath).GetAwaiter().GetResult();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        ProcessSnapshot before = CaptureProcessSnapshot();
        long allocatedBytesBefore = GC.GetTotalAllocatedBytes(precise: true);

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            (double startup, double shutdown) = RunHostLifecycleAsync(configurationPath)
                .GetAwaiter()
                .GetResult();
            startupMicroseconds.Add(startup);
            shutdownMicroseconds.Add(shutdown);
        }

        long allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBytesBefore;
        ProcessSnapshot after = CaptureProcessSnapshot();
        PrintTiming("Generic Host StartAsync with options validation", startupMicroseconds);
        PrintTiming("Generic Host StopAsync + Dispose", shutdownMicroseconds);
        Console.WriteLine($"  process allocation per complete host cycle: {allocatedBytes / (double)iterations:F0} B/op");
        PrintResourceDelta("  process resource delta", before, after);
    }

    private static void MeasureHealthCheckExecution(JsonUserSettingsService settingsService, int iterations)
    {
        ServiceCollection services = new();
        services.AddSingleton(settingsService);
        services.AddLogging();
        services
            .AddHealthChecks()
            .AddCheck("ready", () => HealthCheckResult.Healthy())
            .AddCheck<SettingsStoreHealthCheck>(
                "settings-store",
                failureStatus: HealthStatus.Degraded);

        using ServiceProvider provider = services.BuildServiceProvider();
        HealthCheckService healthCheckService = provider.GetRequiredService<HealthCheckService>();

        MeasureAsync(
            "HealthCheckService (readiness + settings store)",
            iterations,
            async _ =>
            {
                HealthReport report = await healthCheckService
                    .CheckHealthAsync()
                    .ConfigureAwait(false);
                if (report.Status != HealthStatus.Healthy)
                {
                    throw new InvalidOperationException("The benchmark health checks did not report healthy.");
                }
            });
    }

    private static JsonUserSettingsService CreateSettingsService(string filePath)
        => new(new UserSettingsOptions
        {
            FilePath = filePath,
            SchemaVersion = 1
        });

    private static Task SaveSettingsAsync(
        JsonUserSettingsService settingsService,
        BenchmarkSettings settings)
        => settingsService.SaveAsync(settings);

    private static async Task RunPersistenceCycleAsync(
        JsonUserSettingsService settingsService,
        int sequence)
    {
        await SaveSettingsAsync(
            settingsService,
            new BenchmarkSettings { Sequence = sequence }).ConfigureAwait(false);
        _ = await settingsService
            .LoadAsync(new BenchmarkSettings())
            .ConfigureAwait(false);
    }

    private static void LogBatch(ILogger logger)
    {
        for (int eventNumber = 0; eventNumber < LogEventsPerBatch; eventNumber++)
        {
            s_logEvent(logger, eventNumber, null);
        }
    }

    private static double FlushBufferedProvider(string workingDirectory, int iteration)
    {
        string logPath = Path.Combine(workingDirectory, $"flush-{iteration}.log");
        BufferedFileLoggerProvider provider = new(logPath);
        ILoggerFactory loggerFactory = LoggerFactory.Create(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Information);
            logging.AddProvider(provider);
        });
        ILogger logger = loggerFactory.CreateLogger("FlushBenchmark");

        for (int eventNumber = 0; eventNumber < LogEventsPerFlush; eventNumber++)
        {
            s_logEvent(logger, eventNumber, null);
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            loggerFactory.Dispose();
        }
        finally
        {
            provider.Dispose();
            stopwatch.Stop();
        }

        if (File.ReadLines(logPath).Count() != LogEventsPerFlush)
        {
            throw new InvalidOperationException("The buffered logger did not flush all benchmark events.");
        }

        File.Delete(logPath);

        return stopwatch.Elapsed.TotalMicroseconds;
    }

    private static IHost BuildBenchmarkHost(string configurationPath)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = [],
            ContentRootPath = Path.GetDirectoryName(configurationPath)
        });
        builder.Services
            .AddOptions<BenchmarkOptions>()
            .Bind(builder.Configuration.GetSection("Benchmark"))
            .Validate(
                static options => options.ServiceIntervalSeconds > 0,
                "Benchmark:ServiceIntervalSeconds must be positive.")
            .ValidateOnStart();
        builder.Services.AddHostedService<NoOpHostedService>();
        builder.Logging.SetMinimumLevel(LogLevel.Critical);

        return builder.Build();
    }

    private static async Task<(double StartupMicroseconds, double ShutdownMicroseconds)> RunHostLifecycleAsync(
        string configurationPath)
    {
        IHost host = BuildBenchmarkHost(configurationPath);
        bool disposed = false;

        try
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            await host.StartAsync().ConfigureAwait(false);
            stopwatch.Stop();
            double startupMicroseconds = stopwatch.Elapsed.TotalMicroseconds;

            stopwatch.Restart();
            try
            {
                await host.StopAsync().ConfigureAwait(false);
            }
            finally
            {
                host.Dispose();
                disposed = true;
                stopwatch.Stop();
            }

            return (startupMicroseconds, stopwatch.Elapsed.TotalMicroseconds);
        }
        finally
        {
            if (!disposed)
            {
                host.Dispose();
            }
        }
    }

    private static void MeasureSync(string name, int iterations, Action<int> operation)
    {
        for (int iteration = 0; iteration < Math.Min(WarmupIterations, iterations); iteration++)
        {
            operation(iteration);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        ProcessSnapshot before = CaptureProcessSnapshot();
        long allocatedBytesBefore = GC.GetTotalAllocatedBytes(precise: true);
        List<double> elapsedMicroseconds = new(iterations);

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            operation(iteration);
            stopwatch.Stop();
            elapsedMicroseconds.Add(stopwatch.Elapsed.TotalMicroseconds);
        }

        long allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBytesBefore;
        ProcessSnapshot after = CaptureProcessSnapshot();
        PrintMeasurement(name, elapsedMicroseconds, allocatedBytes, iterations);
        PrintResourceDelta("  process resource delta", before, after);
    }

    private static void MeasureAsync(string name, int iterations, Func<int, Task> operation)
    {
        for (int iteration = 0; iteration < Math.Min(WarmupIterations, iterations); iteration++)
        {
            operation(iteration).GetAwaiter().GetResult();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        ProcessSnapshot before = CaptureProcessSnapshot();
        long allocatedBytesBefore = GC.GetTotalAllocatedBytes(precise: true);
        List<double> elapsedMicroseconds = new(iterations);

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            operation(iteration).GetAwaiter().GetResult();
            stopwatch.Stop();
            elapsedMicroseconds.Add(stopwatch.Elapsed.TotalMicroseconds);
        }

        long allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBytesBefore;
        ProcessSnapshot after = CaptureProcessSnapshot();
        PrintMeasurement(name, elapsedMicroseconds, allocatedBytes, iterations);
        PrintResourceDelta("  process resource delta", before, after);
    }

    private static void PrintMeasurement(
        string name,
        List<double> elapsedMicroseconds,
        long allocatedBytes,
        int iterations)
    {
        Console.WriteLine(
            $"  {name}: median {Percentile(elapsedMicroseconds, 0.50):F2} us/op, "
                + $"p95 {Percentile(elapsedMicroseconds, 0.95):F2} us/op, "
                + $"process allocation {allocatedBytes / (double)iterations:F0} B/op");
    }

    private static void PrintTiming(string name, List<double> elapsedMicroseconds)
    {
        Console.WriteLine(
            $"  {name}: median {Percentile(elapsedMicroseconds, 0.50):F2} us/op, "
                + $"p95 {Percentile(elapsedMicroseconds, 0.95):F2} us/op");
    }

    private static void PrintResourceDelta(string name, ProcessSnapshot previous, ProcessSnapshot current)
    {
        Console.WriteLine(
            $"{name}: handles {current.HandleCount - previous.HandleCount:+#;-#;0}, "
                + $"threads {current.ThreadCount - previous.ThreadCount:+#;-#;0}, "
                + $"working set {current.WorkingSetBytes - previous.WorkingSetBytes:+#;-#;0} B, "
                + $"private bytes {current.PrivateBytes - previous.PrivateBytes:+#;-#;0} B, "
                + $"managed live bytes {current.ManagedLiveBytes - previous.ManagedLiveBytes:+#;-#;0} B");
    }

    private static ProcessSnapshot CaptureProcessSnapshot()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        using Process process = Process.GetCurrentProcess();
        process.Refresh();

        return new ProcessSnapshot(
            process.HandleCount,
            process.Threads.Count,
            process.WorkingSet64,
            process.PrivateMemorySize64,
            GC.GetTotalMemory(forceFullCollection: true));
    }

    private static double Percentile(List<double> values, double percentile)
    {
        double[] sortedValues = [.. values.Order()];
        int index = Math.Clamp(
            (int)Math.Ceiling(percentile * sortedValues.Length) - 1,
            0,
            sortedValues.Length - 1);

        return sortedValues[index];
    }

    private static int GetIterationCount(string[] args)
    {
        int optionIndex = Array.IndexOf(args, "--infrastructure-iterations");
        if (optionIndex < 0)
        {
            return DefaultIterations;
        }

        if (optionIndex + 1 >= args.Length
            || !int.TryParse(args[optionIndex + 1], out int iterations)
            || iterations < 1)
        {
            throw new ArgumentException("--infrastructure-iterations requires a positive integer.");
        }

        return iterations;
    }

    /// <summary>
    ///  Holds settings values used by file-persistence measurements.
    /// </summary>
    private sealed class BenchmarkSettings
    {
        /// <summary>
        ///  Gets or sets a value that changes between persistence operations.
        /// </summary>
        public int Sequence { get; set; }
    }

    /// <summary>
    ///  Holds an isolated, pre-seeded settings file for one recovery measurement.
    /// </summary>
    private sealed record RecoveryFixture(JsonUserSettingsService SettingsService, string SettingsPath);

    /// <summary>
    ///  Holds configuration values resolved by the options benchmark.
    /// </summary>
    internal sealed class BenchmarkOptions
    {
        /// <summary>
        ///  Gets or sets the benchmark application name.
        /// </summary>
        public string ApplicationName { get; set; } = string.Empty;

        /// <summary>
        ///  Gets or sets the hosted-service interval.
        /// </summary>
        public int ServiceIntervalSeconds { get; set; }
    }

    /// <summary>
    ///  Starts and stops without additional work to isolate Generic Host lifecycle cost.
    /// </summary>
    private sealed class NoOpHostedService : IHostedService
    {
        /// <inheritdoc/>
        public Task StartAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        /// <inheritdoc/>
        public Task StopAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    /// <summary>
    ///  Checks settings-store readability during health-check measurements.
    /// </summary>
    private sealed class SettingsStoreHealthCheck(JsonUserSettingsService settingsService) : IHealthCheck
    {
        /// <inheritdoc/>
        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            int? version = await settingsService
                .GetStoredSchemaVersionAsync(cancellationToken)
                .ConfigureAwait(false);

            return HealthCheckResult.Healthy(
                version is int storedVersion
                    ? $"Settings schema version {storedVersion} is readable."
                    : "Settings are not initialized.");
        }
    }

    /// <summary>
    ///  Discards log events while exercising the logging provider pipeline.
    /// </summary>
    private sealed class DiscardingLoggerProvider : ILoggerProvider
    {
        /// <inheritdoc/>
        public ILogger CreateLogger(string categoryName)
            => DiscardingLogger.Instance;

        /// <inheritdoc/>
        public void Dispose()
        {
        }

        private sealed class DiscardingLogger : ILogger
        {
            private static readonly DiscardingLogger s_instance = new();

            internal static DiscardingLogger Instance => s_instance;

            /// <inheritdoc/>
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
                => null;

            /// <inheritdoc/>
            public bool IsEnabled(LogLevel logLevel)
                => logLevel >= LogLevel.Information;

            /// <inheritdoc/>
            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
            }
        }
    }

    /// <summary>
    ///  Buffers log events to a file and flushes them when the provider is disposed.
    /// </summary>
    private sealed class BufferedFileLoggerProvider : ILoggerProvider
    {
        private readonly Lock _sync = new();
        private readonly FileStream _fileStream;
        private readonly StreamWriter _writer;
        private bool _disposed;

        internal BufferedFileLoggerProvider(string filePath)
        {
            _fileStream = new FileStream(
                filePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.SequentialScan);
            _writer = new StreamWriter(_fileStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        /// <inheritdoc/>
        public ILogger CreateLogger(string categoryName)
            => new BufferedFileLogger(this);

        /// <inheritdoc/>
        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _writer.Flush();
                _fileStream.Flush(flushToDisk: true);
                _writer.Dispose();
                _fileStream.Dispose();
                _disposed = true;
            }
        }

        private void Write(string message)
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _writer.WriteLine(message);
            }
        }

        private sealed class BufferedFileLogger(BufferedFileLoggerProvider provider) : ILogger
        {
            /// <inheritdoc/>
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
                => null;

            /// <inheritdoc/>
            public bool IsEnabled(LogLevel logLevel)
                => logLevel >= LogLevel.Information;

            /// <inheritdoc/>
            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel))
                {
                    provider.Write(formatter(state, exception));
                }
            }
        }
    }

    /// <summary>
    ///  Captures coarse process-level resource counters.
    /// </summary>
    private sealed record ProcessSnapshot(
        int HandleCount,
        int ThreadCount,
        long WorkingSetBytes,
        long PrivateBytes,
        long ManagedLiveBytes);
}
