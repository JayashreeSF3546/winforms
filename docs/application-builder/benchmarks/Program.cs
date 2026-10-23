// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.WinForms;

namespace WinFormsApplicationBuilder.Benchmarks;

/// <summary>
///  Runs STA-aware startup, shutdown, allocation, and resource measurements.
/// </summary>
internal static partial class Program
{
    private const string WorkerResultPrefix = "BENCHMARK_RESULT:";
    private const int DefaultColdIterations = 5;
    private const int DefaultWarmIterations = 30;
    private const int DefaultResourceIterations = 100;
    private const int DefaultActivationIterations = 5;
    private const int MinimumSplashDisplayMilliseconds = 50;
    private const int WarmupIterations = 5;

    /// <summary>
    ///  Runs the coordinator or one STA worker process.
    /// </summary>
    /// <param name="args">The benchmark mode and optional iteration counts.</param>
    /// <returns>Zero when all measurements complete successfully.</returns>
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "--worker")
            {
                RunWorker(args);
                return 0;
            }

            if (args.Length > 0
                && (args[0] == "--activation-primary" || args[0] == "--activation-secondary"))
            {
                RunActivationWorker(args);
                return 0;
            }

            RunBenchmarks(args);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void RunBenchmarks(string[] args)
    {
        int coldIterations = GetIterationCount(args, "--cold-iterations", DefaultColdIterations);
        int warmIterations = GetIterationCount(args, "--warm-iterations", DefaultWarmIterations);
        int resourceIterations = GetIterationCount(args, "--resource-iterations", DefaultResourceIterations);
        int activationIterations = GetIterationCount(
            args,
            "--activation-iterations",
            DefaultActivationIterations);
        Scenario[] scenarios = Enum.GetValues<Scenario>();

        Console.WriteLine("VB Application Framework and WinForms Application Builder benchmark");
        Console.WriteLine($"Runtime: {RuntimeInformation.FrameworkDescription}");
        Console.WriteLine($"Operating system: {RuntimeInformation.OSDescription}");
        Console.WriteLine($"Process architecture: {RuntimeInformation.ProcessArchitecture}");
        Console.WriteLine($"Cold process launches per scenario: {coldIterations}");
        Console.WriteLine($"Warm iterations per scenario: {warmIterations}");
        Console.WriteLine($"Resource-stability cycles per scenario: {resourceIterations}");
        Console.WriteLine($"Single-instance activation iterations per model: {activationIterations}");
        Console.WriteLine();

        foreach (Scenario scenario in scenarios)
        {
            Console.WriteLine($"Scenario: {scenario}");

            List<double> coldProcessMilliseconds = [];
            for (int iteration = 0; iteration < coldIterations; iteration++)
            {
                WorkerExecution execution = RunWorkerProcess(scenario, WorkerMode.Cold, iterationCount: 1);
                coldProcessMilliseconds.Add(execution.ProcessElapsedMilliseconds);
            }

            WorkerPayload warmPayload = RunWorkerProcess(
                scenario,
                WorkerMode.Warm,
                iterationCount: warmIterations).Payload;
            WorkerPayload resourcePayload = RunWorkerProcess(
                scenario,
                WorkerMode.Resources,
                iterationCount: resourceIterations).Payload;

            PrintTimingResults(coldProcessMilliseconds, warmPayload.Measurements);
            PrintResourceResults(resourcePayload, resourceIterations);
            Console.WriteLine();
        }

        RunActivationBenchmarks(activationIterations);
        InfrastructureBenchmarks.Run(args);

        Console.WriteLine("These comparative measurements are diagnostic, not CI thresholds.");
    }

    private static void RunWorker(string[] args)
    {
        if (args.Length != 4
            || !Enum.TryParse(args[1], ignoreCase: true, out Scenario scenario)
            || !Enum.TryParse(args[2], ignoreCase: true, out WorkerMode mode)
            || !int.TryParse(args[3], out int iterationCount)
            || iterationCount < 1)
        {
            throw new ArgumentException(
                "Worker usage: --worker <scenario> <cold|warm|resources> <iteration-count>");
        }

        if (scenario is not (Scenario.VisualBasicFramework
            or Scenario.VisualBasicFrameworkWithSplash
            or Scenario.VisualBasicFrameworkWithNetworkListener))
        {
            ConfigureWinForms();
        }

        WorkerPayload payload = mode switch
        {
            WorkerMode.Cold => CreatePayload(scenario, [RunOnce(scenario)], []),
            WorkerMode.Warm => RunWarmWorker(scenario, iterationCount),
            WorkerMode.Resources => RunResourceWorker(scenario, iterationCount),
            _ => throw new InvalidOperationException($"Unsupported benchmark mode: {mode}.")
        };

        Console.WriteLine(
            $"{WorkerResultPrefix}{JsonSerializer.Serialize(payload, BenchmarkJsonContext.Default.WorkerPayload)}");
    }

    private static WorkerPayload RunWarmWorker(Scenario scenario, int iterationCount)
    {
        for (int iteration = 0; iteration < WarmupIterations; iteration++)
        {
            _ = RunOnce(scenario);
        }

        RunMeasurement[] measurements = new RunMeasurement[iterationCount];
        for (int iteration = 0; iteration < iterationCount; iteration++)
        {
            measurements[iteration] = RunOnce(scenario);
        }

        return CreatePayload(scenario, measurements, []);
    }

    private static WorkerPayload RunResourceWorker(Scenario scenario, int iterationCount)
    {
        for (int iteration = 0; iteration < WarmupIterations; iteration++)
        {
            _ = RunOnce(scenario);
        }

        List<ProcessSnapshot> snapshots = [CaptureProcessSnapshot()];
        int cyclesPerCheckpoint = Math.Max(1, (int)Math.Ceiling(iterationCount / 4.0));
        int completedIterations = 0;

        while (completedIterations < iterationCount)
        {
            int checkpointIterations = Math.Min(
                cyclesPerCheckpoint,
                iterationCount - completedIterations);
            for (int iteration = 0; iteration < checkpointIterations; iteration++)
            {
                _ = RunOnce(scenario);
            }

            completedIterations += checkpointIterations;
            snapshots.Add(CaptureProcessSnapshot());
        }

        return CreatePayload(scenario, [], [.. snapshots]);
    }

    private static WorkerPayload CreatePayload(
        Scenario scenario,
        RunMeasurement[] measurements,
        ProcessSnapshot[] snapshots)
        => new(scenario, measurements, snapshots);

    private static RunMeasurement RunOnce(Scenario scenario)
    {
        Stopwatch totalTimer = Stopwatch.StartNew();
        long allocatedBytesBefore = GC.GetAllocatedBytesForCurrentThread();
        double shownMilliseconds = double.NaN;
        double closedMilliseconds = double.NaN;
        Form? form = null;

        try
        {
            Form CreateMainForm()
            {
                Form startupForm = CreateBenchmarkForm();
                form = startupForm;
                startupForm.Shown += (_, _) =>
                {
                    shownMilliseconds = totalTimer.Elapsed.TotalMilliseconds;
                    startupForm.BeginInvoke(startupForm.Close);
                };
                startupForm.FormClosed += (_, _) => closedMilliseconds = totalTimer.Elapsed.TotalMilliseconds;

                return startupForm;
            }

            if (scenario is Scenario.VisualBasicFramework
                or Scenario.VisualBasicFrameworkWithSplash
                or Scenario.VisualBasicFrameworkWithNetworkListener)
            {
                bool useSplashScreen = scenario == Scenario.VisualBasicFrameworkWithSplash;
                BenchmarkVisualBasicApplication application = new(
                    CreateMainForm,
                    useSplashScreen ? CreateBenchmarkForm : null);
                if (scenario == Scenario.VisualBasicFrameworkWithNetworkListener)
                {
                    application.ListenForNetworkAvailability();
                }

                application.Run([]);
            }
            else
            {
                Form startupForm = CreateMainForm();
                Microsoft.Extensions.WinForms.WinFormsApplicationBuilder builder =
                    WinFormsApplication.CreateBuilder();

                if (scenario is Scenario.BuilderWithSplash)
                {
                    builder.UseSplashScreen(
                        CreateBenchmarkForm,
                        TimeSpan.FromMilliseconds(MinimumSplashDisplayMilliseconds));
                }

                if (scenario is Scenario.BuilderWithNetworkListener)
                {
                    builder.AddApplicationServices();
                    builder.UseStartupForm(services =>
                    {
                        services.GetRequiredService<IWinFormsNetworkAvailabilityService>()
                            .NetworkAvailabilityChanged += static (_, _) => { };
                        return startupForm;
                    });
                }
                else
                {
                    builder.UseStartupForm(startupForm);
                }

                if (scenario is Scenario.BuilderWithHost)
                {
                    HostApplicationBuilder hostBuilder = Host.CreateApplicationBuilder();
                    IHost host = hostBuilder.Build();
                    builder.UseHost(host);
                }

                using WinFormsApplication application = builder.Build();
                application.Run();
            }
        }
        finally
        {
            form?.Dispose();
        }

        totalTimer.Stop();

        return new RunMeasurement(
            shownMilliseconds,
            totalTimer.Elapsed.TotalMilliseconds - closedMilliseconds,
            totalTimer.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBytesBefore);
    }

    private static void ConfigureWinForms()
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
    }

    private static Form CreateBenchmarkForm()
        => new()
        {
            FormBorderStyle = FormBorderStyle.None,
            Location = new Point(-32000, -32000),
            Opacity = 0,
            ShowInTaskbar = false,
            Size = new Size(1, 1),
            WindowState = FormWindowState.Minimized
        };

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

    private static WorkerExecution RunWorkerProcess(
        Scenario scenario,
        WorkerMode mode,
        int iterationCount)
    {
        string executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("The benchmark executable path is unavailable.");
        ProcessStartInfo startInfo = new(executablePath)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        startInfo.ArgumentList.Add("--worker");
        startInfo.ArgumentList.Add(scenario.ToString());
        startInfo.ArgumentList.Add(mode.ToString());
        startInfo.ArgumentList.Add(iterationCount.ToString());

        using Process process = new() { StartInfo = startInfo };
        Stopwatch processTimer = Stopwatch.StartNew();
        if (!process.Start())
        {
            throw new InvalidOperationException($"Could not start benchmark worker for {scenario}.");
        }

        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(outputTask, errorTask);
        processTimer.Stop();

        string standardOutput = outputTask.Result;
        string standardError = errorTask.Result;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Benchmark worker for {scenario} failed with exit code {process.ExitCode}."
                    + Environment.NewLine
                    + standardOutput
                    + standardError);
        }

        string? resultLine = standardOutput
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault(line => line.StartsWith(WorkerResultPrefix, StringComparison.Ordinal));
        string json = resultLine
            ?? throw new InvalidOperationException(
                $"Benchmark worker for {scenario} did not return a result."
                    + Environment.NewLine
                    + standardOutput
                    + standardError);

        WorkerPayload payload = JsonSerializer.Deserialize(
            json[WorkerResultPrefix.Length..],
            BenchmarkJsonContext.Default.WorkerPayload)
            ?? throw new InvalidOperationException(
                $"Benchmark worker for {scenario} returned invalid JSON.");

        return new WorkerExecution(payload, processTimer.Elapsed.TotalMilliseconds);
    }

    private static int GetIterationCount(string[] args, string option, int defaultValue)
    {
        int optionIndex = Array.IndexOf(args, option);
        if (optionIndex < 0)
        {
            return defaultValue;
        }

        if (optionIndex + 1 >= args.Length
            || !int.TryParse(args[optionIndex + 1], out int iterationCount)
            || iterationCount < 1)
        {
            throw new ArgumentException($"{option} requires a positive integer.");
        }

        return iterationCount;
    }

    private static void PrintTimingResults(
        List<double> coldProcessMilliseconds,
        RunMeasurement[] warmMeasurements)
    {
        Console.WriteLine(
            $"  Cold process launch-to-exit ms: median {Percentile(coldProcessMilliseconds, 0.50):F2}, "
                + $"p95 {Percentile(coldProcessMilliseconds, 0.95):F2}");
        Console.WriteLine(
            $"  Warm scenario-to-form-shown ms: median {Percentile(warmMeasurements.Select(item => item.ShownMilliseconds), 0.50):F2}, "
                + $"p95 {Percentile(warmMeasurements.Select(item => item.ShownMilliseconds), 0.95):F2}");
        Console.WriteLine(
            $"  Warm form-closed-to-dispose ms: median {Percentile(warmMeasurements.Select(item => item.ShutdownMilliseconds), 0.50):F2}, "
                + $"p95 {Percentile(warmMeasurements.Select(item => item.ShutdownMilliseconds), 0.95):F2}");
        Console.WriteLine(
            $"  Warm total lifecycle ms: median {Percentile(warmMeasurements.Select(item => item.TotalMilliseconds), 0.50):F2}, "
                + $"p95 {Percentile(warmMeasurements.Select(item => item.TotalMilliseconds), 0.95):F2}");
        Console.WriteLine(
            $"  UI-thread allocated bytes: median {Percentile(warmMeasurements.Select(item => (double)item.UiThreadAllocatedBytes), 0.50):F0}, "
                + $"p95 {Percentile(warmMeasurements.Select(item => (double)item.UiThreadAllocatedBytes), 0.95):F0}");
    }

    private static void PrintResourceResults(WorkerPayload payload, int iterationCount)
    {
        if (payload.Snapshots.Length < 2)
        {
            throw new InvalidOperationException("The resource worker did not return interval snapshots.");
        }

        int cyclesPerCheckpoint = (int)Math.Ceiling(
            iterationCount / (double)(payload.Snapshots.Length - 1));
        for (int snapshotIndex = 1; snapshotIndex < payload.Snapshots.Length; snapshotIndex++)
        {
            ProcessSnapshot previous = payload.Snapshots[snapshotIndex - 1];
            ProcessSnapshot current = payload.Snapshots[snapshotIndex];
            int cycleCount = Math.Min(snapshotIndex * cyclesPerCheckpoint, iterationCount);

            Console.WriteLine(
                $"  Resource delta after cycle {cycleCount} (post-GC): "
                    + $"handles {current.HandleCount - previous.HandleCount:+#;-#;0}, "
                    + $"threads {current.ThreadCount - previous.ThreadCount:+#;-#;0}, "
                    + $"working set {current.WorkingSetBytes - previous.WorkingSetBytes:+#;-#;0}, "
                    + $"private bytes {current.PrivateBytes - previous.PrivateBytes:+#;-#;0}, "
                    + $"managed live bytes {current.ManagedLiveBytes - previous.ManagedLiveBytes:+#;-#;0}");
        }
    }

    private static double Percentile(IEnumerable<double> values, double percentile)
    {
        double[] sortedValues = [.. values.Order()];
        int index = Math.Clamp((int)Math.Ceiling(percentile * sortedValues.Length) - 1, 0, sortedValues.Length - 1);

        return sortedValues[index];
    }

    /// <summary>
    ///  Identifies the baseline and hosting scenarios.
    /// </summary>
    private enum Scenario
    {
        ApplicationRun,
        VisualBasicFramework,
        VisualBasicFrameworkWithSplash,
        VisualBasicFrameworkWithNetworkListener,
        Builder,
        BuilderWithHost,
        BuilderWithSplash,
        BuilderWithNetworkListener
    }

    /// <summary>
    ///  Identifies a benchmark worker's measurement mode.
    /// </summary>
    private enum WorkerMode
    {
        Cold,
        Warm,
        Resources
    }

    /// <summary>
    ///  Holds timing and UI-thread allocation measurements for one run.
    /// </summary>
    private sealed record RunMeasurement(
        double ShownMilliseconds,
        double ShutdownMilliseconds,
        double TotalMilliseconds,
        long UiThreadAllocatedBytes);

    /// <summary>
    ///  Holds process and managed-heap resource counters.
    /// </summary>
    private sealed record ProcessSnapshot(
        int HandleCount,
        int ThreadCount,
        long WorkingSetBytes,
        long PrivateBytes,
        long ManagedLiveBytes);

    /// <summary>
    ///  Carries a worker's measurements and optional resource snapshots.
    /// </summary>
    private sealed record WorkerPayload(
        Scenario Scenario,
        RunMeasurement[] Measurements,
        ProcessSnapshot[] Snapshots);

    /// <summary>
    ///  Carries worker results and parent-measured process launch duration.
    /// </summary>
    private sealed record WorkerExecution(WorkerPayload Payload, double ProcessElapsedMilliseconds);

    [JsonSerializable(typeof(WorkerPayload))]
    private sealed partial class BenchmarkJsonContext : JsonSerializerContext
    {
    }
}
