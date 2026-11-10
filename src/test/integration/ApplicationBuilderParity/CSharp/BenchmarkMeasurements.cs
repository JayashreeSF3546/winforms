// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Extensions.WinForms;
using WinFormsLifetimePerformance;

namespace ApplicationBuilderParity;

/// <summary>
///  Measures native-language compositions without replacing their ownership or event implementations.
/// </summary>
public static class BenchmarkMeasurements
{
    public static void Run(string[] args, Action<BenchmarkCycle> run)
    {
        string mode = args[0];
        string workload = args[1];
        int cycles = int.Parse(args[2], CultureInfo.InvariantCulture);
        int warmup = int.Parse(args[3], CultureInfo.InvariantCulture);
        Probe.Require(mode is "legacy" or "shared", "Unknown measurement mode.");
        Probe.Require(workload is "empty" or "splash" or "network", "Unknown measurement workload.");
        ArgumentOutOfRangeException.ThrowIfLessThan(cycles, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(cycles, 10000);
        ArgumentOutOfRangeException.ThrowIfNegative(warmup);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(warmup, 100);
        AppContext.SetSwitch("Switch.System.Windows.Forms.EnableMsoComponentManager", false);
        BenchmarkCycle[] samples = new BenchmarkCycle[1 + warmup + cycles];
        for (int index = 0; index < samples.Length; index++)
        {
            samples[index] = new(mode, workload, index == 0);
        }

        List<object> checkpoints = new(2 + cycles / 25);
        for (int index = 0; index < samples.Length; index++)
        {
            Execute(samples[index], run);
            int measured = index - warmup;
            if (measured == 0 || (measured > 0 && (measured % 25 == 0 || measured == cycles)))
            {
                checkpoints.Add(Snapshot(measured, samples));
            }
        }

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Mode = mode, Workload = workload, Warmup = warmup, Cycles = cycles,
            Runtime = RuntimeInformation.FrameworkDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            OS = RuntimeInformation.OSDescription,
            RemoteSession = SystemInformation.TerminalServerSession,
            FormsModule = typeof(Form).Module.ModuleVersionId,
            VisualBasicModule = typeof(Microsoft.VisualBasic.ApplicationServices.WindowsFormsApplicationBase).Module.ModuleVersionId,
#if DEBUG
            DebugBuild = true,
#else
            DebugBuild = false,
#endif
            FirstUse = samples[0].Result,
            Samples = samples.Skip(1 + warmup).Select(sample => sample.Result),
            Checkpoints = checkpoints
        }));
    }

    public static void RunIpc(string[] args, Action<BenchmarkCycle> run)
    {
        Probe.Require(args[0] is "legacy" or "shared", "Unknown IPC mode.");
        BenchmarkCycle cycle = new(args[0], "empty", signal: true, args[1])
        {
            Tokens = [args[1], args[2], Stopwatch.GetTimestamp().ToString(CultureInfo.InvariantCulture)]
        };
        Execute(cycle, run);
        if (cycle.WasPrimary)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { cycle.Result, Final = Snapshot(1, [cycle]) }));
        }
        else
        {
            Console.WriteLine($"RESULT {cycle.DeliveryResult}");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Execute(BenchmarkCycle cycle, Action<BenchmarkCycle> run)
    {
        long allocated = GC.GetTotalAllocatedBytes(precise: true);
        long started = Stopwatch.GetTimestamp();
        run(cycle);
        long finished = Stopwatch.GetTimestamp();
        cycle.Complete(started, finished, GC.GetTotalAllocatedBytes(precise: true) - allocated);
    }

    internal static object Snapshot(int completed, BenchmarkCycle[] samples)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        using Process process = Process.GetCurrentProcess();
        int threads = process.Threads.Count;
        foreach (ProcessThread thread in process.Threads)
        {
            thread.Dispose();
        }

        return new
        {
            CompletedCycles = completed,
            ManagedHeapBytes = GC.GetTotalMemory(forceFullCollection: false),
            PrivateBytes = process.PrivateMemorySize64,
            WorkingSetBytes = process.WorkingSet64,
            Threads = threads,
            Handles = process.HandleCount,
            GdiObjects = NativeMethods.ReadGuiResources(process, userObjects: false),
            UserObjects = NativeMethods.ReadGuiResources(process, userObjects: true),
            LiveForms = samples.Count(sample => sample.FormReference.IsAlive),
            LiveApplications = samples.Count(sample => sample.ApplicationReference.IsAlive),
            OpenForms = Application.OpenForms.Count
        };
    }
}

/// <summary>
///  Preallocates scalar result storage before warmed resource checkpoints.
/// </summary>
public sealed class BenchmarkResult
{
    public double StartupMilliseconds { get; set; }
    public double ShutdownMilliseconds { get; set; }
    public double TotalMilliseconds { get; set; }
    public long AllocatedBytes { get; set; }
    public int? SubscriptionsAfterRun { get; set; }
    public object? ReadyResources { get; set; }
}

