// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Buffers.Binary;
using System.IO.Pipes;
using System.Runtime.ExceptionServices;
using System.Security.Principal;
using Microsoft.Extensions.WinForms;

namespace System.Windows.Forms.Tests;

/// <summary>
///  Exercises the bounded internal single-instance prototype, not multi-user certification.
/// </summary>
[Collection("Sequential")]
public class WinFormsSingleInstanceTests
{
    private static TimeSpan Timeout => TimeSpan.FromSeconds(5);

    [Fact]
    public void Identity_SeparatesBoundariesWithoutDelimiterCollisions()
    {
        string baseline = WinFormsSingleInstanceIdentity.GetKey("app", "1.2", @"C:\app.exe", "sid", 1);
        baseline.Should().Be(WinFormsSingleInstanceIdentity.GetKey("app", "1.2", @"c:\APP.exe", "sid", 1));
        baseline.Should().NotBe(WinFormsSingleInstanceIdentity.GetKey("other", "1.2", @"C:\app.exe", "sid", 1));
        baseline.Should().NotBe(WinFormsSingleInstanceIdentity.GetKey("app", "1.3", @"C:\app.exe", "sid", 1));
        baseline.Should().NotBe(WinFormsSingleInstanceIdentity.GetKey("app", "1.2", @"C:\other.exe", "sid", 1));
        baseline.Should().NotBe(WinFormsSingleInstanceIdentity.GetKey("app", "1.2", @"C:\app.exe", "other", 1));
        baseline.Should().NotBe(WinFormsSingleInstanceIdentity.GetKey("app", "1.2", @"C:\app.exe", "sid", 2));
        WinFormsSingleInstanceIdentity.GetKey("a|b", "c", @"C:\app.exe", "sid", 1)
            .Should().NotBe(WinFormsSingleInstanceIdentity.GetKey("a", "b|c", @"C:\app.exe", "sid", 1));
        WinFormsSingleInstanceIdentity current = WinFormsSingleInstanceIdentity.Create(Guid.NewGuid().ToString(), "test");
        current.MatchesProcess((uint)Environment.ProcessId).Should().BeTrue();
        current.MatchesProcess(uint.MaxValue).Should().BeFalse();
    }

