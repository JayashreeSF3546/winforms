// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.WinForms;

namespace WinFormsLifetimePerformance;

/// <summary>
///  Measures fresh application lifetimes on one caller-owned STA without timing assertions.
/// </summary>
internal static class Program
{
    private const int CheckpointInterval = 25;

    [STAThread]
    private static void Main(string[] args)
    {
        string mode = "builder";
        int cycles = 100;
        int warmup = 5;
        bool componentManager = false;
        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--mode" when index + 1 < args.Length:
                    mode = args[++index];
                    break;
                case "--cycles" when index + 1 < args.Length:
                    cycles = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    break;
                case "--warmup" when index + 1 < args.Length:
                    warmup = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    break;
                case "--component-manager":
                    componentManager = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown or incomplete argument: {args[index]}");
            }
        }

        if (mode is not ("legacy" or "builder" or "hosted"))
        {
            throw new ArgumentException("Expected --mode legacy, builder, or hosted.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(cycles, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(warmup);
        int totalCycles = checked(1 + warmup + cycles);
        WeakReference[] forms = new WeakReference[totalCycles];
        WeakReference[] applications = new WeakReference[totalCycles];
        for (int index = 0; index < totalCycles; index++)
        {
            forms[index] = new WeakReference(null);
            applications[index] = new WeakReference(null);
        }

        CycleMeasurement firstUse = new();
        CycleMeasurement warmupMeasurement = new();
        CycleMeasurement[] measurements = new CycleMeasurement[cycles];
        for (int index = 0; index < cycles; index++)
        {
            measurements[index] = new CycleMeasurement();
        }

        ResourceSnapshot[] checkpoints = new ResourceSnapshot[1 + ((cycles - 1) / CheckpointInterval) + 1];
        using Process process = Process.GetCurrentProcess();
        ResourceSnapshot initial = Snapshot(process, 0, forms, applications);
        long configurationStarted = Stopwatch.GetTimestamp();
        AppContext.SetSwitch("Switch.System.Windows.Forms.EnableMsoComponentManager", componentManager);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        double configurationMilliseconds = Stopwatch.GetElapsedTime(configurationStarted).TotalMilliseconds;

        RunCycle(mode, firstUse, forms[0], applications[0], signalReady: true);
        for (int index = 0; index < warmup; index++)
        {
            RunCycle(mode, warmupMeasurement, forms[index + 1], applications[index + 1], signalReady: false);
        }

        checkpoints[0] = Snapshot(process, 0, forms, applications);
        int checkpoint = 1;
        for (int index = 0; index < cycles; index++)
        {
            int referenceIndex = 1 + warmup + index;
            RunCycle(mode, measurements[index], forms[referenceIndex], applications[referenceIndex], signalReady: false);
            if ((index + 1) % CheckpointInterval == 0 || index + 1 == cycles)
            {
                checkpoints[checkpoint++] = Snapshot(process, index + 1, forms, applications);
            }
        }

        PerformanceReport report = new(
            mode,
            componentManager ? "component-manager" : "light",
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            RuntimeInformation.OSDescription,
            SystemInformation.TerminalServerSession,
#if DEBUG
            true,
#else
            false,
#endif
            warmup,
            cycles,
            configurationMilliseconds,
            firstUse,
            measurements,
            initial,
            checkpoints);
        Console.WriteLine(JsonSerializer.Serialize(report, ReportJsonContext.Default.PerformanceReport));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunCycle(
        string mode,
        CycleMeasurement measurement,
        WeakReference formReference,
        WeakReference applicationReference,
        bool signalReady)
    {
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        long started = Stopwatch.GetTimestamp();
        long dispatched = 0;
        long shutdownStarted = 0;
        nint window = 0;
        int disposed = 0;
        int startedEvents = 0;
        int stoppingEvents = 0;
        int stoppedEvents = 0;
        bool hostStoppedAfterUi = false;
        using Form form = new()
        {
            Text = "Lifetime performance probe",
            ClientSize = new Size(160, 80),
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(0, 0)
        };
        form.Disposed += (_, _) => disposed++;
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

                shutdownStarted = Stopwatch.GetTimestamp();
                form.Close();
            });
        };
        WinFormsApplication? application = null;
        CancellationTokenRegistration hostStopped = default;
        try
        {
            if (mode == "legacy")
            {
                Application.Run(form);
            }
            else
            {
                var builder = WinFormsApplication.CreateBuilder().UseStartupForm(form);
                if (mode == "hosted")
                {
                    builder.UseHost(new HostBuilder());
                }

                application = builder.Build();
                application.Lifetime.ApplicationStarted += (_, _) => startedEvents++;
                application.Lifetime.ApplicationStopping += (_, _) => stoppingEvents++;
                application.Lifetime.ApplicationStopped += (_, _) => stoppedEvents++;
                if (application.Host is { } host)
                {
                    var lifetime = host.Services.GetService(typeof(IHostApplicationLifetime)) as IHostApplicationLifetime
                        ?? throw new InvalidOperationException("The host did not expose its lifetime.");
                    hostStopped = lifetime.ApplicationStopped.Register(() => hostStoppedAfterUi = form.IsDisposed);
                }

                application.Run();
                application.Dispose();
            }

            long finished = Stopwatch.GetTimestamp();
            long allocatedAfter = GC.GetTotalAllocatedBytes(precise: true);
            measurement.StartupMilliseconds = Stopwatch.GetElapsedTime(started, dispatched).TotalMilliseconds;
            measurement.ShutdownMilliseconds = Stopwatch.GetElapsedTime(shutdownStarted, finished).TotalMilliseconds;
            measurement.TotalMilliseconds = Stopwatch.GetElapsedTime(started, finished).TotalMilliseconds;
            measurement.AllocatedBytes = allocatedAfter - allocatedBefore;
            if (dispatched == 0 || shutdownStarted == 0 || disposed != 1
                || !form.IsDisposed || form.IsHandleCreated || NativeMethods.IsWindow(window)
                || Application.MessageLoop)
            {
                throw new InvalidOperationException(
                    $"Invalid cycle: dispatched={dispatched}, shutdown={shutdownStarted}, disposed={disposed}, "
                    + $"isDisposed={form.IsDisposed}, handleCreated={form.IsHandleCreated}, "
                    + $"nativeWindow={NativeMethods.IsWindow(window)}, loop={Application.MessageLoop}.");
            }

            if (mode != "legacy" && (startedEvents != 1 || stoppingEvents != 1 || stoppedEvents != 1))
            {
                throw new InvalidOperationException("The builder lifetime did not complete exactly once.");
            }

            if (mode == "hosted" && !hostStoppedAfterUi)
            {
                throw new InvalidOperationException("The host stopped before its UI was disposed.");
            }

            formReference.Target = form;
            applicationReference.Target = application;
        }
        finally
        {
            hostStopped.Dispose();
            application?.Dispose();
        }
    }

    private static ResourceSnapshot Snapshot(
        Process process,
        int completedCycles,
        WeakReference[] forms,
        WeakReference[] applications)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        process.Refresh();
        int threads = process.Threads.Count;
        foreach (ProcessThread thread in process.Threads)
        {
            thread.Dispose();
        }

        ResourceSnapshot snapshot = new(
            completedCycles,
            GC.GetTotalMemory(forceFullCollection: false),
            process.PrivateMemorySize64,
            process.WorkingSet64,
            threads,
            process.HandleCount,
            NativeMethods.ReadGuiResources(process, userObjects: false),
            NativeMethods.ReadGuiResources(process, userObjects: true),
            forms.Count(reference => reference.IsAlive),
            applications.Count(reference => reference.IsAlive));
        return snapshot;
    }
}
