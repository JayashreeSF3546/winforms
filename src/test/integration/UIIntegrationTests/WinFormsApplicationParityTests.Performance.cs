// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using Microsoft.VisualBasic.ApplicationServices;

namespace System.Windows.Forms.UITests;

/// <summary>
///  Checks benchmark ownership and measurement boundaries without accepting timing budgets.
/// </summary>
public sealed partial class WinFormsApplicationParityTests
{
    [Theory]
    [InlineData("VisualBasic", "legacy", "empty")]
    [InlineData("VisualBasic", "legacy", "splash")]
    [InlineData("VisualBasic", "legacy", "network")]
    [InlineData("VisualBasic", "shared", "empty")]
    [InlineData("VisualBasic", "shared", "splash")]
    [InlineData("VisualBasic", "shared", "network")]
    [InlineData("CSharp", "shared", "empty")]
    [InlineData("CSharp", "shared", "splash")]
    [InlineData("CSharp", "shared", "network")]
    public async Task NativePerformance_ReportsCompletedLifetimes(string language, string mode, string workload)
    {
        using Process process = Start(language, "measure", [mode, workload, "3", "1"]);
        using JsonDocument document = await CompleteMeasurementAsync(process, ready: true);
        JsonElement report = document.RootElement;
        Assert.Equal(mode, report.GetProperty("Mode").GetString());
        Assert.Equal(workload, report.GetProperty("Workload").GetString());
        Assert.Equal(typeof(Form).Module.ModuleVersionId, report.GetProperty("FormsModule").GetGuid());
        Assert.Equal(typeof(WindowsFormsApplicationBase).Module.ModuleVersionId, report.GetProperty("VisualBasicModule").GetGuid());
        JsonElement samples = report.GetProperty("Samples");
        Assert.Equal(3, samples.GetArrayLength());
        foreach (JsonElement sample in samples.EnumerateArray())
        {
            double startup = sample.GetProperty("StartupMilliseconds").GetDouble();
            double shutdown = sample.GetProperty("ShutdownMilliseconds").GetDouble();
            Assert.True(startup > 0);
            Assert.True(shutdown >= 0);
            Assert.True(sample.GetProperty("TotalMilliseconds").GetDouble() >= startup + shutdown);
            Assert.True(sample.GetProperty("AllocatedBytes").GetInt64() > 0);
            if (mode == "shared")
            {
                Assert.Equal(0, sample.GetProperty("SubscriptionsAfterRun").GetInt32());
            }
        }

        JsonElement checkpoints = report.GetProperty("Checkpoints");
        Assert.Equal(2, checkpoints.GetArrayLength());
        Assert.Equal(0, checkpoints[0].GetProperty("CompletedCycles").GetInt32());
        Assert.Equal(3, checkpoints[1].GetProperty("CompletedCycles").GetInt32());
        foreach (JsonElement checkpoint in checkpoints.EnumerateArray())
        {
            Assert.Equal(0, checkpoint.GetProperty("OpenForms").GetInt32());
            // Weak survival and resource deltas remain diagnostics, not a shipping leak budget.
            Assert.True(checkpoint.GetProperty("Handles").GetInt32() > 0);
        }
    }

    [Theory]
    [InlineData("VisualBasic", "legacy")]
    [InlineData("VisualBasic", "shared")]
    [InlineData("CSharp", "shared")]
    public async Task NativePerformance_CorrelatesActualActivation(string language, string mode)
    {
        RequireSupportedIdentity();
        string id = Guid.NewGuid().ToString("N");
        using Process primary = Start(language, "measure-ipc", [mode, id, "owner"]);
        Process? secondary = null;
        try
        {
            Assert.Equal("READY", await ReadSignalAsync(primary));
            secondary = Start(language, "measure-ipc", [mode, id, "0"]);
            string callback = await ReadSignalAsync(primary);
            Assert.StartsWith("CALLBACK ", callback);
            using JsonDocument activated = JsonDocument.Parse(callback["CALLBACK ".Length..]);
            Assert.Equal("0", activated.RootElement.GetProperty("Sequence").GetString());
            Assert.True(activated.RootElement.GetProperty("Milliseconds").GetDouble() >= 0);
            (string result, string errors) = await ApplicationProcess.CompleteAsync(secondary);
            Assert.Empty(errors);
            Assert.Equal(0, secondary.ExitCode);
            Assert.Equal(mode == "shared" ? "RESULT Handled" : "RESULT LegacyReturned", result.Trim());
            await primary.StandardInput.WriteLineAsync("snapshot");
            string checkpoint = await ReadSignalAsync(primary);
            Assert.StartsWith("CHECKPOINT ", checkpoint);
            using JsonDocument resources = JsonDocument.Parse(checkpoint["CHECKPOINT ".Length..]);
            Assert.Equal(1, resources.RootElement.GetProperty("CompletedCycles").GetInt32());
            Assert.Equal(1, resources.RootElement.GetProperty("OpenForms").GetInt32());
            await primary.StandardInput.WriteLineAsync("stop");
            using JsonDocument report = await CompleteMeasurementAsync(primary, ready: false);
            Assert.Equal(0, report.RootElement.GetProperty("Final").GetProperty("OpenForms").GetInt32());
        }
        finally
        {
            await ApplicationProcess.KillOwnedAsync(primary);
            if (secondary is not null)
            {
                await ApplicationProcess.KillOwnedAsync(secondary);
                secondary.Dispose();
            }
        }
    }

    private async Task<JsonDocument> CompleteMeasurementAsync(Process process, bool ready)
    {
        (string transcript, string errors) = await ApplicationProcess.CompleteAsync(process);
        output.WriteLine(transcript + errors);
        Assert.Equal(0, process.ExitCode);
        Assert.Empty(errors);
        if (ready)
        {
            Assert.StartsWith($"READY{Environment.NewLine}", transcript);
            transcript = transcript[(transcript.IndexOf('\n') + 1)..];
        }

        return JsonDocument.Parse(transcript);
    }
}
