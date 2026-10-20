// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.WinForms;

namespace System.Windows.Forms.Tests;

/// <summary>
///  Serializes the process-wide resource measurements in the leak stress fixture.
/// </summary>
[CollectionDefinition(nameof(WinFormsApplicationLeakTests), DisableParallelization = true)]
public sealed class WinFormsApplicationLeakTestsCollection
{
}

/// <summary>
///  Stress-tests Application Builder activation scope cleanup and retained resources.
/// </summary>
[Collection(nameof(WinFormsApplicationLeakTests))]
public sealed partial class WinFormsApplicationLeakTests
{
    private const int WarmupIterationCount = 20;
    private const int ModelessIterationCount = 1_000;
    private const int ModalIterationCount = 250;
    private const int SampleInterval = 100;
    private const long ManagedMemoryGrowthToleranceBytes = 1_048_576;
    private const int ProcessHandleGrowthTolerance = 16;
    private const uint GuiResourceGrowthTolerance = 4;

    private readonly ITestOutputHelper _output;

    public WinFormsApplicationLeakTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void ScopedActivationStress_ReleasesScopesObjectsAndNativeResources()
    {
        List<string> report = [];
        RunOnStaThread(() => RunStressScenario(report));

        foreach (string line in report)
        {
            _output.WriteLine(line);
        }
    }

    private static void RunStressScenario(List<string> report)
    {
        LeakDiagnostics baselineDiagnostics = new();
        LeakDiagnostics scopedDiagnostics = new();
        List<WeakReference<object>> references = [];
        TrackingHost host = new(scopedDiagnostics);

        using Form startupForm = new();
        StressMeasurement? baseline = null;
        StressMeasurement? modeless = null;
        StressMeasurement? modal = null;

        using WinFormsApplication application = WinFormsApplication.CreateBuilder()
            .UseStartupForm(services =>
            {
                _ = services.GetRequiredService<ScopedActivationProbe>();

                return startupForm;
            })
            .UseHost(host)
            .Build();

        startupForm.Shown += (_, _) =>
        {
            baseline = Measure(
                "baseline Forms",
                WarmupIterationCount,
                ModelessIterationCount,
                references,
                weakReferences => CreateBaselineForm(baselineDiagnostics, weakReferences));

            modeless = Measure(
                "scoped modeless Forms",
                WarmupIterationCount,
                ModelessIterationCount,
                references,
                weakReferences => CreateModelessForm(
                    application,
                    scopedDiagnostics,
                    weakReferences));

            modal = Measure(
                "scoped modal Forms",
                WarmupIterationCount,
                ModalIterationCount,
                references,
                weakReferences => CreateModalForm(
                    application,
                    startupForm,
                    scopedDiagnostics,
                    weakReferences));

            CollectGarbage();
            Assert.Equal(0, CountRetainedObjects(references));
            AssertNoNativeResourceGrowth(baseline);
            AssertNoNativeResourceGrowth(modeless);
            AssertNoNativeResourceGrowth(modal);

            startupForm.Close();
        };

        application.Run();

        Assert.NotNull(baseline);
        Assert.NotNull(modeless);
        Assert.NotNull(modal);

        int scopedFormCount =
            WarmupIterationCount * 2 + ModelessIterationCount + ModalIterationCount + 1;
        int activatedScopedFormCount =
            WarmupIterationCount * 2 + ModelessIterationCount + ModalIterationCount;
        int totalFormCount =
            WarmupIterationCount * 3 + ModelessIterationCount * 2 + ModalIterationCount;

        Assert.Equal(scopedFormCount, scopedDiagnostics.ProbesCreated);
        Assert.Equal(scopedFormCount, scopedDiagnostics.ProbesDisposed);
        Assert.Equal(scopedFormCount, scopedDiagnostics.ScopesCreated);
        Assert.Equal(scopedFormCount, scopedDiagnostics.ScopesDisposed);
        Assert.Equal(totalFormCount, baselineDiagnostics.FormsDisposed + scopedDiagnostics.FormsDisposed);
        Assert.Equal(totalFormCount, baselineDiagnostics.ControlsDisposed + scopedDiagnostics.ControlsDisposed);
        Assert.Equal(totalFormCount, baselineDiagnostics.ComponentsDisposed + scopedDiagnostics.ComponentsDisposed);
        Assert.Equal(
            activatedScopedFormCount * 2,
            scopedDiagnostics.ControlsDisposedBeforeProbeDisposal
                + scopedDiagnostics.ComponentsDisposedBeforeProbeDisposal);
        Assert.Equal(0, CountRetainedObjects(references));
        report.Add(
            $"Weak-reference sentinels checked: {references.Count}; retained while the application was running: 0.");

        WriteMeasurement(baseline, report);
        WriteMeasurement(modeless, report);
        WriteMeasurement(modal, report);
    }

