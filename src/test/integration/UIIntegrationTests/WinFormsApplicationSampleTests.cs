// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using System.Windows.Forms.IntegrationTests.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace System.Windows.Forms.UITests;

/// <summary>
///  Runs both language samples in isolated processes with real native message loops.
/// </summary>
[Collection(nameof(WinFormsApplicationSampleTests))]
[CollectionDefinition(nameof(WinFormsApplicationSampleTests), DisableParallelization = true)]
public class WinFormsApplicationSampleTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("CSharp", false)]
    [InlineData("CSharp", true)]
    [InlineData("VisualBasic", false)]
    [InlineData("VisualBasic", true)]
    public async Task BackgroundWorker_ShutdownCancellation_CompletesNormally(string language, bool uiReady)
    {
        using IHost host = new HostBuilder().Build();
        using CancellationTokenSource cancellation = new();
        TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (uiReady)
        {
            ready.SetResult();
        }

        using BackgroundService worker = CreateBackgroundWorker(language, host, ready.Task);
        Task execution = ExecuteBackgroundWorker(worker, cancellation.Token);
        Assert.False(execution.IsCompleted);

        cancellation.Cancel();
        await execution.WaitAsync(TestContext.Current.CancellationToken);

        Assert.True(execution.IsCompletedSuccessfully);
        Assert.False(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested);
    }

    [Theory]
    [InlineData("CSharp")]
    [InlineData("VisualBasic")]
    public async Task BackgroundWorker_UnrelatedFailureDuringShutdown_IsNotSwallowed(string language)
    {
        using IHost host = new HostBuilder().Build();
        using CancellationTokenSource cancellation = new();
        InvalidOperationException expected = new("UI readiness failed.");
        using BackgroundService worker = CreateBackgroundWorker(language, host, Task.FromException(expected));
        cancellation.Cancel();

        Task execution = ExecuteBackgroundWorker(worker, cancellation.Token);

        Exception actual = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await execution.WaitAsync(TestContext.Current.CancellationToken));
        Assert.Same(expected, actual);
    }

    private static BackgroundService CreateBackgroundWorker(string language, IHost host, Task uiReady)
    {
        Assembly assembly = Assembly.Load($"WinFormsApplicationBuilder.{language}");
        Type type = assembly.GetType($"WinFormsApplicationBuilder.{language}.SampleWorker", throwOnError: true)!;

        // Keep the sample's worker internal while testing the actual implementation in both languages.
        return (BackgroundService)Activator.CreateInstance(
            type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [host.Services.GetRequiredService<IHostApplicationLifetime>(), uiReady, false],
            culture: null)!;
    }

    private static Task ExecuteBackgroundWorker(BackgroundService worker, CancellationToken stoppingToken)
    {
        MethodInfo execute = worker.GetType().GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

        // Invoke directly so the test cancels only after the method has reached its first incomplete await.
        return (Task)execute.Invoke(worker, [stoppingToken])!;
    }

    public static TheoryData<string, string, bool, bool> Scenarios
    {
        get
        {
            TheoryData<string, string, bool, bool> data = [];
            foreach (string language in new[] { "CSharp", "VisualBasic" })
            {
                foreach (string scenario in new[] { "form", "context", "background", "cancellation", "shutdown" })
                {
                    foreach (bool componentManager in new[] { false, true })
                    {
                        foreach (bool dark in new[] { false, true })
                        {
                            data.Add(language, scenario, componentManager, dark);
                        }
                    }
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task Sample_DispatchesAndCleansUpBeforeStopped(
        string language,
        string scenario,
        bool componentManager,
        bool dark)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = TestHelpers.GetExePath($"WinFormsApplicationBuilder.{language}"),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(scenario);
        startInfo.ArgumentList.Add("--smoke");
        if (componentManager)
        {
            startInfo.ArgumentList.Add("--component-manager");
        }

        if (dark)
        {
            startInfo.ArgumentList.Add("--dark");
        }

        using Process process = TestHelpers.StartProcess(startInfo);
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> standardError = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
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

        string transcript = await standardOutput;
        output.WriteLine(transcript);
        output.WriteLine(await standardError);
        Assert.Equal(0, process.ExitCode);
        string[] lines = transcript.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines, line => line == "Started");
        Assert.Single(lines, line => line == "Stopping");
        Assert.Single(lines, line => line == "Stopped");
        Assert.Contains($"Color: {(dark ? "dark" : "light")}", lines);
        Assert.Contains($"Backend: {(componentManager ? "component-manager" : "light")}", lines);
        Assert.True(Array.IndexOf(lines, "Started") < Array.IndexOf(lines, "UI dispatched"));
        Assert.True(Array.IndexOf(lines, "UI dispatched") < Array.IndexOf(lines, "Stopping"));
        int disposed = Array.LastIndexOf(lines, "UI disposed");
        Assert.True(Array.IndexOf(lines, "Stopping") < disposed);
        Assert.True(disposed < Array.IndexOf(lines, "Stopped"));
        if (scenario is "background" or "shutdown")
        {
            Assert.Single(lines, line => line == "Host stopped");
            Assert.True(disposed < Array.IndexOf(lines, "Host stopped"));
            Assert.True(Array.IndexOf(lines, "Host stopped") < Array.IndexOf(lines, "Stopped"));
        }

        if (scenario == "context")
        {
            Assert.Contains("Context switched", lines);
            Assert.Equal(2, lines.Count(line => line == "UI disposed"));
        }

        if (scenario == "background")
        {
            Assert.Single(lines, line => line == "Background service started");
            Assert.Contains("Background requesting shutdown", lines);
            Assert.Single(lines, line => line == "Background service stopped");
        }

        if (scenario == "cancellation")
        {
            Assert.True(Array.IndexOf(lines, "Stopped") < Array.IndexOf(lines, "Canceled after UI cleanup"));
        }
    }
}