    [Fact]
    public void Protocol_RoundTripsTokensAndRejectsInvalidFrames()
    {
        string[] tokens = ["", "日本語 🚀", "a b", "\"quoted\"", @"C:\a b\file.txt"];
        WinFormsNextInstanceRequest request = new(Guid.NewGuid(), tokens, requestForeground: true);
        byte[] payload = WinFormsSingleInstanceProtocol.Encode(request);
        WinFormsNextInstanceRequest decoded = WinFormsSingleInstanceProtocol.Decode(payload);
        decoded.CommandLine.Should().Equal(tokens);
        decoded.Id.Should().Be(request.Id);
        decoded.BringToForeground.Should().BeTrue();
        tokens[0] = "mutated";
        request.CommandLine[0].Should().BeEmpty();
        Assert.Throws<NotSupportedException>(() => ((IList<string>)decoded.CommandLine)[0] = "changed");
        Assert.Throws<InvalidDataException>(() => WinFormsSingleInstanceProtocol.Decode([1, 2]));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WinFormsSingleInstanceProtocol.Encode(new(Guid.NewGuid(), [new string('x', 65536)], false)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WinFormsSingleInstanceProtocol.Encode(new(Guid.NewGuid(), Enumerable.Repeat("", 257), false)));
        Assert.Throws<ArgumentNullException>(() => new WinFormsNextInstanceRequest(Guid.NewGuid(), [null!], false));
        Assert.Throws<InvalidDataException>(() => WinFormsSingleInstanceProtocol.Decode([.. payload, 0]));
        payload[20] = 2; // Invalid Boolean.
        Assert.Throws<InvalidDataException>(() => WinFormsSingleInstanceProtocol.Decode(payload));
        payload = WinFormsSingleInstanceProtocol.Encode(request);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(21), 257);
        Assert.Throws<InvalidDataException>(() => WinFormsSingleInstanceProtocol.Decode(payload));
        payload = WinFormsSingleInstanceProtocol.Encode(new(Guid.NewGuid(), ["日本語"], false));
        payload[^1] = 255; // Invalid UTF-8.
        Assert.Throws<InvalidDataException>(() => WinFormsSingleInstanceProtocol.Decode(payload));
    }

    [Fact]
    public async Task Election_NotReadyAndReleaseDoNotClaimDelivery()
    {
        WinFormsSingleInstanceIdentity identity = WinFormsSingleInstanceIdentity.Create(Guid.NewGuid().ToString(), "test");
        WinFormsSingleInstanceSelection first = await WinFormsSingleInstance.SelectAsync(identity, [], false, Timeout, TestContext.Current.CancellationToken);
        first.Result.Should().Be(WinFormsSingleInstanceResult.Primary);
        using WinFormsSingleInstance leader = first.Primary!;
        WinFormsSingleInstanceSelection second = await WinFormsSingleInstance.SelectAsync(identity, ["next"], false, Timeout, TestContext.Current.CancellationToken);
        second.Primary.Should().BeNull();
        second.Result.Should().Be(WinFormsSingleInstanceResult.NotReady);
        await leader.StopAsync(TestContext.Current.CancellationToken);
        leader.Completion.IsCompleted.Should().BeTrue();
        WinFormsSingleInstanceSelection reopened = await WinFormsSingleInstance.SelectAsync(identity, [], false, Timeout, TestContext.Current.CancellationToken);
        reopened.Result.Should().Be(WinFormsSingleInstanceResult.Primary);
        reopened.Primary!.Dispose();
    }

    [Fact]
    public async Task Election_ConcurrentLaunchesHaveOnlyOnePrimary()
    {
        WinFormsSingleInstanceIdentity identity = WinFormsSingleInstanceIdentity.Create(Guid.NewGuid().ToString(), "test");
        Task<WinFormsSingleInstanceSelection>[] launches = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => WinFormsSingleInstance.SelectAsync(identity, [], false, Timeout, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken)).ToArray();
        try
        {
            WinFormsSingleInstanceSelection[] results = await Task.WhenAll(launches);
            results.Count(r => r.Primary is not null).Should().Be(1);
            results.Where(r => r.Primary is null).Should().OnlyContain(r => r.Result == WinFormsSingleInstanceResult.NotReady);
        }
        finally
        {
            foreach (Task<WinFormsSingleInstanceSelection> launch in launches)
            {
                if (launch.IsCompletedSuccessfully)
                {
                    (await launch).Primary?.Dispose();
                }
            }
        }
    }

    [Fact]
    public async Task Election_ExplicitVersionCohortsAreIndependent()
    {
        string logicalId = Guid.NewGuid().ToString();
        using WinFormsSingleInstance first = (await WinFormsSingleInstance.SelectAsync(
            WinFormsSingleInstanceIdentity.Create(logicalId, "1.2"), [], false, Timeout, TestContext.Current.CancellationToken)).Primary!;
        using WinFormsSingleInstance second = (await WinFormsSingleInstance.SelectAsync(
            WinFormsSingleInstanceIdentity.Create(logicalId, "1.3"), [], false, Timeout, TestContext.Current.CancellationToken)).Primary!;
        first.Should().NotBeSameAs(second);
    }

    [Fact]
    public async Task Selection_AlreadyCanceledDoesNotAcquire()
    {
        WinFormsSingleInstanceIdentity identity = WinFormsSingleInstanceIdentity.Create(Guid.NewGuid().ToString(), "test");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            WinFormsSingleInstance.SelectAsync(identity, [], false, Timeout, cancellation.Token));
        WinFormsSingleInstanceSelection result = await WinFormsSingleInstance.SelectAsync(identity, [], false, Timeout, TestContext.Current.CancellationToken);
        using WinFormsSingleInstance leader = result.Primary!;
        result.Result.Should().Be(WinFormsSingleInstanceResult.Primary);
    }

    [Fact]
    public async Task Pipe_InvalidOversizeAndDisconnectedClientsDoNotBreakElection()
    {
        WinFormsSingleInstanceIdentity identity = WinFormsSingleInstanceIdentity.Create(Guid.NewGuid().ToString(), "test");
        using WinFormsSingleInstance leader = (await WinFormsSingleInstance.SelectAsync(identity, [], false, Timeout, TestContext.Current.CancellationToken)).Primary!;
        foreach (byte[] payload in new byte[][] { [1, 2], new byte[65537] })
        {
            using CancellationTokenSource watchdog = new(TimeSpan.FromSeconds(10));
            using NamedPipeClientStream client = CreateClient(identity);
            await client.ConnectAsync(watchdog.Token);
            byte[] header = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
            await client.WriteAsync(header, watchdog.Token);
            if (payload.Length <= 65536)
            {
                await client.WriteAsync(payload, watchdog.Token);
            }

            byte[] response = new byte[17];
            (await client.ReadAsync(response, watchdog.Token)).Should().Be(0);
        }

        using (NamedPipeClientStream disconnected = CreateClient(identity))
        {
            using CancellationTokenSource watchdog = new(TimeSpan.FromSeconds(10));
            await disconnected.ConnectAsync(watchdog.Token);
            await disconnected.WriteAsync(new byte[] { 1 }, watchdog.Token);
        }

        WinFormsSingleInstanceSelection next = await WinFormsSingleInstance.SelectAsync(identity, [], false, Timeout, TestContext.Current.CancellationToken);
        next.Result.Should().Be(WinFormsSingleInstanceResult.NotReady);
    }

    [Fact]
    public async Task Pipe_SilentClientDoesNotPreventStopAndCanceledWaitDoesNotCancelSharedStop()
    {
        WinFormsSingleInstanceIdentity identity = WinFormsSingleInstanceIdentity.Create(Guid.NewGuid().ToString(), "test");
        using WinFormsSingleInstance leader = (await WinFormsSingleInstance.SelectAsync(identity, [], false, Timeout, TestContext.Current.CancellationToken)).Primary!;
        using NamedPipeClientStream client = CreateClient(identity);
        using CancellationTokenSource watchdog = new(TimeSpan.FromSeconds(10));
        await client.ConnectAsync(watchdog.Token);
        using CancellationTokenSource canceledWait = new();
        canceledWait.Cancel();
        try
        {
            await leader.StopAsync(canceledWait.Token);
        }
        catch (OperationCanceledException)
        {
            // A stop which already completed may also return normally.
        }

        await leader.StopAsync(TestContext.Current.CancellationToken).WaitAsync(watchdog.Token);
        leader.Completion.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task Pipe_SilentConnectionDeadlinePreservesElection()
    {
        WinFormsSingleInstanceIdentity identity = WinFormsSingleInstanceIdentity.Create(Guid.NewGuid().ToString(), "test");
        using WinFormsSingleInstance leader = (await WinFormsSingleInstance.SelectAsync(identity, [], false,
            TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken)).Primary!;
        using CancellationTokenSource watchdog = new(TimeSpan.FromSeconds(10));
        using (NamedPipeClientStream client = CreateClient(identity))
        {
            await client.ConnectAsync(watchdog.Token);
            byte[] response = new byte[17];
            // Wait for the actual deadline-driven close; do not assert an elapsed duration.
            (await client.ReadAsync(response, watchdog.Token)).Should().Be(0);
        }

        WinFormsSingleInstanceSelection next = await WinFormsSingleInstance.SelectAsync(
            identity, [], false, Timeout, TestContext.Current.CancellationToken);
        next.Result.Should().Be(WinFormsSingleInstanceResult.NotReady);
    }

    [Fact]
    public async Task Pipe_UncorrelatedAcknowledgementIsNotDeliverySuccess()
    {
        // Deliberately controlled same-process endpoint; not adversarial same-user certification.
        WinFormsSingleInstanceIdentity identity = WinFormsSingleInstanceIdentity.Create(Guid.NewGuid().ToString(), "test");
        using NamedPipeServerStream fake = new(identity.PipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using CancellationTokenSource watchdog = new(TimeSpan.FromSeconds(10));
        Task endpoint = Task.Run(async () =>
        {
            await fake.WaitForConnectionAsync(watchdog.Token);
            byte[] header = new byte[4];
            await fake.ReadExactlyAsync(header, watchdog.Token);
            int length = BinaryPrimitives.ReadInt32LittleEndian(header);
            byte[] payload = new byte[length];
            await fake.ReadExactlyAsync(payload, watchdog.Token);
            byte[] reply = new byte[17];
            Guid.NewGuid().TryWriteBytes(reply);
            reply[16] = (byte)WinFormsSingleInstanceResult.Handled;
            await fake.WriteAsync(reply, watchdog.Token);
        }, TestContext.Current.CancellationToken);
        try
        {
            WinFormsSingleInstanceSelection result = await WinFormsSingleInstance.SelectAsync(
                identity, [], false, Timeout, TestContext.Current.CancellationToken);
            await endpoint;
            result.Result.Should().Be(WinFormsSingleInstanceResult.Unknown);
            result.Primary.Should().BeNull();
        }
        finally
        {
            watchdog.Cancel();
            try
            {
                await endpoint;
            }
            catch (OperationCanceledException) when (watchdog.IsCancellationRequested)
            {
            }
        }
    }

    [Fact]
    public async Task Pipe_RealSecondaryProcessDoesNotBuildHostOrUi()
    {
        string logicalId = Guid.NewGuid().ToString();
        WinFormsSingleInstanceIdentity identity = WinFormsSingleInstanceIdentity.Create(logicalId, "test");
        using WinFormsSingleInstance leader = (await WinFormsSingleInstance.SelectAsync(identity, [], false, Timeout, TestContext.Current.CancellationToken)).Primary!;
        await RunSecondaryProcessAsync(logicalId, WinFormsSingleInstanceResult.NotReady);
    }

    private static async Task RunSecondaryProcessAsync(string logicalId, WinFormsSingleInstanceResult expected)
    {
        using Process child = StartWorker(logicalId, expected.ToString());
        using CancellationTokenSource watchdog = new(TimeSpan.FromSeconds(30));
        try
        {
            await child.WaitForExitAsync(watchdog.Token);
            child.ExitCode.Should().Be(0);
        }
        finally
        {
            if (!child.HasExited)
            {
                // Only this test-owned process; no process-name or shared-session cleanup.
                child.Kill(entireProcessTree: true);
                using CancellationTokenSource cleanup = new(TimeSpan.FromSeconds(5));
                await child.WaitForExitAsync(cleanup.Token);
            }
        }
    }

    private static Process StartWorker(string logicalId, string expected, string? readyPipe = null)
    {
        ProcessStartInfo start = new(Environment.ProcessPath!)
        {
            UseShellExecute = false
        };
        start.ArgumentList.Add("--filter-method");
        start.ArgumentList.Add("System.Windows.Forms.Tests.WinFormsSingleInstanceTests.SecondaryProcessWorker");
        start.ArgumentList.Add("--parallel");
        start.ArgumentList.Add("none");
        start.Environment["WINFORMS_14976_TEST_ID"] = logicalId;
        start.Environment["WINFORMS_14976_TEST_RESULT"] = expected;
        if (readyPipe is not null)
        {
            start.Environment["WINFORMS_14976_TEST_READY"] = readyPipe;
        }

        return Process.Start(start)!;
    }

    [Fact]
    public async Task Election_DeadTestOwnedLeaderReleasesKernelElection()
    {
        string logicalId = Guid.NewGuid().ToString();
        string readyName = $"WinForms14976TestReady.{Guid.NewGuid():N}";
        using NamedPipeServerStream ready = new(readyName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using Process child = StartWorker(logicalId, "Primary", readyName);
        using CancellationTokenSource watchdog = new(TimeSpan.FromSeconds(35));
        try
        {
            await ready.WaitForConnectionAsync(watchdog.Token);
            byte[] marker = new byte[1];
            await ready.ReadExactlyAsync(marker, watchdog.Token);
            marker[0].Should().Be(1); // Child has actually acquired the election, not just started.
            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync(watchdog.Token);
            WinFormsSingleInstanceIdentity identity = WinFormsSingleInstanceIdentity.Create(logicalId, "test");
            WinFormsSingleInstanceSelection reopened = await WinFormsSingleInstance.SelectAsync(
                identity, [], false, Timeout, TestContext.Current.CancellationToken);
            using WinFormsSingleInstance primary = reopened.Primary!;
            reopened.Result.Should().Be(WinFormsSingleInstanceResult.Primary);
        }
        finally
        {
            if (!child.HasExited)
            {
                // Only this test-owned process; no process-name or shared-session cleanup.
                child.Kill(entireProcessTree: true);
                using CancellationTokenSource cleanup = new(TimeSpan.FromSeconds(5));
                await child.WaitForExitAsync(cleanup.Token);
            }
        }
    }

    [Fact]
    public async Task SecondaryProcessWorker()
    {
        string? logicalId = Environment.GetEnvironmentVariable("WINFORMS_14976_TEST_ID");
        if (logicalId is null)
        {
            return;
        }

        WinFormsSingleInstanceIdentity identity = WinFormsSingleInstanceIdentity.Create(logicalId, "test");
        if (Environment.GetEnvironmentVariable("WINFORMS_14976_TEST_READY") is { } readyName)
        {
            WinFormsSingleInstanceSelection election = await WinFormsSingleInstance.SelectAsync(identity, [], false, Timeout, TestContext.Current.CancellationToken);
            using WinFormsSingleInstance leader = election.Primary!;
            election.Result.Should().Be(WinFormsSingleInstanceResult.Primary);
            using NamedPipeClientStream ready = new(".", readyName, PipeDirection.Out,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, TokenImpersonationLevel.Identification);
            using CancellationTokenSource watchdog = new(TimeSpan.FromSeconds(5));
            await ready.ConnectAsync(watchdog.Token);
            await ready.WriteAsync(new byte[] { 1 }, watchdog.Token);
            // Bounded even if the owning parent test dies.
            await Task.Delay(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            return;
        }

        WinFormsSingleInstanceSelection result = await WinFormsSingleInstance.SelectAsync(identity, ["", "日本語", @"C:\a b\file.txt", "\"quoted\""], true, Timeout, TestContext.Current.CancellationToken);
        using WinFormsSingleInstance? accidentalPrimary = result.Primary;
        result.Result.Should().Be(Enum.Parse<WinFormsSingleInstanceResult>(Environment.GetEnvironmentVariable("WINFORMS_14976_TEST_RESULT")!));
        result.Primary.Should().BeNull();
        // A non-primary does not call Build/Run; no host or UI is required to select the role.
        Application.MessageLoop.Should().BeFalse();
        Application.OpenForms.Count.Should().Be(0);
    }

    [Fact]
    public void Pipe_RealSecondaryProcessCompletesOwnerThreadHandlerWithoutMainForm()
    {
        RunOnSta(() =>
        {
            string logicalId = Guid.NewGuid().ToString();
            WinFormsSingleInstanceIdentity identity = WinFormsSingleInstanceIdentity.Create(logicalId, "test");
            using WinFormsSingleInstance leader = WinFormsSingleInstance.SelectAsync(identity, [], false, Timeout, TestContext.Current.CancellationToken)
                .GetAwaiter().GetResult().Primary!;
            int calls = 0;
            int owner = Environment.CurrentManagedThreadId;
            Task? secondary = null;
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseApplicationContext(() => new ApplicationContext())
                .UseSingleInstance(leader, (_, e) =>
                {
                    Environment.CurrentManagedThreadId.Should().Be(owner);
                    e.CommandLine.Should().Equal("", "日本語", @"C:\a b\file.txt", "\"quoted\"");
                    e.BringToForeground.Should().BeTrue();
                    calls++;
                }).Build();
            application.Lifetime.ApplicationStarted += (_, _) =>
            {
                secondary = Task.Run(async () =>
                {
                    try
                    {
                        await RunSecondaryProcessAsync(logicalId, WinFormsSingleInstanceResult.Handled);
                    }
                    finally
                    {
                        await application.StopAsync(TestContext.Current.CancellationToken);
                    }
                }, TestContext.Current.CancellationToken);
            };
            using CancellationTokenSource watchdog = new(TimeSpan.FromSeconds(40));
            application.Run(watchdog.Token);
            secondary!.GetAwaiter().GetResult();
            calls.Should().Be(1);
            leader.Completion.IsCompletedSuccessfully.Should().BeTrue();
        });
    }

    private static NamedPipeClientStream CreateClient(WinFormsSingleInstanceIdentity identity)
        => new(".", identity.PipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, TokenImpersonationLevel.Identification);

    [Fact]
    public void Run_CustomContext_DispatchesOnceAndContainsHandlerFailure()
    {
        RunOnSta(() =>
        {
            WinFormsSingleInstanceIdentity identity = WinFormsSingleInstanceIdentity.Create(Guid.NewGuid().ToString(), "test");
            using WinFormsSingleInstance leader = WinFormsSingleInstance.SelectAsync(identity, [], false, Timeout, TestContext.Current.CancellationToken)
                .GetAwaiter().GetResult().Primary!;
            int owner = Environment.CurrentManagedThreadId;
            int calls = 0;
            WinFormsApplication? application = null;
            Task? secondary = null;
            using (application = WinFormsApplication.CreateBuilder()
                .UseApplicationContext(() => new ApplicationContext())
                .UseSingleInstance(leader, (_, e) =>
                {
                    Environment.CurrentManagedThreadId.Should().Be(owner);
                    application!.StartupContext!.MainForm.Should().BeNull();
                    e.CommandLine.Should().Equal("", "日本語", @"C:\a b\file.txt");
                    calls++;
                    if (calls == 1)
                    {
                        throw new InvalidOperationException("Contained activation failure.");
                    }
                }).Build())
            {
                application.Lifetime.ApplicationStarted += (_, _) =>
                {
                    secondary = Task.Run(async () =>
                    {
                        try
                        {
                            WinFormsSingleInstanceSelection failed = await WinFormsSingleInstance.SelectAsync(
                                identity, ["", "日本語", @"C:\a b\file.txt"], true, Timeout, TestContext.Current.CancellationToken);
                            failed.Result.Should().Be(WinFormsSingleInstanceResult.HandlerFailed);
                            WinFormsSingleInstanceSelection handled = await WinFormsSingleInstance.SelectAsync(
                                identity, ["", "日本語", @"C:\a b\file.txt"], true, Timeout, TestContext.Current.CancellationToken);
                            handled.Result.Should().Be(WinFormsSingleInstanceResult.Handled);
                        }
                        finally
                        {
                            await application.StopAsync(TestContext.Current.CancellationToken);
                        }
                    }, TestContext.Current.CancellationToken);
                };
                using CancellationTokenSource watchdog = new(TimeSpan.FromSeconds(30));
                application.Run(watchdog.Token);
                secondary!.GetAwaiter().GetResult();
                calls.Should().Be(2);
                leader.Completion.IsCompleted.Should().BeTrue();
            }
        });
    }

    [Fact]
    public void Run_CanceledStartup_ReleasesTransport()
    {
        RunOnSta(() =>
        {
            WinFormsSingleInstanceIdentity identity = WinFormsSingleInstanceIdentity.Create(Guid.NewGuid().ToString(), "test");
            using WinFormsSingleInstance leader = WinFormsSingleInstance.SelectAsync(identity, [], false, Timeout, TestContext.Current.CancellationToken)
                .GetAwaiter().GetResult().Primary!;
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseApplicationContext(() => throw new InvalidOperationException("Must not create UI."))
                .ConfigureApplicationFeatures(features => features.Starting = (_, e) => e.Cancel = true)
                .UseSingleInstance(leader, (_, _) => throw new InvalidOperationException("Must not activate."))
                .Build();
            application.Run();
            leader.Completion.IsCompleted.Should().BeTrue();
            using WinFormsSingleInstance reopened = WinFormsSingleInstance.SelectAsync(identity, [], false, Timeout, TestContext.Current.CancellationToken)
                .GetAwaiter().GetResult().Primary!;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Dispatch_QueuedCancellationOrStopPreventsCallback(bool stop)
    {
        // Direct dispatcher seam, not a simulated user/session/IPC authentication test.
        RunOnSta(() =>
        {
            WinFormsSingleInstanceIdentity identity = WinFormsSingleInstanceIdentity.Create(Guid.NewGuid().ToString(), "test");
            using WinFormsSingleInstance leader = WinFormsSingleInstance.SelectAsync(identity, [], false, Timeout, TestContext.Current.CancellationToken)
                .GetAwaiter().GetResult().Primary!;
            int calls = 0;
            Task<WinFormsSingleInstanceResult>? pending = null;
            using Form form = new();
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseStartupForm(form)
                .UseSingleInstance(leader, (_, _) => calls++)
                .Build();
            form.Shown += (_, _) =>
            {
                using CancellationTokenSource deadline = new();
                pending = application.DispatchNextInstanceAsync(new(Guid.NewGuid(), ["queued"], false), deadline.Token);
                if (stop)
                {
                    _ = application.StopAsync(TestContext.Current.CancellationToken);
                }
                else
                {
                    deadline.Cancel();
                    form.BeginInvoke(() => _ = application.StopAsync(TestContext.Current.CancellationToken));
                }
            };
            using CancellationTokenSource watchdog = new(TimeSpan.FromSeconds(30));
            application.Run(watchdog.Token);
            pending!.GetAwaiter().GetResult().Should().Be(stop
                ? WinFormsSingleInstanceResult.Stopping
                : WinFormsSingleInstanceResult.Unavailable);
            calls.Should().Be(0);
            leader.Completion.IsCompletedSuccessfully.Should().BeTrue();
        });
    }

    [Fact]
    public void Pipe_CancellationAfterHandlerStartsReportsUnknownWithoutRetry()
    {
        RunOnSta(() =>
        {
            WinFormsSingleInstanceIdentity identity = WinFormsSingleInstanceIdentity.Create(Guid.NewGuid().ToString(), "test");
            using WinFormsSingleInstance leader = WinFormsSingleInstance.SelectAsync(identity, [], false, Timeout, TestContext.Current.CancellationToken)
                .GetAwaiter().GetResult().Primary!;
            TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<WinFormsSingleInstanceSelection>? handoff = null;
            int calls = 0;
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseApplicationContext(() => new ApplicationContext())
                .UseSingleInstance(leader, (_, _) =>
                {
                    calls++;
                    started.TrySetResult();
                    // Test-owned client cancellation requires no UI dispatch.
                    handoff!.WaitAsync(Timeout).GetAwaiter().GetResult().Result.Should().Be(WinFormsSingleInstanceResult.Unknown);
                }).Build();
            application.Lifetime.ApplicationStarted += (_, _) =>
            {
                handoff = Task.Run(async () =>
                {
                    using CancellationTokenSource cancellation = new();
                    Task<WinFormsSingleInstanceSelection> sending = WinFormsSingleInstance.SelectAsync(identity, [], false, Timeout, cancellation.Token);
                    await started.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
                    cancellation.Cancel();
                    try
                    {
                        return await sending;
                    }
                    finally
                    {
                        _ = application.StopAsync(TestContext.Current.CancellationToken);
                    }
                }, TestContext.Current.CancellationToken);
            };
            using CancellationTokenSource watchdog = new(TimeSpan.FromSeconds(30));
            application.Run(watchdog.Token);
            handoff!.GetAwaiter().GetResult().Result.Should().Be(WinFormsSingleInstanceResult.Unknown);
            calls.Should().Be(1);
            leader.Completion.IsCompletedSuccessfully.Should().BeTrue();
        });
    }

    [Fact]
    public void Dispatch_StoppingInHandlerDoesNotRestoreOrActivateMainForm()
    {
        RunOnSta(() =>
        {
            WinFormsSingleInstanceIdentity identity = WinFormsSingleInstanceIdentity.Create(Guid.NewGuid().ToString(), "test");
            using WinFormsSingleInstance leader = WinFormsSingleInstance.SelectAsync(identity, [], false, Timeout, TestContext.Current.CancellationToken)
                .GetAwaiter().GetResult().Primary!;
            using Form form = new();
            Task<WinFormsSingleInstanceResult>? request = null;
            WinFormsApplication? application = null;
            bool shutdownObserved = false;
            using (application = WinFormsApplication.CreateBuilder().UseStartupForm(form)
                .UseSingleInstance(leader, (_, e) =>
                {
                    e.BringToForeground.Should().BeTrue();
                    form.WindowState = FormWindowState.Minimized;
                    _ = application!.StopAsync(TestContext.Current.CancellationToken);
                }).Build())
            {
                form.Shown += (_, _) => request = application.DispatchNextInstanceAsync(
                    new(Guid.NewGuid(), [], true), TestContext.Current.CancellationToken);
                application.Lifetime.ApplicationStopping += (_, _) =>
                {
                    form.WindowState.Should().Be(FormWindowState.Minimized);
                    shutdownObserved = true;
                };
                using CancellationTokenSource watchdog = new(TimeSpan.FromSeconds(30));
                application.Run(watchdog.Token);
                // Stop after callback entry makes its waiting transport outcome Unknown, not replayable.
                request!.GetAwaiter().GetResult().Should().Be(WinFormsSingleInstanceResult.Unknown);
                shutdownObserved.Should().BeTrue();
            }
        });
    }

    [Fact]
    public async Task Build_LeaseTransfersOnceAndUnrunDisposeReleasesIt()
    {
        WinFormsSingleInstanceIdentity identity = WinFormsSingleInstanceIdentity.Create(Guid.NewGuid().ToString(), "test");
        using WinFormsSingleInstance leader = (await WinFormsSingleInstance.SelectAsync(identity, [], false, Timeout, TestContext.Current.CancellationToken)).Primary!;
        using WinFormsApplication first = WinFormsApplication.CreateBuilder().UseStartupForm<Form>()
            .UseSingleInstance(leader, (_, _) => { }).Build();
        Assert.Throws<InvalidOperationException>(leader.Dispose);
        Assert.Throws<InvalidOperationException>(() => { _ = leader.StopAsync(TestContext.Current.CancellationToken); });
        Assert.Throws<InvalidOperationException>(() => WinFormsApplication.CreateBuilder().UseStartupForm<Form>()
            .UseSingleInstance(leader, (_, _) => { }).Build());
        first.Dispose();
        (await first.DispatchNextInstanceAsync(new(Guid.NewGuid(), [], false), TestContext.Current.CancellationToken))
            .Should().Be(WinFormsSingleInstanceResult.Stopping);
        leader.Completion.IsCompletedSuccessfully.Should().BeTrue();
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(60)).Should().BeTrue();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Throw(failure);
        }
    }
}
