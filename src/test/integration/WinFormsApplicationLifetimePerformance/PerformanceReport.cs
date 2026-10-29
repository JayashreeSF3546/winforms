// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Serialization;

namespace WinFormsLifetimePerformance;

/// <summary>
///  Contains timings and resource checkpoints from one isolated process.
/// </summary>
internal sealed record PerformanceReport(
    string Mode,
    string Backend,
    string Runtime,
    string Architecture,
    string OperatingSystem,
    bool TerminalServices,
    bool DebugBuild,
    int WarmupCycles,
    int MeasuredCycles,
    double ConfigurationMilliseconds,
    CycleMeasurement FirstUse,
    CycleMeasurement[] WarmCycles,
    ResourceSnapshot InitialResources,
    ResourceSnapshot[] Checkpoints);

/// <summary>
///  Stores one construction-to-dispatch and close-to-cleanup ownership cycle.
/// </summary>
internal sealed class CycleMeasurement
{
    public double StartupMilliseconds { get; set; }

    public double ShutdownMilliseconds { get; set; }

    public double TotalMilliseconds { get; set; }

    public long AllocatedBytes { get; set; }
}

/// <summary>
///  Captures process resources after full collection, outside timed ownership cycles.
/// </summary>
internal sealed record ResourceSnapshot(
    int CompletedCycles,
    long ManagedHeapBytes,
    long PrivateBytes,
    long WorkingSetBytes,
    int Threads,
    int Handles,
    uint GdiObjects,
    uint UserObjects,
    int LiveForms,
    int LiveApplications);

/// <summary>
///  Generates serialization metadata without reflection-based discovery in measured processes.
/// </summary>
[JsonSerializable(typeof(PerformanceReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class ReportJsonContext : JsonSerializerContext
{
}
