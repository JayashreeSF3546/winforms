// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using System.Windows.Forms.IntegrationTests.Common;

namespace System.Windows.Forms.UITests;

/// <summary>
///  Runs the service samples as real applications with isolated per-test storage.
/// </summary>
[Collection(nameof(WinFormsInfrastructureSampleTests))]
[CollectionDefinition(nameof(WinFormsInfrastructureSampleTests), DisableParallelization = true)]
public class WinFormsInfrastructureSampleTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("CSharp", "smoke")]
    [InlineData("VisualBasic", "smoke")]
    [InlineData("CSharp", "close-pending")]
    [InlineData("VisualBasic", "close-pending")]
    [InlineData("CSharp", "host-stop")]
    [InlineData("VisualBasic", "host-stop")]
    [InlineData("CSharp", "worker-failure")]
    [InlineData("VisualBasic", "worker-failure")]
    public async Task Sample_UsesServicesAndDrainsOwnedOperations(string language, string scenario)
    {
        string storage = Path.Combine(Path.GetTempPath(), $"WinFormsInfrastructure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(storage);
        try
        {
            (int exitCode, string transcript, string errors) = await RunAsync(language, storage, scenario);
            Assert.Contains("STARTED Development; command-line caption;", transcript);
            if (scenario == "worker-failure")
            {
                Assert.Equal(1, exitCode);
                Assert.Contains("FAILED background worker: InvalidOperationException", errors);
                Assert.DoesNotContain("PASS ", transcript);
            }
            else
            {
                Assert.Equal(0, exitCode);
                Assert.Empty(errors);
                Assert.Contains($"PASS {scenario}:", transcript);
            }

            Assert.Contains("STOPPED background worker", transcript);
            Assert.Contains("DRAINED owner operations before provider disposal", transcript);
            Assert.Equal(1, transcript.Split("DISPOSED sample session", StringSplitOptions.None).Length - 1);
            Assert.True(transcript.IndexOf("DRAINED owner operations", StringComparison.Ordinal)
                < transcript.IndexOf("DISPOSED sample session", StringComparison.Ordinal));
            if (scenario == "smoke")
            {
                Assert.Contains("Health=Degraded", transcript);
                Assert.Contains("HANDLED controlled UI exception", transcript);
                Assert.Contains("OPERATION Reload", transcript);
                using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(
                    Path.Combine(storage, "settings.json"), TestContext.Current.CancellationToken));
                Assert.Equal(2, document.RootElement.GetProperty("schemaVersion").GetInt32());
                Assert.Equal(42, document.RootElement.GetProperty("settings").GetProperty("Value").GetInt32());
                Assert.True(File.Exists(Path.Combine(storage, "settings.json.bak")));
            }
            else
            {
                Assert.Contains("CANCELED Wait", transcript);
                Assert.True(transcript.IndexOf("CANCELED Wait", StringComparison.Ordinal)
                    < transcript.IndexOf("DRAINED owner operations", StringComparison.Ordinal));
                Assert.False(File.Exists(Path.Combine(storage, "settings.json")));
            }
        }
        finally
        {
            Directory.Delete(storage, recursive: true);
        }
    }

    [Theory]
    [InlineData("CSharp", "upgrade")]
    [InlineData("VisualBasic", "upgrade")]
    [InlineData("CSharp", "recover")]
    [InlineData("VisualBasic", "recover")]
    public async Task Sample_ExplicitSettingsMaintenancePreservesOrResetsPreferences(string language, string scenario)
    {
        string storage = Path.Combine(Path.GetTempPath(), $"WinFormsInfrastructure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(storage);
        try
        {
            string primary = Path.Combine(storage, "settings.json");
            string original = scenario == "upgrade"
                ? """{"schemaVersion":1,"revision":1,"settings":{"Value":42}}"""
                : """{"schemaVersion":2,"revision":1,"settings":{"Value":42}}""";
            await File.WriteAllTextAsync(scenario == "upgrade" ? primary : primary + ".bak",
                original, TestContext.Current.CancellationToken);
            if (scenario == "recover")
            {
                await File.WriteAllTextAsync(primary, "{damaged", TestContext.Current.CancellationToken);
            }

            (int exitCode, string transcript, string errors) = await RunAsync(language, storage, scenario);
            Assert.Equal(0, exitCode);
            if (scenario == "recover")
            {
                Assert.Contains("FAILED Load: InvalidDataException", errors);
                Assert.Contains("OPERATION Recover", transcript);
                Assert.Contains("OPERATION Reset", transcript);
            }
            else
            {
                Assert.Empty(errors);
                Assert.Contains("OPERATION Upgrade", transcript);
            }

            Assert.Contains($"PASS {scenario}:", transcript);
            using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(
                primary, TestContext.Current.CancellationToken));
            Assert.Equal(2, document.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(scenario == "upgrade" ? 42 : 7,
                document.RootElement.GetProperty("settings").GetProperty("Value").GetInt32());
        }
        finally
        {
            Directory.Delete(storage, recursive: true);
        }
    }

    [Theory]
    [InlineData("CSharp")]
    [InlineData("VisualBasic")]
    public async Task Sample_InvalidStartupOptionsDoNotActivateUiOrWriteSettings(string language)
    {
        string storage = Path.Combine(Path.GetTempPath(), $"WinFormsInfrastructure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(storage);
        try
        {
            (int exitCode, string transcript, string errors) = await RunAsync(
                language, storage, "smoke", additionalArguments: ["--Sample:WorkDelayMilliseconds=0"]);
            Assert.Equal(1, exitCode);
            Assert.Contains("APPLICATION FAILED: OptionsValidationException", errors);
            Assert.DoesNotContain("STARTED ", transcript);
            Assert.DoesNotContain("PASS ", transcript);
            Assert.Empty(Directory.GetFiles(storage));
        }
        finally
        {
            Directory.Delete(storage, recursive: true);
        }
    }

    [Theory]
    [InlineData("CSharp")]
    [InlineData("VisualBasic")]
    public async Task Sample_LoadsEnvironmentJsonFromExecutableDirectory(string language)
    {
        string storage = Path.Combine(Path.GetTempPath(), $"WinFormsInfrastructure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(storage);
        try
        {
            (int exitCode, string transcript, string errors) = await RunAsync(
                language, storage, "smoke", captionOverride: null);
            Assert.Equal(0, exitCode);
            Assert.Empty(errors);
            Assert.Contains("STARTED Development; Desktop infrastructure (Development);", transcript);
            Assert.Contains("PASS smoke:", transcript);
        }
        finally
        {
            Directory.Delete(storage, recursive: true);
        }
    }

    private async Task<(int ExitCode, string Transcript, string Errors)> RunAsync(
        string language, string storage, string scenario,
        string? captionOverride = "command-line caption", params string[] additionalArguments)
    {
        DirectoryInfo? projectOutput = new(AppContext.BaseDirectory);
        while (projectOutput is not null && projectOutput.Name != typeof(WinFormsInfrastructureSampleTests).Assembly.GetName().Name)
        {
            projectOutput = projectOutput.Parent;
        }

        Assert.NotNull(projectOutput);
        string bin = projectOutput.Parent!.FullName;
        string configurationPath = Path.GetRelativePath(projectOutput.FullName, AppContext.BaseDirectory);
        string project = $"ApplicationBuilderInfrastructure.{language}";
        ProcessStartInfo startInfo = new()
        {
            FileName = Path.Combine(bin, project, configurationPath, $"{project}.exe"),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = storage
        };
        startInfo.ArgumentList.Add($"--Sample:StorageDirectory={storage}");
        startInfo.ArgumentList.Add($"--Sample:Scenario={scenario}");
        startInfo.ArgumentList.Add("--environment=Development");
        startInfo.Environment.Remove("Sample__Caption");
        startInfo.Environment.Remove("Sample:Caption");
        startInfo.Environment.Remove("DOTNET_CONTENTROOT");
        if (captionOverride is not null)
        {
            startInfo.Environment["Sample__Caption"] = "environment caption";
            startInfo.ArgumentList.Add($"--Sample:Caption={captionOverride}");
        }

        startInfo.ArgumentList.Add("--Sample:Offline=true");
        foreach (string argument in additionalArguments)
        {
            startInfo.ArgumentList.Add(argument);
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
        string errors = await standardError;
        output.WriteLine(transcript);
        output.WriteLine(errors);

        return (process.ExitCode, transcript, errors);
    }
}
