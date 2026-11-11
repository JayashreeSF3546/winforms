// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Windows.Forms.UITests;

/// <summary>
///  Validates finite launch races and isolation boundaries using only test-owned native workers.
/// </summary>
public sealed partial class WinFormsApplicationParityTests
{
    [Theory]
    [InlineData("CSharp")]
    [InlineData("VisualBasic")]
    public async Task NativeEnvironment_ConcurrentElectionAndReopen(string language)
    {
        RequireSupportedIdentity();
        string id = Guid.NewGuid().ToString("N");
        // Reuse the identity only after both previous processes have exited and released ownership.
        for (int iteration = 0; iteration < 5; iteration++)
        {
            output.WriteLine($"Election/reopen iteration {iteration + 1}/5; language={language}");
            using Process first = Start(language, "ipc-validate", [id, .. s_tokens]);
            Process? second = null;
            try
            {
                second = Start(language, "ipc-validate", [id, .. s_tokens]);
                await ArmAsync(first);
                await ArmAsync(second);
                await Task.WhenAll(ReleaseAsync(first), ReleaseAsync(second));
                string[] lines = await Task.WhenAll(ReadSignalAsync(first), ReadSignalAsync(second));
                Assert.Single(lines, line => line == "READY");
                Process primary = lines[0] == "READY" ? first : second;
                Process secondary = lines[0] == "READY" ? second : first;
                string response = lines[0] == "READY" ? lines[1] : lines[0];
                Evidence handoff = await CompleteAsync(secondary, firstLine: response);
                Assert.Single(handoff.Events);
                Assert.Contains(handoff.Events[0], new[] { "Handled", "NotReady" });
                Assert.Empty(handoff.Requests);
                await primary.StandardInput.WriteLineAsync("stop");
                Evidence owner = await CompleteAsync(primary);
                AssertOwner(owner, handoff.Events[0] == "Handled" ? 1 : 0);
                foreach (string[] request in owner.Requests)
                {
                    Assert.Equal(s_tokens, request);
                }
            }
            finally
            {
                await ApplicationProcess.KillOwnedAsync(first);
                if (second is not null)
                {
                    await ApplicationProcess.KillOwnedAsync(second);
                    second.Dispose();
                }
            }
        }
    }

    [Theory]
    [InlineData("CSharp")]
    [InlineData("VisualBasic")]
    public async Task NativeEnvironment_RapidActivation(string language)
    {
        RequireSupportedIdentity();
        const int Clients = 8;
        for (int iteration = 0; iteration < 3; iteration++)
        {
            output.WriteLine($"Rapid activation iteration {iteration + 1}/3; clients={Clients}; language={language}");
            string id = Guid.NewGuid().ToString("N");
            using Process primary = Start(language, "ipc-validate", [id]);
            List<Process> clients = [];
            try
            {
                await ArmAsync(primary);
                await ReleaseAsync(primary);
                Assert.Equal("READY", await ReadSignalAsync(primary));
                for (int index = 0; index < Clients; index++)
                {
                    clients.Add(Start(language, "ipc-validate", [id, index.ToString(), .. s_tokens]));
                }

                await Task.WhenAll(clients.Select(ArmAsync));
                await Task.WhenAll(clients.Select(ReleaseAsync));
                Evidence[] handoffs = await Task.WhenAll(clients.Select(client => CompleteAsync(client)));
                // Retain the owner's trace even when a client result fails the expectations below.
                await primary.StandardInput.WriteLineAsync("stop");
                Evidence owner = await CompleteAsync(primary);
                foreach (Evidence handoff in handoffs)
                {
                    Assert.Equal(["Handled"], handoff.Events);
                    Assert.Empty(handoff.Requests);
                }

                AssertOwner(owner, Clients);
                // No ordering promise between concurrent clients, but each immutable payload is unique.
                Assert.Equal(Clients, owner.Requests.Select(request => request[0]).Distinct().Count());
                for (int index = 0; index < Clients; index++)
                {
                    Assert.Contains(owner.Requests, request => request.SequenceEqual([index.ToString(), .. s_tokens]));
                }
            }
            finally
            {
                foreach (Process client in clients)
                {
                    await ApplicationProcess.KillOwnedAsync(client);
                    client.Dispose();
                }

                await ApplicationProcess.KillOwnedAsync(primary);
            }
        }
    }

    [Theory]
    [InlineData("CSharp", false)]
    [InlineData("VisualBasic", false)]
    [InlineData("CSharp", true)]
    [InlineData("VisualBasic", true)]
    public async Task NativeEnvironment_PathAndVersionIsolation(string language, bool differentVersion)
    {
        RequireSupportedIdentity();
        DirectoryInfo? copy = null;
        Process? first = null;
        Process? second = null;
        try
        {
            string? directory = null;
            if (!differentVersion)
            {
                copy = Directory.CreateTempSubdirectory("WinForms14980-");
                directory = Path.Combine(copy.FullName, "installed copy with spaces");
                string source = Path.Combine(AppContext.BaseDirectory, "Parity", $"ApplicationBuilderParity.{language}");
                foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                {
                    string destination = Path.Combine(directory, Path.GetRelativePath(source, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(file, destination);
                }
            }

            string id = Guid.NewGuid().ToString("N");
            first = Start(language, "ipc-validate", [id]);
            second = Start(language, differentVersion ? "ipc-validate-version" : "ipc-validate", [id], directory);
            await ArmAsync(first);
            await ArmAsync(second);
            await Task.WhenAll(ReleaseAsync(first), ReleaseAsync(second));
            Assert.Equal(["READY", "READY"], await Task.WhenAll(ReadSignalAsync(first), ReadSignalAsync(second)));
            await Task.WhenAll(first.StandardInput.WriteLineAsync("stop"), second.StandardInput.WriteLineAsync("stop"));
            Evidence[] owners = await Task.WhenAll(CompleteAsync(first), CompleteAsync(second));
            foreach (Evidence owner in owners)
            {
                AssertOwner(owner, 0);
            }
        }
        finally
        {
            if (first is not null)
            {
                await ApplicationProcess.KillOwnedAsync(first);
                first.Dispose();
            }

            if (second is not null)
            {
                await ApplicationProcess.KillOwnedAsync(second);
                second.Dispose();
            }

            copy?.Delete(recursive: true);
        }
    }

    private static void AssertOwner(Evidence evidence, int callbacks)
    {
        foreach (string name in new[] { "Primary", "host-start", "activate", "started", "form-dispose", "host-stop", "host-dispose" })
        {
            Assert.Single(evidence.Events, actual => actual == name);
        }

        Assert.Equal(callbacks, evidence.Events.Count(name => name == "next"));
        Assert.Equal(callbacks, evidence.Events.Count(name => name == "immutable"));
        Assert.Equal(callbacks, evidence.Requests.Length);
        Assert.Equal(Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(), evidence.ProcessArchitecture);
        AssertOrdered(evidence.Events, "Primary", "host-start", "activate", "started", "form-dispose", "host-stop", "host-dispose");
    }

    private static async Task ArmAsync(Process process)
        => Assert.Equal("ARMED", await ReadSignalAsync(process));

    private static Task ReleaseAsync(Process process)
        => process.StandardInput.WriteLineAsync("go");

    private static async Task<string> ReadSignalAsync(Process process)
        => await process.StandardOutput.ReadLineAsync(TestContext.Current.CancellationToken)
            .AsTask().WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("Worker exited before its coordination signal.");
}
