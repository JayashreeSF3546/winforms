// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.WinForms;
using Microsoft.Win32.SafeHandles;

namespace ApplicationBuilderScopes.CSharp;

/// <summary>
///  Compares explicit manual ownership with scoped activation under one live hosted dispatcher.
/// </summary>
internal static partial class ScopeStressProbe
{
    internal static void Run(string[] args)
    {
        string mode = "scoped";
        int cycles = 1000;
        int warmup = 10;
        int interval = 100;
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
                case "--interval" when index + 1 < args.Length:
                    interval = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    break;
                case "--component-manager":
                    componentManager = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown or incomplete stress option: {args[index]}");
            }
        }

        if (mode is not ("manual" or "scoped"))
        {
            throw new ArgumentException("Expected --mode manual or scoped.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(cycles, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(warmup);
        ArgumentOutOfRangeException.ThrowIfLessThan(interval, 1);
        AppContext.SetSwitch("Switch.System.Windows.Forms.EnableMsoComponentManager", componentManager);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        StressState state = new(mode, cycles, warmup, interval);
        WeakReference application = new(null);
        WeakReference context = new(null);
        WeakReference coordinator = new(null);
        RunApplication(state, application, context, coordinator);
        Collect();
        state.VerifyCleanup();
        if (Application.MessageLoop || Application.OpenForms.Count != 0)
        {
            throw new InvalidOperationException("The stress application's UI did not finish.");
        }

        StressReport report = new(
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
            state.Created,
            state.Disposed,
            state.ComponentDisposals,
            state.ControlDisposals,
            state.Subscriptions,
            application.IsAlive,
            context.IsAlive,
            coordinator.IsAlive,
            state.Checkpoints);
        Console.WriteLine(JsonSerializer.Serialize(report, StressJsonContext.Default.StressReport));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunApplication(
        StressState state, WeakReference applicationReference, WeakReference contextReference, WeakReference coordinatorReference)
    {
        StressContext? context = null;
        using WinFormsApplication application = WinFormsApplication.CreateBuilder()
            .UseHost(new HostBuilder().ConfigureServices((_, services) =>
                services.AddScoped(provider => state.CreateService(provider))))
            .UseScopedApplicationContext((services, scopes) =>
            {
                coordinatorReference.Target = scopes;
                context = new StressContext(state, services, scopes);
                contextReference.Target = context;
                return context;
            })
            .Build();
        applicationReference.Target = application;
        application.Run();
        if (context?.Failure is { } failure)
        {
            ExceptionDispatchInfo.Throw(failure);
        }
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint GetGuiResources(SafeProcessHandle process, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(nint window);

    private static uint ReadGuiResources(Process process, uint flags)
    {
        Marshal.SetLastPInvokeError(0);
        uint count = GetGuiResources(process.SafeHandle, flags);
        int error = Marshal.GetLastPInvokeError();
        return count == 0 && error != 0 ? throw new Win32Exception(error) : count;
    }

    /// <summary>
    ///  Runs posted batches without retaining completed per-cycle graphs in dispatcher delegates.
    /// </summary>
    private sealed class StressContext : ApplicationContext
    {
        private readonly StressState _state;
        private readonly IServiceProvider _services;
        private readonly WinFormsUiScope _scopes;
        private int _completed;
        private long _batchStarted;
        private long _batchAllocated;

        internal StressContext(StressState state, IServiceProvider services, WinFormsUiScope scopes)
        {
            _state = state;
            _services = services;
            _scopes = scopes;
            MainForm = scopes.CreateForm(_ => new Form
            {
                Text = "Scope stress dispatcher",
                ShowInTaskbar = false,
                ClientSize = new Size(160, 80)
            }).Form;
            MainForm.Shown += (_, _) => MainForm.BeginInvoke(Advance);
        }

        internal Exception? Failure { get; private set; }

        private void Advance()
        {
            try
            {
                if (_completed == 0)
                {
                    for (int index = 0; index < _state.Warmup; index++)
                    {
                        RunCycle(index);
                    }

                    Checkpoint(0);
                    Console.WriteLine("READY");
                    Console.Out.Flush();
                    _batchStarted = Stopwatch.GetTimestamp();
                    _batchAllocated = GC.GetTotalAllocatedBytes(precise: true);
                }

                int count = Math.Min(_state.Interval, _state.Cycles - _completed);
                for (int index = 0; index < count; index++)
                {
                    RunCycle(_completed + index);
                }

                _completed += count;
                Checkpoint(_completed);
                if (_completed == _state.Cycles)
                {
                    MainForm!.Close();
                }
                else
                {
                    MainForm!.BeginInvoke(Advance);
                }
            }
            catch (Exception exception)
            {
                Failure = exception;
                ExitThread();
            }
        }

        private void Checkpoint(int completed)
        {
            double milliseconds = completed == 0 ? 0 : Stopwatch.GetElapsedTime(_batchStarted).TotalMilliseconds;
            long allocated = completed == 0 ? 0 : GC.GetTotalAllocatedBytes(precise: true) - _batchAllocated;
            _state.Snapshot(completed, milliseconds, allocated);
            _batchStarted = Stopwatch.GetTimestamp();
            _batchAllocated = GC.GetTotalAllocatedBytes(precise: true);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void RunCycle(int index)
        {
            if (_state.Mode == "manual")
            {
                using IServiceScope parentScope = _services.CreateScope();
                using StressForm parent = CreateForm(parentScope.ServiceProvider);
                parent.Show();
                using (IServiceScope childScope = _services.CreateScope())
                {
                    using StressForm child = CreateForm(childScope.ServiceProvider);
                    child.Show(parent);
                    _state.Pulse();
                    using (IServiceScope dialogScope = _services.CreateScope())
                    {
                        using StressForm dialog = CreateForm(dialogScope.ServiceProvider);
                        dialog.Shown += CloseModal;
                        if (dialog.ShowDialog(child) != DialogResult.OK)
                        {
                            throw new InvalidOperationException("The manual dialog did not complete.");
                        }

                        dialog.Dispose();
                        dialog.VerifyDisposed();
                    }

                    child.Close();
                    child.VerifyDisposed();
                }

                parent.Close();
                parent.VerifyDisposed();
            }
            else
            {
                using WinFormsFormScope parent = _scopes.CreateForm(CreateForm);
                _state.Track(parent, TrackedKind.Lifetime);
                parent.Show();
                using WinFormsFormScope child = _scopes.CreateForm(CreateForm, parent);
                _state.Track(child, TrackedKind.Lifetime);
                child.Show();
                _state.Pulse();
                using (WinFormsFormScope dialog = _scopes.CreateForm(CreateForm, child))
                {
                    _state.Track(dialog, TrackedKind.Lifetime);
                    dialog.Form.Shown += CloseModal;
                    if (dialog.ShowDialog() != DialogResult.OK)
                    {
                        throw new InvalidOperationException("The scoped dialog did not complete.");
                    }

                    ((StressForm)dialog.Form).VerifyDisposed();
                }

                if (index % 2 == 0)
                {
                    child.Form.Close();
                }

                parent.Form.Close();
                ((StressForm)child.Form).VerifyDisposed();
                ((StressForm)parent.Form).VerifyDisposed();
            }

            _state.VerifyCleanup();
        }

        private StressForm CreateForm(IServiceProvider services)
        {
            var service = services.GetRequiredService<StressService>();
            StressForm form = new(_state, service);
            _state.Track(form, TrackedKind.Form);
            return form;
        }

        private static void CloseModal(object? sender, EventArgs e)
        {
            var form = (Form)sender!;
            form.BeginInvoke(() => form.DialogResult = DialogResult.OK);
        }
    }

    /// <summary>
    ///  Holds fixed-capacity weak tracking and a long-lived publisher, never the tracked UI graphs.
    /// </summary>
    private sealed class StressState
    {
        private readonly WeakReference[] _references;
        private readonly TrackedKind[] _kinds;
        private int _used;
        private EventHandler? _pulse;

        internal StressState(string mode, int cycles, int warmup, int interval)
        {
            Mode = mode;
            Cycles = cycles;
            Warmup = warmup;
            Interval = interval;
            int capacity = checked((cycles + warmup) * 3 * 9);
            _references = new WeakReference[capacity];
            _kinds = new TrackedKind[capacity];
            for (int index = 0; index < capacity; index++)
            {
                _references[index] = new WeakReference(null);
            }

            Checkpoints = new StressSnapshot[1 + ((cycles - 1) / interval) + 1];
        }

        internal string Mode { get; }
        internal int Cycles { get; }
        internal int Warmup { get; }
        internal int Interval { get; }
        internal StressSnapshot[] Checkpoints { get; }
        internal int Created { get; private set; }
        internal int Disposed { get; set; }
        internal int ComponentDisposals { get; set; }
        internal int ControlDisposals { get; set; }
        internal int FormDisposals { get; set; }
        internal int Subscriptions { get; private set; }

        internal StressService CreateService(IServiceProvider provider)
        {
            StressService service = new(this);
            Created++;
            Track(service, TrackedKind.Service);
            Track(provider, TrackedKind.Provider);
            return service;
        }

        internal void Track(object target, TrackedKind kind)
        {
            _references[_used].Target = target;
            _kinds[_used++] = kind;
        }

        internal void Subscribe(EventHandler handler)
        {
            _pulse += handler;
            Subscriptions++;
        }

        internal void Unsubscribe(EventHandler handler)
        {
            _pulse -= handler;
            Subscriptions--;
        }

        internal void Pulse() => _pulse?.Invoke(this, EventArgs.Empty);

        internal void VerifyCleanup()
        {
            Pulse();
            if (Created != Disposed || Created != ComponentDisposals || Created * 2 != ControlDisposals
                || Created != FormDisposals || Subscriptions != 0)
            {
                throw new InvalidOperationException(
                    $"Incomplete cleanup: services={Created}/{Disposed}, components={ComponentDisposals}, "
                    + $"controls={ControlDisposals}, forms={FormDisposals}, subscriptions={Subscriptions}.");
            }
        }

        internal void Snapshot(int completed, double milliseconds, long allocated)
        {
            Collect();
            int[] live = new int[7];
            for (int index = 0; index < _used; index++)
            {
                if (_references[index].IsAlive)
                {
                    live[(int)_kinds[index]]++;
                }
            }

            using Process process = Process.GetCurrentProcess();
            StressSnapshot snapshot = new(
                completed, milliseconds, allocated, GC.GetTotalMemory(forceFullCollection: false),
                process.PrivateMemorySize64, process.HandleCount, ReadGuiResources(process, 0), ReadGuiResources(process, 1),
                live[0], live[1], live[2], live[3], live[4], live[5], live[6], Created, Disposed, Subscriptions);
            Checkpoints[(completed + Interval - 1) / Interval] = snapshot;
            if (live.Any(count => count != 0))
            {
                throw new InvalidOperationException($"Retained cycle objects at checkpoint: {snapshot}");
            }
        }
    }

    /// <summary>
    ///  Labels weak references without recording object identities in a growing strong registry.
    /// </summary>
    private enum TrackedKind
    {
        Form,
        Control,
        Component,
        Service,
        Provider,
        Lifetime,
        GraphicsResource
    }

    /// <summary>
    ///  Checks service availability during UI cleanup and counts exactly-once synchronous disposal.
    /// </summary>
    private sealed class StressService(StressState state) : IDisposable
    {
        private bool _disposed;

        internal void VerifyAlive() => ObjectDisposedException.ThrowIf(_disposed, this);

        public void Dispose()
        {
            VerifyAlive();
            _disposed = true;
            state.Disposed++;
        }
    }

    /// <summary>
    ///  Owns a nested control graph, a subscribed component, and explicitly released graphics resources.
    /// </summary>
    private sealed class StressForm : Form
    {
        private readonly StressState _state;
        private readonly StressService _service;
        private readonly IContainer _components = new Container();
        private readonly Bitmap _image = new(8, 8);
        private readonly Font _font = new(FontFamily.GenericSansSerif, 9);
        private nint _window;

        internal StressForm(StressState state, StressService service)
        {
            _state = state;
            _service = service;
            state.Track(_image, TrackedKind.GraphicsResource);
            state.Track(_font, TrackedKind.GraphicsResource);
            ShowInTaskbar = false;
            ClientSize = new Size(180, 90);
            UserControl outer = new() { Dock = DockStyle.Fill };
            UserControl inner = new() { Dock = DockStyle.Fill };
            inner.Controls.Add(new PictureBox { Image = _image, Size = new Size(8, 8) });
            inner.Controls.Add(new Label { Text = "Scoped UI probe", Font = _font, AutoSize = true, Top = 20 });
            outer.Controls.Add(inner);
            Controls.Add(outer);
            state.Track(outer, TrackedKind.Control);
            state.Track(inner, TrackedKind.Control);
            outer.Disposed += ControlDisposed;
            inner.Disposed += ControlDisposed;
            StressComponent component = new(state, service);
            _components.Add(component);
            state.Track(component, TrackedKind.Component);
            Shown += (_, _) => _window = Handle;
            Disposed += (_, _) =>
            {
                service.VerifyAlive();
                state.FormDisposals++;
            };
        }

        private void ControlDisposed(object? sender, EventArgs e)
        {
            _service.VerifyAlive();
            _state.ControlDisposals++;
        }

        internal void VerifyDisposed()
        {
            if (!IsDisposed || IsHandleCreated || _window == 0 || IsWindow(_window))
            {
                throw new InvalidOperationException("A stress window was not shown and destroyed.");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && IsDisposed)
            {
                return;
            }

            if (disposing)
            {
                _components.Dispose();
                _image.Dispose();
                _font.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    ///  Exercises explicit unsubscription from a publisher that outlives every stress window.
    /// </summary>
    private sealed class StressComponent : Component
    {
        private readonly StressState _state;
        private readonly StressService _service;
        private bool _disposed;

        internal StressComponent(StressState state, StressService service)
        {
            _state = state;
            _service = service;
            state.Subscribe(OnPulse);
        }

        private void OnPulse(object? sender, EventArgs e) => _service.VerifyAlive();

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _service.VerifyAlive();
                _state.Unsubscribe(OnPulse);
                _state.ComponentDisposals++;
                _disposed = true;
            }

            base.Dispose(disposing);
        }
    }
}

/// <summary>
///  Records workload identity, strict cleanup counts, and non-budgeted process-resource diagnostics.
/// </summary>
internal sealed record StressReport(
    string Mode, string Backend, string Runtime, string Architecture, string OperatingSystem,
    bool TerminalServices, bool DebugBuild, int WarmupCycles, int MeasuredCycles,
    int ServicesCreated, int ServicesDisposed, int ComponentsDisposed, int ControlsDisposed, int ActiveSubscriptions,
    bool ApplicationRetained, bool ContextRetained, bool CoordinatorRetained, StressSnapshot[] Checkpoints);

/// <summary>
///  Reports a post-collection checkpoint while the host, coordinator, and dispatcher remain alive.
/// </summary>
internal sealed record StressSnapshot(
    int CompletedCycles, double BatchMilliseconds, long AllocatedBytes, long ManagedHeapBytes, long PrivateBytes,
    int Handles, uint GdiObjects, uint UserObjects, int LiveForms, int LiveControls, int LiveComponents,
    int LiveServices, int LiveProviders, int LiveLifetimes, int LiveGraphicsResources,
    int ServicesCreated, int ServicesDisposed, int ActiveSubscriptions);

/// <summary>
///  Serializes reports without reflection-based discovery in measured processes.
/// </summary>
[JsonSerializable(typeof(StressReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class StressJsonContext : JsonSerializerContext
{
}