/// <summary>
///  Records dispatch, cleanup and activation boundaries for one measured lifetime.
/// </summary>
public sealed class BenchmarkCycle(string mode, string workload, bool signal, string? ipcIdentity = null)
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private long _dispatched;
    private long _closing;
    private int _disposed;
    private Exception? _failure;
    private Task? _control;
    private Form? _form;
    private nint _window;
    private int _activations;

    public string Mode => mode;
    public bool Splash => workload == "splash";
    public bool Network => workload == "network";
    public bool Ipc => ipcIdentity is not null;
    public string Identity => ipcIdentity!;
    public string[] Tokens { get; set; } = [];
    public bool WasPrimary { get; private set; }
    public string DeliveryResult { get; set; } = "LegacyReturned";
    public WeakReference FormReference { get; } = new(null);
    public WeakReference ApplicationReference { get; } = new(null);
    public BenchmarkResult Result { get; } = new();
    public int? SubscriptionsAfterRun { get; set; }
    public object? ReadyResources { get; private set; }

    public Form CreateForm()
    {
        WasPrimary = true;
        Form form = new() { ShowInTaskbar = false, Text = "VB parity measurement" };
        _form = form;
        FormReference.Target = form;
        form.Disposed += (_, _) => _disposed++;
        form.Shown += (_, _) => form.BeginInvoke(() =>
        {
            try
            {
                RequireOwner();
                _window = form.Handle;
                _dispatched = Stopwatch.GetTimestamp();
                if (Ipc)
                {
                    ReadyResources = BenchmarkMeasurements.Snapshot(0, [this]);
                }

                if (signal)
                {
                    Console.WriteLine("READY");
                }

                if (Ipc)
                {
                    _control = Task.Run(async () =>
                    {
                        while (true)
                        {
                            string? command = await Console.In.ReadLineAsync();
                            if (command == "snapshot")
                            {
                                form.BeginInvoke(() =>
                                {
                                    try
                                    {
                                        Console.WriteLine("CHECKPOINT " +
                                            JsonSerializer.Serialize(BenchmarkMeasurements.Snapshot(_activations, [this])));
                                    }
                                    catch (Exception exception)
                                    {
                                        _failure = exception;
                                        Close(form);
                                    }
                                });
                                continue;
                            }

                            form.BeginInvoke(() =>
                            {
                                if (command != "stop")
                                {
                                    _failure = new InvalidOperationException("Missing owned stop command.");
                                }

                                Close(form);
                            });
                            break;
                        }
                    });
                }
                else
                {
                    Close(form);
                }
            }
            catch (Exception exception)
            {
                _failure = exception;
                Close(form);
            }
        });
        return form;
    }

    private void Close(Form form)
    {
        _closing = Stopwatch.GetTimestamp();
        form.Close();
    }

    public void Activated(IList<string> tokens)
    {
        RequireOwner();
        Probe.Require(tokens.Count == 3 && tokens[0] == Identity, "Foreign benchmark activation.");
        long sent = long.Parse(tokens[2], CultureInfo.InvariantCulture);
        double milliseconds = Stopwatch.GetElapsedTime(sent).TotalMilliseconds;
        _activations++;
        Console.WriteLine("CALLBACK " + JsonSerializer.Serialize(new { Sequence = tokens[1], Milliseconds = milliseconds }));
    }

    private void RequireOwner()
        => Probe.Require(Environment.CurrentManagedThreadId == _owner
            && Thread.CurrentThread.GetApartmentState() == ApartmentState.STA, "Wrong measurement dispatcher.");

    internal void Complete(long started, long finished, long allocated)
    {
        _control?.GetAwaiter().GetResult();
        _control = null;
        if (_failure is not null)
        {
            throw new InvalidOperationException("Measurement callback failed.", _failure);
        }

        if (WasPrimary)
        {
            Probe.Require(_dispatched > 0 && _closing >= _dispatched && _disposed == 1
                && _form!.IsDisposed && !_form.IsHandleCreated && !NativeMethods.IsWindow(_window)
                && !Application.MessageLoop && SubscriptionsAfterRun is null or 0, "Incomplete measured ownership cycle.");
        }

        _form = null;
        Result.StartupMilliseconds = WasPrimary ? Stopwatch.GetElapsedTime(started, _dispatched).TotalMilliseconds : 0;
        Result.ShutdownMilliseconds = WasPrimary ? Stopwatch.GetElapsedTime(_closing, finished).TotalMilliseconds : 0;
        Result.TotalMilliseconds = Stopwatch.GetElapsedTime(started, finished).TotalMilliseconds;
        Result.AllocatedBytes = allocated;
        Result.SubscriptionsAfterRun = SubscriptionsAfterRun;
        Result.ReadyResources = ReadyResources;
    }
}

/// <summary>
///  Counts the shared adapter's actual OS subscriptions without changing any network configuration.
/// </summary>
public sealed class MeasuredNetworkSource : IWinFormsNetworkSource
{
    public bool IsAvailable => NetworkInterface.GetIsNetworkAvailable();
    public int Subscriptions { get; private set; }

    public event NetworkAddressChangedEventHandler Changed
    {
        add
        {
            NetworkChange.NetworkAddressChanged += value;
            Subscriptions++;
        }
        remove
        {
            NetworkChange.NetworkAddressChanged -= value;
            Subscriptions--;
        }
    }
}
