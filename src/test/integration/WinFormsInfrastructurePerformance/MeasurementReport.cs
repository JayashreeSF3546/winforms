// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Serialization;

namespace WinFormsInfrastructurePerformance;

/// <summary>
///  Reports equivalent workloads and raw measurements without machine-specific acceptance thresholds.
/// </summary>
internal sealed record MeasurementReport(
    string Mode,
    string Workload,
    bool Watchers,
    string Backend,
    string Runtime,
    string Architecture,
    string OperatingSystem,
    bool TerminalServices,
    bool DebugBuild,
    int WarmupCycles,
    CycleResult FirstUse,
    CycleResult[] Cycles,
    ResourceCheckpoint[] Checkpoints);

/// <summary>
///  Aligns elapsed time and process-wide allocation boundaries for a single operation.
/// </summary>
internal readonly record struct OperationMeasurement(double Milliseconds, long AllocatedBytes, int Operations = 1);

/// <summary>
///  Contains a complete application lifetime and its nested operation measurements.
/// </summary>
internal sealed class CycleResult
{
    public OperationMeasurement ConfigureBuild { get; set; } = new(0, 0);
    public OperationMeasurement Lifetime { get; set; } = new(0, 0);
    public double StartupToDispatchMilliseconds { get; set; }
    public double ShutdownMilliseconds { get; set; }
    public Dictionary<string, OperationMeasurement> Operations { get; } = new(12);
    public int ReleasedFiles { get; set; }
    public int ExpectedFailures { get; set; }
    public int DisposedServices { get; set; }
    public bool WorkerStopped { get; set; }
    public bool FlushAfterUi { get; set; }
    public int FlushedRecords { get; set; }
    public int NotificationCount { get; set; }
    public int HealthScopeDisposals { get; set; }
}

/// <summary>
///  Records collected resource trends; kernel handle totals are not file-handle counts.
/// </summary>
internal sealed record ResourceCheckpoint(
    int CompletedCycles,
    long ManagedBytes,
    long PrivateBytes,
    long WorkingSetBytes,
    int Threads,
    int KernelHandles,
    uint GdiObjects,
    uint UserObjects,
    int LiveForms,
    int LiveApplications,
    int LiveProviders);

/// <summary>
///  Generates report and settings metadata outside reflection-based serialization.
/// </summary>
[JsonSerializable(typeof(MeasurementReport))]
[JsonSerializable(typeof(ProbePreferences))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class ProbeJsonContext : JsonSerializerContext
{
}

/// <summary>
///  Supplies a bounded, identical settings payload for both hosted paths.
/// </summary>
internal sealed class ProbePreferences
{
    public int Value { get; set; } = 42;
}

/// <summary>
///  Supplies eagerly validated desktop configuration.
/// </summary>
internal sealed class ProbeOptions
{
    public string Caption { get; set; } = "";
}
