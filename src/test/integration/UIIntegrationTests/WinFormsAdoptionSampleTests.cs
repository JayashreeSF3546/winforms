// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows.Forms.IntegrationTests.Common;

namespace System.Windows.Forms.UITests;

/// <summary>
///  Runs adoption samples as real processes, including their UI loop, host and owned background work.
/// </summary>
[Collection(nameof(WinFormsAdoptionSampleTests))]
[CollectionDefinition(nameof(WinFormsAdoptionSampleTests), DisableParallelization = true)]
public class WinFormsAdoptionSampleTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("CSharp", "classic")]
    [InlineData("VisualBasic", "classic")]
    [InlineData("CSharp", "builder")]
    [InlineData("VisualBasic", "builder")]
    [InlineData("CSharp", "hosted")]
    [InlineData("VisualBasic", "hosted")]
    public async Task Migration_RetainsTheOriginalFormAndHandler(string language, string stage)
    {
        var (exitCode, transcript, errors) = await RunAsync(
            "Migration", language, "smoke", $"--Migration:Stage={stage}");
        Assert.Equal(0, exitCode);
        Assert.Empty(errors);
        Assert.Contains("LEGACY HANDLER Count: 1", transcript);
        Assert.Contains($"PASS migration {stage}: original Form disposed", transcript);
        if (stage == "hosted")
        {
            AssertHostedOrder(transcript);
        }
        else
        {
            Assert.DoesNotContain("HOST STARTED", transcript);
            Assert.DoesNotContain("HOST STOPPED", transcript);
        }
    }

    [Theory]
    [InlineData("CSharp", "Operations", 2)]
    [InlineData("VisualBasic", "Operations", 2)]
    [InlineData("CSharp", "Finance", 1)]
    [InlineData("VisualBasic", "Finance", 1)]
    public async Task Enterprise_ClosesEachOperationScopeBeforeReturningData(string language, string department, int orders)
    {
        string[] overrides = department == "Finance" ? ["--Enterprise:Department=Finance"] : [];
        var (exitCode, transcript, errors) = await RunAsync("Enterprise", language, "smoke", overrides);
        Assert.Equal(0, exitCode);
        Assert.Empty(errors);
        Assert.Contains($"SCOPES created=2 disposed=2; department={department}; orders={orders}", transcript);
        Assert.Contains("PASS enterprise:", transcript);
        AssertHostedOrder(transcript);
    }

    [Theory]
    [InlineData("CSharp", "exit")]
    [InlineData("VisualBasic", "exit")]
    [InlineData("CSharp", "host-stop")]
    [InlineData("VisualBasic", "host-stop")]
    public async Task Tray_OptionalWindowDoesNotOwnLifetime(string language, string scenario)
    {
        var (exitCode, transcript, errors) = await RunAsync("Tray", language, scenario);
        Assert.Equal(0, exitCode);
        Assert.Empty(errors);
        Assert.Contains("UI ACTIVATED tray; MainForm=null", transcript);
        Assert.Contains("WINDOW CLOSED; tray remains active", transcript);
        Assert.Contains("TRAY windows opened=2 disposed=2", transcript);
        Assert.Contains("PASS tray:", transcript);
        Assert.Equal(1, transcript.Split("UI DISPOSED tray", StringSplitOptions.None).Length - 1);
        AssertHostedOrder(transcript);
    }

    [Theory]
    [InlineData("CSharp", "smoke")]
    [InlineData("VisualBasic", "smoke")]
    [InlineData("CSharp", "close-pending")]
    [InlineData("VisualBasic", "close-pending")]
    [InlineData("CSharp", "host-stop")]
    [InlineData("VisualBasic", "host-stop")]
    public async Task Backend_OfflineWorkIsResponsiveAndJoinedAtShutdown(string language, string scenario)
    {
        string[] overrides = scenario == "smoke"
            ? ["--Backend:IntervalMilliseconds=60000"]
            : ["--Backend:IntervalMilliseconds=60000", "--Backend:OfflineDelayMilliseconds=30000"];
        var (exitCode, transcript, errors) = await RunAsync("Backend", language, scenario, overrides);
        Assert.Equal(0, exitCode);
        Assert.Empty(errors);
        Assert.Contains("BACKEND mode=offline", transcript);
        Assert.Contains("PASS backend:", transcript);
        if (scenario == "smoke")
        {
            Assert.Contains("SNAPSHOT Offline snapshot 1; UI clicks=1", transcript);
        }
        else
        {
            Assert.Contains("READ PENDING", transcript);
            Assert.Contains("CANCELED backend work", transcript);
        }

        AssertHostedOrder(transcript);
        AssertBefore(transcript, "STOPPED backend worker", "DISPOSED backend worker");
        Assert.Equal(1, transcript.Split("DISPOSED backend worker", StringSplitOptions.None).Length - 1);
    }

    [Theory]
    [InlineData("CSharp")]
    [InlineData("VisualBasic")]
    public async Task Backend_StopDuringStartupStillJoinsWorker(string language)
    {
        var (exitCode, transcript, errors) = await RunAsync("Backend", language, "stop-startup",
            "--Backend:OfflineDelayMilliseconds=30000");
        Assert.Equal(0, exitCode);
        Assert.Empty(errors);
        Assert.DoesNotContain("UI ACTIVATED", transcript);
        Assert.Contains("PASS backend:", transcript);
        AssertBefore(transcript, "STOPPED backend worker", "DISPOSED backend worker");
    }

    [Theory]
    [InlineData("CSharp", "Enterprise", "--Enterprise:Department=Unknown")]
    [InlineData("VisualBasic", "Enterprise", "--Enterprise:Department=Unknown")]
    [InlineData("CSharp", "Backend", "--Backend:IntervalMilliseconds=0")]
    [InlineData("VisualBasic", "Backend", "--Backend:IntervalMilliseconds=0")]
    [InlineData("CSharp", "Backend", "--Backend:Endpoint=http://example.invalid", "--Backend:Offline=false")]
    [InlineData("VisualBasic", "Backend", "--Backend:Endpoint=http://example.invalid", "--Backend:Offline=false")]
    public async Task InvalidOptions_FailBeforeUiActivation(string language, string sample, params string[] arguments)
    {
        var (exitCode, transcript, errors) = await RunAsync(sample, language, "smoke", arguments);
        Assert.Equal(1, exitCode);
        Assert.Contains("APPLICATION FAILED: OptionsValidationException", errors);
        Assert.DoesNotContain("UI ACTIVATED", transcript);
        Assert.DoesNotContain("PASS ", transcript);
    }

    [Theory]
    [InlineData("CSharp", 200)]
    [InlineData("VisualBasic", 200)]
    [InlineData("CSharp", 503)]
    [InlineData("VisualBasic", 503)]
    public async Task Backend_HttpResultLeavesUiResponsive(string language, int statusCode)
    {
        using CancellationTokenSource watchdog = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        watchdog.CancelAfter(TimeSpan.FromSeconds(60));
        TcpListener server = new(IPAddress.Loopback, 0);
        server.Start();
        int port = ((IPEndPoint)server.LocalEndpoint).Port;
        Task response = RespondAsync(server, statusCode, watchdog.Token);
        try
        {
            var (exitCode, transcript, errors) = await RunAsync("Backend", language, statusCode == 200 ? "smoke" : "unavailable",
                "--Backend:Offline=false", $"--Backend:Endpoint=http://127.0.0.1:{port}",
                "--Backend:IntervalMilliseconds=60000");
            await response;
            Assert.Equal(0, exitCode);
            Assert.Empty(errors);
            Assert.Contains("BACKEND mode=http", transcript);
            Assert.Contains(statusCode == 200
                ? "SNAPSHOT Backend reachable: HTTP 200; UI clicks=1"
                : "SNAPSHOT Backend unavailable; UI clicks=1", transcript);
            Assert.Contains("PASS backend:", transcript);
            AssertHostedOrder(transcript);
            AssertBefore(transcript, "STOPPED backend worker", "DISPOSED backend worker");
        }
        finally
        {
            await watchdog.CancelAsync();
            try
            {
                await response;
            }
            catch (OperationCanceledException) when (watchdog.IsCancellationRequested)
            {
            }
            finally
            {
                server.Stop();
            }
        }
    }

    private static async Task RespondAsync(TcpListener server, int statusCode, CancellationToken cancellationToken)
    {
        using TcpClient client = await server.AcceptTcpClientAsync(cancellationToken);
        using NetworkStream stream = client.GetStream();
        using StreamReader reader = new(stream, Encoding.ASCII, leaveOpen: true);
        string? firstLine = await reader.ReadLineAsync(cancellationToken);
        Assert.StartsWith("HEAD ", firstLine);
        while (await reader.ReadLineAsync(cancellationToken) is { Length: > 0 })
        {
        }

        await stream.WriteAsync(
            Encoding.ASCII.GetBytes($"HTTP/1.1 {statusCode} Test\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"),
            cancellationToken);
    }

    private static void AssertHostedOrder(string transcript)
    {
        AssertBefore(transcript, "HOST STARTED", "UI ACTIVATED");
        AssertBefore(transcript, "UI DISPOSED", "HOST STOPPED");
    }

    private static void AssertBefore(string transcript, string first, string second)
    {
        int firstIndex = transcript.IndexOf(first, StringComparison.Ordinal);
        int secondIndex = transcript.IndexOf(second, StringComparison.Ordinal);
        Assert.True(firstIndex >= 0 && secondIndex > firstIndex, $"{first} must precede {second}.\n{transcript}");
    }

    private async Task<(int ExitCode, string Transcript, string Errors)> RunAsync(
        string sample, string language, string scenario, params string[] arguments)
    {
        DirectoryInfo? projectOutput = new(AppContext.BaseDirectory);
        while (projectOutput is not null && projectOutput.Name != GetType().Assembly.GetName().Name)
        {
            projectOutput = projectOutput.Parent;
        }

        Assert.NotNull(projectOutput);
        string configurationPath = Path.GetRelativePath(projectOutput.FullName, AppContext.BaseDirectory);
        string project = $"ApplicationBuilderAdoption.{sample}.{language}";
        ProcessStartInfo startInfo = new()
        {
            FileName = Path.Combine(projectOutput.Parent!.FullName, project, configurationPath, $"{project}.exe"),
            WorkingDirectory = Path.GetTempPath(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string key in startInfo.Environment.Keys.Where(key =>
            new[] { "Sample", "Enterprise", "Backend", "Migration" }.Any(prefix =>
                key.StartsWith(prefix + "__", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith(prefix + ":", StringComparison.OrdinalIgnoreCase))).ToArray())
        {
            startInfo.Environment.Remove(key);
        }

        startInfo.Environment.Remove("DOTNET_CONTENTROOT");
        startInfo.Environment.Remove("DOTNET_ENVIRONMENT");
        startInfo.ArgumentList.Add($"--Sample:Scenario={scenario}");
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = TestHelpers.StartProcess(startInfo);
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
        string errors = await stderr;
        output.WriteLine(transcript);
        output.WriteLine(errors);

        return (process.ExitCode, transcript, errors);
    }
}
