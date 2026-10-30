// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using System.Windows.Forms.IntegrationTests.Common;

namespace System.Windows.Forms.UITests;

/// <summary>
///  Checks the stress workload's cleanup invariants in isolated processes, without resource budgets.
/// </summary>
[Collection(nameof(WinFormsScopeStressTests))]
[CollectionDefinition(nameof(WinFormsScopeStressTests), DisableParallelization = true)]
public class WinFormsScopeStressTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("manual", false)]
    [InlineData("manual", true)]
    [InlineData("scoped", false)]
    [InlineData("scoped", true)]
    public async Task Stress_ReleasesUiServicesProvidersAndSubscriptions(string mode, bool componentManager)
    {
        ProcessStartInfo start = new()
        {
            FileName = TestHelpers.GetExePath("ApplicationBuilderScopes.CSharp"),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in new[] { "--stress", "--mode", mode, "--cycles", "3", "--warmup", "2", "--interval", "2" })
        {
            start.ArgumentList.Add(argument);
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
        Assert.Equal(componentManager ? "component-manager" : "light", report.GetProperty("Backend").GetString());
        Assert.Equal(15, report.GetProperty("ServicesCreated").GetInt32());
        Assert.Equal(15, report.GetProperty("ServicesDisposed").GetInt32());
        Assert.Equal(15, report.GetProperty("ComponentsDisposed").GetInt32());
        Assert.Equal(30, report.GetProperty("ControlsDisposed").GetInt32());
        Assert.Equal(0, report.GetProperty("ActiveSubscriptions").GetInt32());
        JsonElement checkpoints = report.GetProperty("Checkpoints");
        Assert.Equal(3, checkpoints.GetArrayLength());
        Assert.Equal(0, checkpoints[0].GetProperty("CompletedCycles").GetInt32());
        Assert.Equal(2, checkpoints[1].GetProperty("CompletedCycles").GetInt32());
        Assert.Equal(3, checkpoints[2].GetProperty("CompletedCycles").GetInt32());
        foreach (JsonElement checkpoint in checkpoints.EnumerateArray())
        {
            foreach (string kind in new[]
            {
                "LiveForms", "LiveControls", "LiveComponents", "LiveServices", "LiveProviders", "LiveLifetimes", "LiveGraphicsResources"
            })
            {
                Assert.Equal(0, checkpoint.GetProperty(kind).GetInt32());
            }

            Assert.Equal(checkpoint.GetProperty("ServicesCreated").GetInt32(), checkpoint.GetProperty("ServicesDisposed").GetInt32());
            Assert.Equal(0, checkpoint.GetProperty("ActiveSubscriptions").GetInt32());
        }

        Assert.True(checkpoints[1].GetProperty("AllocatedBytes").GetInt64() > 0);
        Assert.True(checkpoints[2].GetProperty("AllocatedBytes").GetInt64() > 0);
    }
}