    private static StressMeasurement Measure(
        string name,
        int warmupIterationCount,
        int iterationCount,
        List<WeakReference<object>> references,
        Action<List<WeakReference<object>>?> iteration)
    {
        for (int index = 0; index < warmupIterationCount; index++)
        {
            iteration(null);
        }

        CollectGarbage();
        List<ResourceSnapshot> samples = [CaptureResources()];
        Stopwatch stopwatch = new();
        stopwatch.Start();

        for (int index = 1; index <= iterationCount; index++)
        {
            bool captureWeakReferences = index % SampleInterval == 0 || index == iterationCount;
            iteration(captureWeakReferences ? references : null);

            if (index % SampleInterval == 0 || index == iterationCount)
            {
                stopwatch.Stop();
                samples.Add(CaptureResources());

                if (index < iterationCount)
                {
                    stopwatch.Start();
                }
            }
        }

        stopwatch.Stop();

        return new StressMeasurement(
            name,
            iterationCount,
            stopwatch.Elapsed,
            [.. samples]);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CreateBaselineForm(
        LeakDiagnostics diagnostics,
        List<WeakReference<object>>? references)
    {
        TrackingForm form = new(diagnostics, probe: null);

        ShowCloseAndDispose(form, references);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CreateModelessForm(
        WinFormsApplication application,
        LeakDiagnostics diagnostics,
        List<WeakReference<object>>? references)
    {
        TrackingForm form = application.CreateForm(
            services => new TrackingForm(
                diagnostics,
                services.GetRequiredService<ScopedActivationProbe>()));

        ShowCloseAndDispose(form, references);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CreateModalForm(
        WinFormsApplication application,
        Form owner,
        LeakDiagnostics diagnostics,
        List<WeakReference<object>>? references)
    {
        TrackingForm? form = null;

        application.ShowDialog(
            services => form = new TrackingForm(
                diagnostics,
                services.GetRequiredService<ScopedActivationProbe>(),
                closeOnShown: true),
            owner);

        TrackingForm modalForm = Assert.IsType<TrackingForm>(form);
        modalForm.Dispose();
        AssertDisposed(modalForm);
        AddWeakReferences(modalForm, references);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ShowCloseAndDispose(
        TrackingForm form,
        List<WeakReference<object>>? references)
    {
        form.Show();
        form.Close();
        form.Dispose();

        AssertDisposed(form);
        AddWeakReferences(form, references);
    }

    private static void AssertDisposed(TrackingForm form)
    {
        Assert.True(form.IsDisposed);
        Assert.True(form.ContainerPanel.IsDisposed);
        Assert.True(form.ChildControl.IsDisposed);
        Assert.True(form.TrackingComponent.IsDisposed);

        if (form.Probe is not null)
        {
            Assert.True(form.Probe.IsDisposed);
        }
    }

    private static void AddWeakReferences(
        TrackingForm form,
        List<WeakReference<object>>? references)
    {
        if (references is null)
        {
            return;
        }

        references.Add(new(form));
        references.Add(new(form.ContainerPanel));
        references.Add(new(form.ChildControl));
        references.Add(new(form.TrackingComponent));

        if (form.Probe is not null)
        {
            references.Add(new(form.Probe));
        }
    }

    private static void AssertNoNativeResourceGrowth(StressMeasurement measurement)
    {
        ResourceSnapshot baseline = measurement.Samples[0];
        int maximumProcessHandles = measurement.Samples.Max(sample => sample.ProcessHandleCount);
        uint maximumGdiObjects = measurement.Samples.Max(sample => sample.GdiObjectCount);
        uint maximumUserObjects = measurement.Samples.Max(sample => sample.UserObjectCount);
        long maximumManagedBytes = measurement.Samples.Max(sample => sample.ManagedBytes);

        Assert.True(
            maximumManagedBytes <= baseline.ManagedBytes + ManagedMemoryGrowthToleranceBytes,
            $"{measurement.Name} retained managed memory grew from {baseline.ManagedBytes} to {maximumManagedBytes} bytes.");
        Assert.True(
            maximumProcessHandles <= baseline.ProcessHandleCount + ProcessHandleGrowthTolerance,
            $"{measurement.Name} process handles grew from {baseline.ProcessHandleCount} to {maximumProcessHandles}.");
        Assert.True(
            maximumGdiObjects <= baseline.GdiObjectCount + GuiResourceGrowthTolerance,
            $"{measurement.Name} GDI objects grew from {baseline.GdiObjectCount} to {maximumGdiObjects}.");
        Assert.True(
            maximumUserObjects <= baseline.UserObjectCount + GuiResourceGrowthTolerance,
            $"{measurement.Name} USER objects grew from {baseline.UserObjectCount} to {maximumUserObjects}.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ResourceSnapshot CaptureResources()
    {
        CollectGarbage();

        using Process process = Process.GetCurrentProcess();

        return new ResourceSnapshot(
            GC.GetTotalMemory(forceFullCollection: false),
            process.HandleCount,
            GetGuiResourceCount(process.Handle, GuiResourceFlags.GdiObjects),
            GetGuiResourceCount(process.Handle, GuiResourceFlags.UserObjects));
    }

    private static uint GetGuiResourceCount(nint processHandle, GuiResourceFlags flags)
    {
        uint count = GetGuiResources(processHandle, flags);

        if (count == 0)
        {
            int lastWin32Error = Marshal.GetLastWin32Error();

            if (lastWin32Error != 0)
            {
                throw new Win32Exception(lastWin32Error, "Failed to retrieve GUI resource counts.");
            }
        }

        return count;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint GetGuiResources(nint processHandle, GuiResourceFlags flags);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CollectGarbage()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int CountRetainedObjects(List<WeakReference<object>> references)
    {
        int retainedCount = 0;

        foreach (WeakReference<object> reference in references)
        {
            if (reference.TryGetTarget(out _))
            {
                retainedCount++;
            }
        }

        return retainedCount;
    }

    private static void WriteMeasurement(StressMeasurement measurement, List<string> report)
    {
        ResourceSnapshot start = measurement.Samples[0];
        ResourceSnapshot end = measurement.Samples[^1];
        double operationsPerSecond = measurement.IterationCount / measurement.Elapsed.TotalSeconds;

        report.Add(
            $"{measurement.Name}: {measurement.IterationCount} iterations, "
                + $"{operationsPerSecond:F1} iterations/s; "
                + $"managed bytes {start.ManagedBytes}->{end.ManagedBytes}; "
                + $"process handles {start.ProcessHandleCount}->{end.ProcessHandleCount}; "
                + $"GDI {start.GdiObjectCount}->{end.GdiObjectCount}; "
                + $"USER {start.UserObjectCount}->{end.UserObjectCount}.");

        for (int index = 0; index < measurement.Samples.Length; index++)
        {
            ResourceSnapshot sample = measurement.Samples[index];

            report.Add(
                $"  sample {index}: managed={sample.ManagedBytes} B, "
                    + $"process handles={sample.ProcessHandleCount}, "
                    + $"GDI={sample.GdiObjectCount}, USER={sample.UserObjectCount}.");
        }
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
    ///  Counts disposal and scope-lifetime ordering during a stress run.
    /// </summary>
    private sealed class LeakDiagnostics
    {
        private int _formsDisposed;
        private int _controlsDisposed;
        private int _componentsDisposed;
        private int _scopesCreated;
        private int _scopesDisposed;
        private int _probesCreated;
        private int _probesDisposed;
        private int _controlsDisposedBeforeProbeDisposal;
        private int _componentsDisposedBeforeProbeDisposal;

        internal int FormsDisposed => Volatile.Read(ref _formsDisposed);

        internal int ControlsDisposed => Volatile.Read(ref _controlsDisposed);

        internal int ComponentsDisposed => Volatile.Read(ref _componentsDisposed);

        internal int ScopesCreated => Volatile.Read(ref _scopesCreated);

        internal int ScopesDisposed => Volatile.Read(ref _scopesDisposed);

        internal int ProbesCreated => Volatile.Read(ref _probesCreated);

        internal int ProbesDisposed => Volatile.Read(ref _probesDisposed);

        internal int ControlsDisposedBeforeProbeDisposal
            => Volatile.Read(ref _controlsDisposedBeforeProbeDisposal);

        internal int ComponentsDisposedBeforeProbeDisposal
            => Volatile.Read(ref _componentsDisposedBeforeProbeDisposal);

        internal void OnFormDisposed() => Interlocked.Increment(ref _formsDisposed);

        internal void OnControlDisposed(bool probeAlive)
        {
            Interlocked.Increment(ref _controlsDisposed);

            if (probeAlive)
            {
                Interlocked.Increment(ref _controlsDisposedBeforeProbeDisposal);
            }
        }

        internal void OnComponentDisposed(bool probeAlive)
        {
            Interlocked.Increment(ref _componentsDisposed);

            if (probeAlive)
            {
                Interlocked.Increment(ref _componentsDisposedBeforeProbeDisposal);
            }
        }

        internal void OnProbeCreated() => Interlocked.Increment(ref _probesCreated);

        internal void OnProbeDisposed() => Interlocked.Increment(ref _probesDisposed);

        internal void OnScopeCreated() => Interlocked.Increment(ref _scopesCreated);

        internal void OnScopeDisposed() => Interlocked.Increment(ref _scopesDisposed);
    }

    /// <summary>
    ///  A scoped marker used to verify service creation and disposal.
    /// </summary>
    private sealed class ScopedActivationProbe : IDisposable
    {
        private readonly LeakDiagnostics _diagnostics;
        private int _disposed;

        internal ScopedActivationProbe(LeakDiagnostics diagnostics)
        {
            _diagnostics = diagnostics;
            _diagnostics.OnProbeCreated();
        }

        internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _diagnostics.OnProbeDisposed();
            }
        }
    }

    /// <summary>
    ///  A form with a nested control tree and container-owned component.
    /// </summary>
    private sealed class TrackingForm : Form
    {
        private readonly LeakDiagnostics _diagnostics;
        private readonly Container _components = new();
        private readonly bool _closeOnShown;
        private int _disposed;

        internal TrackingForm(
            LeakDiagnostics diagnostics,
            ScopedActivationProbe? probe,
            bool closeOnShown = false)
        {
            _diagnostics = diagnostics;
            Probe = probe;
            _closeOnShown = closeOnShown;
            ContainerPanel = new Panel();
            ChildControl = new TrackingControl(diagnostics, probe);
            ContainerPanel.Controls.Add(ChildControl);
            Controls.Add(ContainerPanel);
            TrackingComponent = new(diagnostics, probe);
            _components.Add(TrackingComponent);
        }

        internal Panel ContainerPanel { get; }

        internal TrackingControl ChildControl { get; }

        internal TrackingComponent TrackingComponent { get; }

        internal ScopedActivationProbe? Probe { get; }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            if (_closeOnShown)
            {
                Close();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _components.Dispose();
                _diagnostics.OnFormDisposed();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    ///  A nested control that records disposal before its activation scope.
    /// </summary>
    private sealed class TrackingControl : Control
    {
        private readonly LeakDiagnostics _diagnostics;
        private readonly ScopedActivationProbe? _probe;
        private int _disposed;

        internal TrackingControl(LeakDiagnostics diagnostics, ScopedActivationProbe? probe)
        {
            _diagnostics = diagnostics;
            _probe = probe;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _diagnostics.OnControlDisposed(_probe is { IsDisposed: false });
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    ///  A component owned by the form's component container.
    /// </summary>
    private sealed class TrackingComponent : Component
    {
        private readonly LeakDiagnostics _diagnostics;
        private readonly ScopedActivationProbe? _probe;
        private int _disposed;

        internal TrackingComponent(LeakDiagnostics diagnostics, ScopedActivationProbe? probe)
        {
            _diagnostics = diagnostics;
            _probe = probe;
        }

        internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        protected override void Dispose(bool disposing)
        {
            if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _diagnostics.OnComponentDisposed(_probe is { IsDisposed: false });
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    ///  A minimal host that exposes a tracking scope factory and lifetime.
    /// </summary>
    private sealed class TrackingHost : IHost
    {
        private readonly TrackingHostLifetime _lifetime;
        private readonly IServiceProvider _services;

        internal TrackingHost(LeakDiagnostics diagnostics)
        {
            _lifetime = new();
            TrackingServiceScopeFactory scopeFactory = new(_lifetime, diagnostics);
            _services = new TrackingServiceProvider(_lifetime, scopeFactory, probe: null);
        }

        public IServiceProvider Services => _services;

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _lifetime.NotifyStarted();

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _lifetime.NotifyStopping();
            _lifetime.NotifyStopped();

            return Task.CompletedTask;
        }

        public void Dispose() => _lifetime.Dispose();

        public ValueTask DisposeAsync()
        {
            Dispose();

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    ///  Supplies one tracking scope and scoped probe per form activation.
    /// </summary>
    private sealed class TrackingServiceScopeFactory : IServiceScopeFactory
    {
        private readonly TrackingHostLifetime _lifetime;
        private readonly LeakDiagnostics _diagnostics;

        internal TrackingServiceScopeFactory(
            TrackingHostLifetime lifetime,
            LeakDiagnostics diagnostics)
        {
            _lifetime = lifetime;
            _diagnostics = diagnostics;
        }

        public IServiceScope CreateScope()
        {
            _diagnostics.OnScopeCreated();
            ScopedActivationProbe probe = new(_diagnostics);
            IServiceProvider services = new TrackingServiceProvider(_lifetime, this, probe);

            return new TrackingServiceScope(services, probe, _diagnostics);
        }
    }

    /// <summary>
    ///  Disposes the scoped probe exactly once with its activation scope.
    /// </summary>
    private sealed class TrackingServiceScope : IServiceScope
    {
        private readonly ScopedActivationProbe _probe;
        private readonly LeakDiagnostics _diagnostics;
        private int _disposed;

        internal TrackingServiceScope(
            IServiceProvider services,
            ScopedActivationProbe probe,
            LeakDiagnostics diagnostics)
        {
            ServiceProvider = services;
            _probe = probe;
            _diagnostics = diagnostics;
        }

        public IServiceProvider ServiceProvider { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _probe.Dispose();
                _diagnostics.OnScopeDisposed();
            }
        }
    }

    /// <summary>
    ///  Provides host services and an optional activation-scoped probe.
    /// </summary>
    private sealed class TrackingServiceProvider(
        TrackingHostLifetime lifetime,
        IServiceScopeFactory scopeFactory,
        ScopedActivationProbe? probe) : IServiceProvider
    {
        public object? GetService(Type serviceType)
            => serviceType switch
            {
                _ when serviceType == typeof(IHostApplicationLifetime) => lifetime,
                _ when serviceType == typeof(IServiceScopeFactory) => scopeFactory,
                _ when serviceType == typeof(ScopedActivationProbe) => probe,
                _ => null
            };
    }

    /// <summary>
    ///  Provides the cancellation tokens required by Generic Host.
    /// </summary>
    private sealed class TrackingHostLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource _started = new();
        private readonly CancellationTokenSource _stopping = new();
        private readonly CancellationTokenSource _stopped = new();

        public CancellationToken ApplicationStarted => _started.Token;

        public CancellationToken ApplicationStopping => _stopping.Token;

        public CancellationToken ApplicationStopped => _stopped.Token;

        public void StopApplication() => NotifyStopping();

        public void Dispose()
        {
            _started.Dispose();
            _stopping.Dispose();
            _stopped.Dispose();
        }

        internal void NotifyStarted() => _started.Cancel();

        internal void NotifyStopping() => _stopping.Cancel();

        internal void NotifyStopped() => _stopped.Cancel();
    }

    /// <summary>
    ///  Resource counts captured after forced collection.
    /// </summary>
    private readonly record struct ResourceSnapshot(
        long ManagedBytes,
        int ProcessHandleCount,
        uint GdiObjectCount,
        uint UserObjectCount);

    /// <summary>
    ///  Selects the GUI object count returned by GetGuiResources.
    /// </summary>
    private enum GuiResourceFlags : uint
    {
        GdiObjects,
        UserObjects
    }

    /// <summary>
    ///  Measurements for one repeated activation scenario.
    /// </summary>
    private sealed record StressMeasurement(
        string Name,
        int IterationCount,
        TimeSpan Elapsed,
        ResourceSnapshot[] Samples);
}
