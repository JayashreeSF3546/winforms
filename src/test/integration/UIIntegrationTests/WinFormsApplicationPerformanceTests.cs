// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using System.Windows.Forms.IntegrationTests.Common;

namespace System.Windows.Forms.UITests;

/// <summary>
///  Verifies measurement boundaries and ownership cleanup without asserting performance budgets.
/// </summary>
[Collection(nameof(WinFormsApplicationPerformanceTests))]
[CollectionDefinition(nameof(WinFormsApplicationPerformanceTests), DisableParallelization = true)]
public class WinFormsApplicationPerformanceTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("legacy", false)]
    [InlineData("builder", false)]
    [InlineData("hosted", false)]
    [InlineData("legacy", true)]
    [InlineData("builder", true)]
    [InlineData("hosted", true)]
    public async Task Measurement_ReportsDispatchedCyclesAndCollectibleApplications(string mode, bool componentManager)
    {
        ProcessStartInfo start = new()
        {
            FileName = TestHelpers.GetExePath("WinFormsApplicationLifetimePerformance"),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("--mode");
        start.ArgumentList.Add(mode);
        start.ArgumentList.Add("--cycles");
        start.ArgumentList.Add("3");
        start.ArgumentList.Add("--warmup");
        start.ArgumentList.Add("1");
        if (componentManager)
        {
            start.ArgumentList.Add("--component-manager");
        }

        using Process process = TestHelpers.StartProcess(start);
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        using CancellationTokenSource watchdog = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        watchdog.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            await process.WaitForExitAsync(watchdog.Token);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }

        string transcript = await stdout;
        output.WriteLine(transcript);
        output.WriteLine(await stderr);
        Assert.Equal(0, process.ExitCode);
        Assert.StartsWith($"READY{Environment.NewLine}", transcript);
        using JsonDocument document = JsonDocument.Parse(transcript[(transcript.IndexOf('\n') + 1)..]);
        JsonElement report = document.RootElement;
        Assert.Equal(mode, report.GetProperty("Mode").GetString());
        Assert.Equal(componentManager ? "component-manager" : "light", report.GetProperty("Backend").GetString());
        Assert.Equal(3, report.GetProperty("MeasuredCycles").GetInt32());
        JsonElement cycles = report.GetProperty("WarmCycles");
        Assert.Equal(3, cycles.GetArrayLength());
        foreach (JsonElement cycle in cycles.EnumerateArray())
        {
            double startup = cycle.GetProperty("StartupMilliseconds").GetDouble();
            double shutdown = cycle.GetProperty("ShutdownMilliseconds").GetDouble();
            double total = cycle.GetProperty("TotalMilliseconds").GetDouble();
            Assert.True(startup >= 0);
            Assert.True(shutdown >= 0);
            Assert.True(total >= startup + shutdown - 0.000001);
            Assert.True(cycle.GetProperty("AllocatedBytes").GetInt64() > 0);
        }

        JsonElement checkpoints = report.GetProperty("Checkpoints");
        Assert.Equal(2, checkpoints.GetArrayLength());
        Assert.Equal(0, checkpoints[0].GetProperty("CompletedCycles").GetInt32());
        Assert.Equal(3, checkpoints[1].GetProperty("CompletedCycles").GetInt32());
        foreach (JsonElement checkpoint in checkpoints.EnumerateArray())
        {
            if (mode != "hosted")
            {
                Assert.Equal(0, checkpoint.GetProperty("LiveForms").GetInt32());
                Assert.Equal(0, checkpoint.GetProperty("LiveApplications").GetInt32());
            }
        }
    }
}
