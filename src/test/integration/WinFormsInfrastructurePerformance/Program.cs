// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.WinForms;
using WinFormsLifetimePerformance;

namespace WinFormsInfrastructurePerformance;

/// <summary>
///  Measures complete infrastructure lifetimes and aligned UI-free operations in isolated processes.
/// </summary>
internal static class Program
{
    private const int CheckpointInterval = 25;
    private const int LogBatchSize = 100;
    private static readonly Action<ILogger, int, Exception?> s_logRecord =
        LoggerMessage.Define<int>(LogLevel.Information, new EventId(2100, "Probe"),
            "Infrastructure probe item {Index}");

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Run(args);

            return 0;
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            Console.Error.WriteLine(exception);

            return 1;
        }
    }

    private static void Run(string[] args)
    {
        string mode = "integrated";
        string workload = "all";
        int cycles = 100;
        int warmup = 5;
        bool watchers = false;
        bool componentManager = false;
        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--mode" when index + 1 < args.Length:
                    mode = args[++index];
                    break;
                case "--workload" when index + 1 < args.Length:
                    workload = args[++index];
                    break;
                case "--cycles" when index + 1 < args.Length:
                    cycles = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    break;
                case "--warmup" when index + 1 < args.Length:
                    warmup = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    break;
                case "--watchers":
                    watchers = true;
                    break;
                case "--component-manager":
                    componentManager = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown or incomplete argument: {args[index]}");
            }
        }

        if (mode is not ("hostless" or "conventional" or "integrated")
            || workload is not ("empty" or "configuration" or "settings" or "logging" or "health" or "all")
            || (mode == "hostless" && (workload != "empty" || watchers))
            || (watchers && workload is not ("configuration" or "all")))
        {
            throw new ArgumentException("Select a supported mode/workload; hostless requires empty, watchers require configuration.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(cycles, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(cycles, 100000);
        ArgumentOutOfRangeException.ThrowIfNegative(warmup);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(warmup, 1000);
        AppContext.SetSwitch("Switch.System.Windows.Forms.EnableMsoComponentManager", componentManager);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string directory = Path.Combine(Path.GetTempPath(), $"WinFormsInfrastructureProbe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "appsettings.json"), """{"Desktop":{"Caption":"base"}}""");
            File.WriteAllText(Path.Combine(directory, "appsettings.Production.json"), """{"Desktop":{"Caption":"production"}}""");
            int total = checked(1 + warmup + cycles);
            WeakReference[] forms = CreateReferences(total);
            WeakReference[] applications = CreateReferences(total);
            WeakReference[] providers = CreateReferences(total);
            CycleResult[] results = new CycleResult[cycles];
            for (int index = 0; index < cycles; index++)
            {
                results[index] = new CycleResult();
            }

            List<ResourceCheckpoint> checkpoints = new(2 + cycles / CheckpointInterval);
            using Process process = Process.GetCurrentProcess();
            CycleResult first = new();
            RunCycle(mode, workload, watchers, directory, first, forms[0], applications[0], providers[0], true);
            CycleResult warmupResult = new();
            for (int index = 0; index < warmup; index++)
            {
                warmupResult.Operations.Clear();
                RunCycle(mode, workload, watchers, directory, warmupResult,
                    forms[index + 1], applications[index + 1], providers[index + 1], false);
            }

            checkpoints.Add(Snapshot(process, 0, forms, applications, providers));
            for (int index = 0; index < cycles; index++)
            {
                int target = 1 + warmup + index;
                RunCycle(mode, workload, watchers, directory, results[index],
                    forms[target], applications[target], providers[target], false);
                if ((index + 1) % CheckpointInterval == 0 || index + 1 == cycles)
                {
                    checkpoints.Add(Snapshot(process, index + 1, forms, applications, providers));
                }
            }

            MeasurementReport report = new(mode, workload, watchers, componentManager ? "component-manager" : "light",
                RuntimeInformation.FrameworkDescription, RuntimeInformation.ProcessArchitecture.ToString(),
                RuntimeInformation.OSDescription, SystemInformation.TerminalServerSession,
#if DEBUG
                true,
#else
                false,
#endif
                warmup, first, results, [.. checkpoints]);
            Console.WriteLine(JsonSerializer.Serialize(report, ProbeJsonContext.Default.MeasurementReport));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static WeakReference[] CreateReferences(int count)
        => Enumerable.Range(0, count).Select(_ => new WeakReference(null)).ToArray();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunCycle(
        string mode, string workload, bool watchers, string directory, CycleResult result,
        WeakReference formReference, WeakReference applicationReference, WeakReference providerReference, bool signalReady)
    {
        CycleState state = new();
        bool settings = workload is "settings" or "all";
        bool logging = workload is "logging" or "all";
        bool health = workload is "health" or "all";
        bool configuration = workload is "configuration" or "all";
        long allocated = GC.GetTotalAllocatedBytes(precise: true);
        long started = Stopwatch.GetTimestamp();
        long dispatched = 0;
        long closing = 0;
        nint window = 0;
        int disposals = 0;
        Task? work = null;
        Task? completion = null;
        IHost? host = null;
        WinFormsApplication? application = null;
        using Form form = new()
        {
            Text = "Infrastructure measurement",
            ClientSize = new Size(160, 80),
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(0, 0)
        };
        form.Disposed += (_, _) =>
        {
            state.UiDisposed = true;
            disposals++;
        };
        form.Shown += (_, _) =>
        {
            window = form.Handle;
            form.BeginInvoke(() =>
            {
                dispatched = Stopwatch.GetTimestamp();
                if (signalReady)
                {
                    Console.WriteLine("READY");
                    Console.Out.Flush();
                }

                if (workload == "empty")
                {
                    closing = Stopwatch.GetTimestamp();
                    form.Close();
                }
                else
                {
                    IServiceProvider provider = (application?.Host ?? host)!.Services;
                    work = Task.Run(() => ExerciseAsync(provider, directory, result, configuration, settings, logging, health));
                    completion = work.ContinueWith(_ =>
                    {
                        form.BeginInvoke(() =>
                        {
                            closing = Stopwatch.GetTimestamp();
                            form.Close();
                        });
                    }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }
            });
        };
        try
        {
            result.ConfigureBuild = Measure(() =>
            {
                if (mode == "hostless")
                {
                    return;
                }

                IHostBuilder builder = CreateHost(mode, directory, watchers, state, configuration, settings, logging, health);
                if (mode == "conventional")
                {
                    host = builder.Build();
                }
                else
                {
                    application = WinFormsApplication.CreateBuilder().UseHost(builder).UseStartupForm(form).Build();
                }
            });
            if (mode == "integrated")
            {
                application!.Run();
                application.Dispose();
            }
            else
            {
                if (host is not null)
                {
                    Task.Run(() => host.StartAsync()).GetAwaiter().GetResult();
                }

                Application.Run(form);
                if (host is not null)
                {
                    Task.Run(() => host.StopAsync()).GetAwaiter().GetResult();
                    host.Dispose();
                }
            }

            long finished = Stopwatch.GetTimestamp();
            result.Lifetime = CompleteMeasurement(started, allocated, finished: finished);
            result.StartupToDispatchMilliseconds = Stopwatch.GetElapsedTime(started, dispatched).TotalMilliseconds;
            result.ShutdownMilliseconds = Stopwatch.GetElapsedTime(closing, finished).TotalMilliseconds;
            completion?.GetAwaiter().GetResult();
            work?.GetAwaiter().GetResult();

            if (dispatched == 0 || closing == 0 || disposals != 1 || !form.IsDisposed
                || form.IsHandleCreated || NativeMethods.IsWindow(window) || Application.MessageLoop)
            {
                throw new InvalidOperationException(
                    $"Incomplete native UI ownership cycle: dispatched={dispatched}, closing={closing}, "
                    + $"disposals={disposals}, disposed={form.IsDisposed}, handle={form.IsHandleCreated}, "
                    + $"native={NativeMethods.IsWindow(window)}, loop={Application.MessageLoop}.");
            }

            result.WorkerStopped = state.WorkerStopped;
            result.FlushAfterUi = state.FlushAfterUi;
            result.FlushedRecords = state.FlushedRecords;
            result.DisposedServices = state.DisposedServices;
            result.HealthScopeDisposals = state.HealthScopeDisposals;
            if ((health && (!state.WorkerStopped || state.HealthScopeDisposals != 1))
                || (logging && (!state.FlushAfterUi || state.DisposedServices != 1 || state.FlushedRecords != LogBatchSize)))
            {
                throw new InvalidOperationException("Hosted work or logging did not complete before provider disposal.");
            }

            if (logging)
            {
                result.Operations.Add("ShutdownFlush", state.Flush);
            }

            foreach (string file in Directory.GetFiles(directory))
            {
                using FileStream exclusive = new(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                result.ReleasedFiles++;
            }

            formReference.Target = form;
            applicationReference.Target = application is not null ? application : host;
            providerReference.Target = (application?.Host ?? host)?.Services;
        }
        finally
        {
            application?.Dispose();
            host?.Dispose();
        }
    }

    private static IHostBuilder CreateHost(
        string mode, string directory, bool watchers, CycleState state,
        bool configuration, bool settings, bool logging, bool health)
    {
        IHostBuilder builder = new HostBuilder().UseEnvironment("Production").UseContentRoot(directory);
        if (configuration)
        {
            if (mode == "integrated")
            {
                builder.ConfigureWinFormsConfiguration(
                    [$"--contentRoot={directory}", "--environment=Production",
                        $"--hostBuilder:reloadConfigOnChange={watchers}", "--Desktop:Caption=command-line"]);
            }
            else
            {
                builder.ConfigureHostConfiguration(config => config.AddEnvironmentVariables("DOTNET_")
                    .AddInMemoryCollection([new("contentRoot", directory), new("environment", "Production")]));
                builder.ConfigureAppConfiguration((context, config) => config
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: watchers)
                    .AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: watchers)
                    .AddJsonFile("InfrastructureProbe.settings.json", optional: true, reloadOnChange: watchers)
                    .AddJsonFile("InfrastructureProbe.settings.Production.json", optional: true, reloadOnChange: watchers)
                    .AddEnvironmentVariables()
                    .AddCommandLine(["--Desktop:Caption=command-line"]));
            }
        }

        builder.ConfigureHostConfiguration(config =>
            config.AddInMemoryCollection([new(HostDefaults.ApplicationKey, "InfrastructureProbe")]));
        return builder.ConfigureServices((_, services) =>
        {
            services.Configure<HostOptions>(options => options.ShutdownTimeout = Timeout.InfiniteTimeSpan);
            if (configuration)
            {
                services.AddOptions<ProbeOptions>().BindConfiguration("Desktop")
                    .Validate(options => options.Caption == "command-line", "Configuration precedence changed.")
                    .ValidateOnStart();
            }

            if (settings)
            {
                services.AddJsonUserSettings("InfrastructureMeasurement", 2,
                    ProbeJsonContext.Default.ProbePreferences, () => new ProbePreferences(),
                    options =>
                    {
                        options.StorageDirectory = directory;
                        options.Migrations.Add(1, old => old.Clone());
                    });
            }

            if (logging)
            {
                services.AddLogging(options =>
                {
                    options.SetMinimumLevel(LogLevel.Information);
                    options.AddFilter("Microsoft", LogLevel.None);
                });
                services.AddSingleton(_ => new BufferedProbeLogger(state));
                services.AddSingleton<ILoggerProvider>(provider => provider.GetRequiredService<BufferedProbeLogger>());
                services.AddSingleton<IHostedService>(provider => new ProbeFlushService(
                    provider.GetRequiredService<BufferedProbeLogger>(), state));
            }

            if (health)
            {
                if (mode == "integrated")
                {
                    services.AddWinFormsHealthDiagnostics();
                }
                else
                {
                    services.AddHealthChecks();
                }

                services.AddScoped(_ => new ProbeHealthCheck(state));
                services.AddHealthChecks().AddCheck<ProbeHealthCheck>("local");
                services.AddSingleton<IHostedService>(_ => new ProbeWorker(state));
            }
        });
    }

    private static async Task ExerciseAsync(IServiceProvider provider, string directory, CycleResult result,
        bool configuration, bool settings, bool logging, bool health)
    {
        if (configuration)
        {
            result.Operations.Add("OptionsRead", Measure(() =>
            {
                var options = provider.GetRequiredService<IOptionsMonitor<ProbeOptions>>();
                for (int index = 0; index < LogBatchSize; index++)
                {
                    if (options.CurrentValue.Caption != "command-line")
                    {
                        throw new InvalidOperationException("Unexpected configuration.");
                    }
                }
            }, LogBatchSize));
            result.Operations.Add("ConfigurationReload", Measure(() =>
                ((IConfigurationRoot)provider.GetRequiredService<IConfiguration>()).Reload()));
        }

        if (settings)
        {
            var store = provider.GetRequiredService<IUserSettingsService<ProbePreferences>>();
            int notifications = 0;
            EventHandler<UserSettingsChangedEventArgs> changed = (_, _) => Interlocked.Increment(ref notifications);
            store.Changed += changed;
            result.Operations.Add("SettingsSave", await MeasureAsync(async () =>
            {
                await CommitAsync(store.SaveAsync(new ProbePreferences()));
                await CommitAsync(store.SaveAsync(new ProbePreferences { Value = 84 }));
            }, 2).ConfigureAwait(false));
            result.Operations.Add("SettingsLoad", await MeasureAsync(async () =>
            {
                if ((await store.LoadAsync().ConfigureAwait(false)).Value != 84)
                {
                    throw new InvalidOperationException("Settings roundtrip failed.");
                }
            }).ConfigureAwait(false));
            result.Operations.Add("FailureRecovery", await MeasureAsync(async () =>
            {
                File.WriteAllText(Path.Combine(directory, "settings.json"), "{damaged");
                try
                {
                    await store.LoadAsync().ConfigureAwait(false);
                    throw new InvalidOperationException("Corruption was silently accepted.");
                }
                catch (InvalidDataException)
                {
                    result.ExpectedFailures++;
                }

                await CommitAsync(store.RecoverAsync());
                if ((await store.LoadAsync().ConfigureAwait(false)).Value != 42)
                {
                    throw new InvalidOperationException("Recovery did not preserve the backup.");
                }
            }).ConfigureAwait(false));
            result.Operations.Add("SettingsUpgrade", await MeasureAsync(async () =>
            {
                File.WriteAllText(Path.Combine(directory, "settings.json"),
                    """{"schemaVersion":1,"revision":1,"settings":{"Value":42}}""");
                await CommitAsync(store.UpgradeAsync());
            }).ConfigureAwait(false));
            result.Operations.Add("SettingsReset", await MeasureAsync(() => CommitAsync(store.ResetAsync())).ConfigureAwait(false));
            foreach (string file in Directory.GetFiles(directory, "settings.json.corrupt.*"))
            {
                File.Delete(file);
            }

            store.Changed -= changed;
            result.NotificationCount = notifications;
            if (notifications != 5)
            {
                throw new InvalidOperationException("Settings commit notifications did not finish in the measured operation.");
            }
        }

        if (logging)
        {
            var factory = provider.GetRequiredService<ILoggerFactory>();
            ILogger enabled = factory.CreateLogger("Probe");
            ILogger disabled = factory.CreateLogger("Microsoft.Probe");
            result.Operations.Add("LoggingDisabled", Measure(() =>
            {
                for (int index = 0; index < LogBatchSize; index++)
                {
                    s_logRecord(disabled, index, null);
                }
            }, LogBatchSize));
            result.Operations.Add("LoggingBuffered", Measure(() =>
            {
                for (int index = 0; index < LogBatchSize; index++)
                {
                    s_logRecord(enabled, index, null);
                }
            }, LogBatchSize));
        }

        if (health)
        {
            result.Operations.Add("HealthCheck", await MeasureAsync(async () =>
            {
                HealthReport report;
                var diagnostics = provider.GetService<WinFormsHealthDiagnostics>();
                if (diagnostics is not null)
                {
                    report = await diagnostics.CheckHealthAsync().ConfigureAwait(false);
                }
                else
                {
                    using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
                        provider.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
                    await using AsyncServiceScope scope = provider.CreateAsyncScope();
                    report = await scope.ServiceProvider.GetRequiredService<HealthCheckService>()
                        .CheckHealthAsync(linked.Token).ConfigureAwait(false);
                }

                if (report.Status != HealthStatus.Healthy)
                {
                    throw new InvalidOperationException("Health workload did not complete.");
                }
            }).ConfigureAwait(false));
        }
    }

    private static async Task CommitAsync(Task<UserSettingsCommit> operation)
    {
        UserSettingsCommit commit = await operation.ConfigureAwait(false);
        await commit.NotificationCompletion.ConfigureAwait(false);
    }

    internal static OperationMeasurement Measure(Action action, int operations = 1)
    {
        long allocated = GC.GetTotalAllocatedBytes(precise: true);
        long started = Stopwatch.GetTimestamp();
        action();

        return CompleteMeasurement(started, allocated, operations);
    }

    private static async Task<OperationMeasurement> MeasureAsync(Func<Task> action, int operations = 1)
    {
        long allocated = GC.GetTotalAllocatedBytes(precise: true);
        long started = Stopwatch.GetTimestamp();
        await action().ConfigureAwait(false);

        return CompleteMeasurement(started, allocated, operations);
    }

    private static OperationMeasurement CompleteMeasurement(long started, long allocated, int operations = 1, long finished = 0)
    {
        double milliseconds = Stopwatch.GetElapsedTime(started, finished == 0 ? Stopwatch.GetTimestamp() : finished).TotalMilliseconds;
        long bytes = GC.GetTotalAllocatedBytes(precise: true) - allocated;

        return new OperationMeasurement(milliseconds, bytes, operations);
    }

    private static ResourceCheckpoint Snapshot(Process process, int completed, WeakReference[] forms,
        WeakReference[] applications, WeakReference[] providers)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        process.Refresh();
        ProcessThreadCollection threads = process.Threads;
        int count = threads.Count;
        foreach (ProcessThread thread in threads)
        {
            thread.Dispose();
        }

        return new ResourceCheckpoint(completed, GC.GetTotalMemory(false), process.PrivateMemorySize64,
            process.WorkingSet64, count, process.HandleCount,
            NativeMethods.ReadGuiResources(process, false), NativeMethods.ReadGuiResources(process, true),
            forms.Count(reference => reference.IsAlive), applications.Count(reference => reference.IsAlive),
            providers.Count(reference => reference.IsAlive));
    }
}
