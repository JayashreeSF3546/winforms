// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;

namespace System.Windows.Forms.UITests;

/// <summary>
///  Exercises the real public-consumer samples and retained VB event handlers in owned processes.
/// </summary>
[Collection(nameof(WinFormsApplicationParityTests))]
public sealed class WinFormsApplicationMigrationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("VisualBasic", "legacy", false)]
    [InlineData("VisualBasic", "legacy", true)]
    [InlineData("VisualBasic", "shared", false)]
    [InlineData("VisualBasic", "shared", true)]
    [InlineData("CSharp", "shared", false)]
    [InlineData("CSharp", "shared", true)]
    public async Task SelectedDriver_PreservesWorkAndArguments(string language, string model, bool cancel)
    {
        string[] tokens = ["日本語", "", "two words", "literal \"quotes\"", @"C:\space path\"];
        if (cancel)
        {
            tokens = [.. tokens, "--cancel-startup"];
        }

        string project = $"ApplicationBuilderMigration.{language}";
        string executable = Path.Combine(AppContext.BaseDirectory, "Migration", project, $"{project}.exe");
        using Process process = ApplicationProcess.Start(executable, [model, "--probe", "--", .. tokens]);
        (string text, string errors) = await ApplicationProcess.CompleteAsync(process);
        output.WriteLine($"{project} {model} cancel={cancel}: {text}{errors}");
        Assert.Equal(0, process.ExitCode);
        Assert.Empty(errors);
        Evidence evidence = JsonSerializer.Deserialize<Evidence>(text)!;
        Assert.Equal(tokens, evidence.Arguments);
        Assert.Equal(typeof(Form).Module.ModuleVersionId, evidence.FormsModule);
        Assert.Equal(Path.GetDirectoryName(executable), Path.GetDirectoryName(evidence.FormsPath));
        if (cancel)
        {
            Assert.Equal(["startup", "canceled", "returned"], evidence.Events);
            Assert.Equal(0, evidence.Count);
            return;
        }

        Assert.Equal(1, evidence.Count);
        Assert.Single(evidence.Events, e => e == "processed");
        Assert.Single(evidence.Events, e => e == "form-disposed");
        AssertOrdered(evidence.Events, "startup", "accepted", "activate", "shown",
            "processed", "close-vetoed", "closing", "form-disposed", "returned");
        if (model == "legacy")
        {
            Assert.Single(evidence.Events, e => e == "shutdown");
            AssertOrdered(evidence.Events, "form-disposed", "shutdown", "returned");
            Assert.DoesNotContain("started", evidence.Events);
        }
        else
        {
            AssertOrdered(evidence.Events, "activate", "started", "shown");
            AssertOrdered(evidence.Events, "closing", "stopping", "stopped", "returned");
            AssertOrdered(evidence.Events, "form-disposed", "stopped");
            Assert.DoesNotContain("shutdown", evidence.Events);
        }
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
    ///  Carries actual UI activity, original tokens, and the loaded public-prototype Forms identity.
    /// </summary>
    private sealed record Evidence(string[] Arguments, string[] Events, int Count, Guid FormsModule, string FormsPath);
}
