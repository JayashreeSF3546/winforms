// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using System.Security.Principal;
using Microsoft.VisualBasic.ApplicationServices;

namespace System.Windows.Forms.UITests;

/// <summary>
///  Compares metadata-defined contracts using independently composed native-language processes.
/// </summary>
[Collection(nameof(WinFormsApplicationParityTests))]
[CollectionDefinition(nameof(WinFormsApplicationParityTests), DisableParallelization = true)]
public sealed partial class WinFormsApplicationParityTests(ITestOutputHelper output)
{
    private static readonly string[] s_tokens = ["日本語", "", "two words", "literal \"quotes\"", @"C:\space path\"];

    public static IEnumerable<object[]> Scenarios()
    {
        foreach (string language in new[] { "CSharp", "VisualBasic" })
        {
            foreach (string scenario in new[]
            {
                "legacy-normal", "legacy-cancel", "legacy-failure",
                "normal", "cancel", "startup-failure", "settings-failure",
                "ui-exception", "background-failure", "network", "splash-gate", "splash-stop"
            })
            {
                yield return [language, scenario];
            }
        }
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task NativeLanguage_ObeysItsModelContract(string language, string scenario)
    {
        using Process process = Start(language, scenario, s_tokens);
        Evidence evidence = await CompleteAsync(process);
        Assert.Equal(s_tokens, evidence.Arguments);
        string[] events = evidence.Events;
        string[] expected = scenario switch
        {
            "legacy-normal" => ["starting", "immutable", "activate", "shown", "shutdown", "returned"],
            "legacy-cancel" => ["starting", "immutable", "returned"],
            // Legacy suppresses an ordinary startup failure when ExitApplication=False.
            "legacy-failure" => ["starting", "immutable", "legacy-handled", "returned"],
            "cancel" => ["host-start", "starting", "immutable", "host-stop", "stopped", "host-dispose", "returned"],
            "startup-failure" => ["host-start", "starting", "immutable", "report:Startup",
                "host-stop", "stopped", "host-dispose", "caught-original", "returned"],
            // Stopping before the splash readiness gate is not an eligible feature shutdown.
            "splash-stop" => ["host-start", "starting", "immutable", "activate", "started",
                "gated", "host-stop", "stopped", "host-dispose", "splash-clean", "returned"],
            _ => []
        };
        if (expected.Length > 0)
        {
            Assert.Equal(expected, events);
            return;
        }

        // Partial order is intentional: host stop and settings persistence are not UI callbacks.
        AssertOrdered(events, "host-start", "starting", "immutable", "activate", "started");
        AssertOrdered(events, "shutdown", "prepare", "save", "host-stop", "stopped", "host-dispose");
        Assert.Single(events, e => e == "shutdown");
        Assert.Single(events, e => e == "host-dispose");
        Assert.DoesNotContain("logging-failure", events);
        if (scenario == "ui-exception")
        {
            AssertOrdered(events, "shown", "ui-handled", "shutdown");
        }

        if (scenario == "background-failure")
        {
            AssertOrdered(events, "shown", "background-fault", "shutdown");
            Assert.DoesNotContain("report:MessageLoop", events);
            Assert.DoesNotContain("ui-handled", events);
        }

        if (scenario == "settings-failure")
        {
            Assert.Contains("caught-original", events);
            Assert.DoesNotContain("saved", events);
        }
        else
        {
            AssertOrdered(events, "save", "saved", "host-stop");
            Assert.DoesNotContain("caught-original", events);
        }

        if (scenario == "network")
        {
            AssertOrdered(events, "shown", "network", "shutdown");
        }

        if (scenario.StartsWith("splash-", StringComparison.Ordinal))
        {
            AssertOrdered(events, "started", "gated", "shutdown", "splash-clean", "returned");
            if (scenario == "splash-gate")
            {
                AssertOrdered(events, "gated", "still-gated", "shown", "shutdown");
            }
            else
            {
                Assert.DoesNotContain("shown", events);
            }
        }
    }

    [Theory]
    [InlineData("CSharp", "ipc-normal", "Handled")]
    [InlineData("VisualBasic", "ipc-normal", "Handled")]
    [InlineData("CSharp", "ipc-failure", "HandlerFailed")]
    [InlineData("VisualBasic", "ipc-failure", "HandlerFailed")]
    [InlineData("CSharp", "ipc-notready", "NotReady")]
    [InlineData("VisualBasic", "ipc-notready", "NotReady")]
    public async Task SameExecutable_ReportsActualDeliveryOutcome(string language, string scenario, string result)
    {
        RequireSupportedIdentity();
        string id = Guid.NewGuid().ToString("N");
        using Process primary = Start(language, scenario, [id]);
        Task<string> errors = primary.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        try
        {
            Assert.Equal("READY", await primary.StandardOutput.ReadLineAsync(TestContext.Current.CancellationToken)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
            using Process secondary = Start(language, "ipc-secondary", [id, .. s_tokens]);
            Evidence secondaryEvidence = await CompleteAsync(secondary);
            Assert.Equal([result], secondaryEvidence.Events);
            // Release only after the acknowledgement: stopping within the handler can mean Unknown.
            await primary.StandardInput.WriteLineAsync("stop");
            Evidence primaryEvidence = await CompleteAsync(primary, errors);
            if (scenario == "ipc-notready")
            {
                Assert.Equal(["Primary"], primaryEvidence.Events);
            }
            else
            {
                Assert.Equal(["Primary", "next", "immutable"], primaryEvidence.Events);
                Assert.Equal(s_tokens, primaryEvidence.Arguments);
            }
        }
        finally
        {
            await ApplicationProcess.KillOwnedAsync(primary);
        }
    }

    [Theory]
    [InlineData("CSharp", "VisualBasic")]
    [InlineData("VisualBasic", "CSharp")]
    public async Task DifferentExecutablePaths_AreSeparatePrimaries(string first, string second)
    {
        RequireSupportedIdentity();
        string id = Guid.NewGuid().ToString("N");
        using Process primary = Start(first, "ipc-notready", [id]);
        Task<string> errors = primary.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        try
        {
            Assert.Equal("READY", await primary.StandardOutput.ReadLineAsync(TestContext.Current.CancellationToken)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
            using Process otherPath = Start(second, "ipc-secondary", [id, .. s_tokens]);
            Evidence otherEvidence = await CompleteAsync(otherPath);
            Assert.Equal(["Primary"], otherEvidence.Events);
            await primary.StandardInput.WriteLineAsync("stop");
            Assert.Equal(["Primary"], (await CompleteAsync(primary, errors)).Events);
        }
        finally
        {
            await ApplicationProcess.KillOwnedAsync(primary);
        }
    }

    private static Process Start(string language, string scenario, string[] arguments, string? directory = null)
    {
        string project = $"ApplicationBuilderParity.{language}";
        string executable = Path.Combine(directory ?? Path.Combine(AppContext.BaseDirectory, "Parity", project), $"{project}.exe");
        return ApplicationProcess.Start(executable, [scenario, .. arguments]);
    }

    private static void RequireSupportedIdentity()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        if (identity.User is null || identity.Owner != identity.User
            || identity.ImpersonationLevel != TokenImpersonationLevel.None)
        {
            Assert.Skip("NotRun: single-instance prototype requires a non-impersonated, user-owned token. "
                + "Provision a normal desktop user; elevation/custom-owner qualification is not covered.");
        }
    }

    private async Task<Evidence> CompleteAsync(Process process, Task<string>? errors = null, string? firstLine = null)
    {
#pragma warning disable VSTHRD003 // Redirected child stderr was started by this test; it has no UI/JTF dependency.
        (string text, string errorText) = await ApplicationProcess.CompleteAsync(process, errors);
#pragma warning restore VSTHRD003
        text = firstLine + text;
        output.WriteLine($"{process.StartInfo.FileName}: {text}{errorText}");
        Assert.Equal(0, process.ExitCode);
        Assert.Empty(errorText);
        Evidence evidence = JsonSerializer.Deserialize<Evidence>(text)!;
        Assert.Equal(typeof(Form).Module.ModuleVersionId, evidence.FormsModule);
        Assert.Equal(typeof(WindowsFormsApplicationBase).Module.ModuleVersionId, evidence.VisualBasicModule);
        Assert.Equal(Path.GetDirectoryName(process.StartInfo.FileName), Path.GetDirectoryName(evidence.FormsPath));
        Assert.Equal(Path.GetDirectoryName(process.StartInfo.FileName), Path.GetDirectoryName(evidence.VisualBasicPath));

        return evidence;
    }

    private static void AssertOrdered(string[] events, params string[] expected)
    {
        int previous = -1;
        foreach (string name in expected)
        {
            int current = Array.IndexOf(events, name);
            Assert.True(current > previous, $"Expected {string.Join(" -> ", expected)}; actual {string.Join(", ", events)}");
            previous = current;
        }
    }

    /// <summary>
    ///  Carries trace, raw tokens and loaded binary identity, not just a successful process exit.
    /// </summary>
    private sealed record Evidence(
        string[] Events, string[] Arguments, Guid FormsModule, Guid VisualBasicModule,
        string FormsPath, string VisualBasicPath, string[][] Requests, string ProcessArchitecture);
}
