// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using System.Windows.Forms.IntegrationTests.Common;

namespace System.Windows.Forms.UITests;

/// <summary>
///  Checks equivalent measured work and ownership without asserting machine-specific timing budgets.
/// </summary>
[Collection(nameof(WinFormsInfrastructurePerformanceTests))]
[CollectionDefinition(nameof(WinFormsInfrastructurePerformanceTests), DisableParallelization = true)]
public class WinFormsInfrastructurePerformanceTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("hostless", "empty", false, false)]
    [InlineData("conventional", "all", false, false)]
    [InlineData("integrated", "all", false, false)]
    [InlineData("conventional", "configuration", true, false)]
    [InlineData("integrated", "configuration", true, false)]
    [InlineData("conventional", "all", false, true)]
    [InlineData("integrated", "all", false, true)]
    public async Task Probe_ReportsEquivalentOperationsAndCompletedLifetimes(
        string mode, string workload, bool watchers, bool componentManager)
    {
        string runnerProject = typeof(WinFormsInfrastructurePerformanceTests).Assembly.GetName().Name!;
        DirectoryInfo? runnerOutput = new(AppContext.BaseDirectory);
        while (runnerOutput is not null && runnerOutput.Name != runnerProject)
        {
            runnerOutput = runnerOutput.Parent;
        }

        Assert.NotNull(runnerOutput);
        string configurationPath = Path.GetRelativePath(runnerOutput.FullName, AppContext.BaseDirectory);
        ProcessStartInfo start = new()
        {
            FileName = Path.Combine(runnerOutput.Parent!.FullName, "WinFormsInfrastructurePerformance",
                configurationPath, "WinFormsInfrastructurePerformance.exe"),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in new[] { "--mode", mode, "--workload", workload, "--cycles", "3", "--warmup", "1" })
        {
            start.ArgumentList.Add(argument);
        }

        if (watchers)
        {
            start.ArgumentList.Add("--watchers");
        }

        if (componentManager)
        {
            start.ArgumentList.Add("--component-manager");
        }

        using Process process = TestHelpers.StartProcess(start);
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        using CancellationTokenSource watchdog = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        watchdog.CancelAfter(TimeSpan.FromSeconds(90));
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
        string errors = await stderr;
        output.WriteLine(transcript);
        output.WriteLine(errors);
        Assert.Equal(0, process.ExitCode);
        Assert.Empty(errors);
        Assert.StartsWith($"READY{Environment.NewLine}", transcript);
        using JsonDocument document = JsonDocument.Parse(transcript[(transcript.IndexOf('\n') + 1)..]);
        JsonElement report = document.RootElement;
        Assert.Equal(mode, report.GetProperty("Mode").GetString());
        Assert.Equal(workload, report.GetProperty("Workload").GetString());
        Assert.Equal(watchers, report.GetProperty("Watchers").GetBoolean());
        Assert.Equal(componentManager ? "component-manager" : "light", report.GetProperty("Backend").GetString());
        Assert.Equal(3, report.GetProperty("Cycles").GetArrayLength());
        foreach (JsonElement cycle in report.GetProperty("Cycles").EnumerateArray())
        {
            double startup = cycle.GetProperty("StartupToDispatchMilliseconds").GetDouble();
            double shutdown = cycle.GetProperty("ShutdownMilliseconds").GetDouble();
            double lifetime = cycle.GetProperty("Lifetime").GetProperty("Milliseconds").GetDouble();
            Assert.True(startup > 0);
            Assert.True(shutdown >= 0);
            Assert.True(lifetime >= startup + shutdown - 0.1);
            Assert.True(cycle.GetProperty("Lifetime").GetProperty("AllocatedBytes").GetInt64() > 0);
            Assert.True(cycle.GetProperty("ReleasedFiles").GetInt32() >= 2);
            JsonElement operations = cycle.GetProperty("Operations");
            if (workload == "all")
            {
                Assert.Equal(1, cycle.GetProperty("ExpectedFailures").GetInt32());
                Assert.Equal(5, cycle.GetProperty("NotificationCount").GetInt32());
                Assert.Equal(1, cycle.GetProperty("HealthScopeDisposals").GetInt32());
                Assert.Equal(1, cycle.GetProperty("DisposedServices").GetInt32());
                Assert.True(cycle.GetProperty("WorkerStopped").GetBoolean());
                Assert.True(cycle.GetProperty("FlushAfterUi").GetBoolean());
                Assert.Equal(100, cycle.GetProperty("FlushedRecords").GetInt32());
                Assert.Equal(100, operations.GetProperty("LoggingDisabled").GetProperty("Operations").GetInt32());
                Assert.Equal(2, operations.GetProperty("SettingsSave").GetProperty("Operations").GetInt32());
                Assert.True(operations.GetProperty("ShutdownFlush").GetProperty("Milliseconds").GetDouble() >= 0);
                Assert.True(operations.GetProperty("FailureRecovery").GetProperty("Milliseconds").GetDouble() > 0);
                Assert.True(operations.GetProperty("HealthCheck").GetProperty("Milliseconds").GetDouble() > 0);
            }
            else if (workload == "configuration")
            {
                Assert.Equal(2, operations.EnumerateObject().Count());
                Assert.True(operations.TryGetProperty("ConfigurationReload", out _));
            }
            else
            {
                Assert.Empty(operations.EnumerateObject());
                Assert.Equal(0, cycle.GetProperty("DisposedServices").GetInt32());
            }
        }

        JsonElement checkpoints = report.GetProperty("Checkpoints");
        Assert.Equal(2, checkpoints.GetArrayLength());
        Assert.Equal(0, checkpoints[0].GetProperty("CompletedCycles").GetInt32());
        Assert.Equal(3, checkpoints[1].GetProperty("CompletedCycles").GetInt32());
        if (mode == "hostless")
        {
            Assert.Equal(0, checkpoints[1].GetProperty("LiveForms").GetInt32());
            Assert.Equal(0, checkpoints[1].GetProperty("LiveApplications").GetInt32());
            Assert.Equal(0, checkpoints[1].GetProperty("LiveProviders").GetInt32());
        }
    }
}
