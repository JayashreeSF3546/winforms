// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.Principal;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Distinguishes an elected primary lease from an observed secondary handoff result.
/// </summary>
internal sealed record WinFormsSingleInstanceSelection(WinFormsSingleInstanceResult Result, WinFormsSingleInstance? Primary);

/// <summary>
///  Owns one elected pipe handle and its bounded activation listener, without creating a host or UI.
/// </summary>
/// <remarks>
///  <para>
///   There is no automatic retry, fallback UI, startup queue or exactly-once guarantee.
///   Successful Build transfers the lease to the application's existing cleanup path.
///  </para>
/// </remarks>
internal sealed class WinFormsSingleInstance : IDisposable
{
    private const byte ResponseReceipt = 1;
    private readonly NamedPipeServerStream _pipe;
    private readonly WinFormsSingleInstanceIdentity _identity;
    private readonly TimeSpan _timeout;
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _gate = new();
    private WinFormsApplication? _application;
    private Task? _stopTask;
    private bool _claimed;

    private WinFormsSingleInstance(NamedPipeServerStream pipe, WinFormsSingleInstanceIdentity identity, TimeSpan timeout)
    {
        _pipe = pipe;
        _identity = identity;
        _timeout = timeout;
        Completion = Task.Run(ListenAsync);
    }

    internal Task Completion { get; }

    internal static async Task<WinFormsSingleInstanceSelection> SelectAsync(
        WinFormsSingleInstanceIdentity identity,
        IEnumerable<string> arguments,
        bool requestForeground,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(timeout, TimeSpan.FromMinutes(1));
        cancellationToken.ThrowIfCancellationRequested();
        WinFormsNextInstanceRequest request = new(Guid.NewGuid(), arguments, requestForeground);
        byte[] payload = WinFormsSingleInstanceProtocol.Encode(request);
        NamedPipeServerStream? pipe = WinFormsSingleInstancePipe.Create(identity);
        if (pipe is not null)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                pipe.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return new(WinFormsSingleInstanceResult.Primary, new(pipe, identity, timeout));
        }

        // An occupied or denied namespace is NOT proof of a legitimate first instance.
        // Only the authenticated connection and matching response below can establish delivery.

        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        using NamedPipeClientStream client = new(".", identity.PipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, TokenImpersonationLevel.Identification);
        bool attemptedSend = false;
        try
        {
            await client.ConnectAsync(deadline.Token).ConfigureAwait(false);
            if (!WinFormsSingleInstancePipe.VerifyProcess(client, identity, server: false))
            {
                return new(WinFormsSingleInstanceResult.Rejected, null);
            }

            attemptedSend = true;
            await WriteFrameAsync(client, payload, deadline.Token).ConfigureAwait(false);
            byte[] response = new byte[17];
            await client.ReadExactlyAsync(response, deadline.Token).ConfigureAwait(false);
            if (new Guid(response.AsSpan(0, 16)) != request.Id
                || response[16] is < (byte)WinFormsSingleInstanceResult.Handled or > (byte)WinFormsSingleInstanceResult.Unknown)
            {
                return new(WinFormsSingleInstanceResult.Unknown, null);
            }

            // A completed pipe write does not mean the peer read the reply. Let the server
            // retain the connection until consumption, rather than discarding an unread reply
            // at DisconnectNamedPipe. Receipt failure cannot undo an already validated result.
            try
            {
                await client.WriteAsync(new byte[] { ResponseReceipt }, deadline.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException)
            {
            }

            return new((WinFormsSingleInstanceResult)response[16], null);
        }
        catch (UnauthorizedAccessException)
        {
            return new(WinFormsSingleInstanceResult.Rejected, null);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or TimeoutException)
        {
            // Once writing starts, even caller cancellation can leave application side effects unknown.
            if (!attemptedSend)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            return new(attemptedSend ? WinFormsSingleInstanceResult.Unknown : WinFormsSingleInstanceResult.Unavailable, null);
        }
    }

    internal void Claim(WinFormsApplication application)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopTask is not null || Completion.IsCompleted, this);
            if (_claimed)
            {
                throw new InvalidOperationException(SR.WinFormsApplication_SingleInstanceAlreadyClaimed);
            }

            _claimed = true;
            _application = application;
        }
    }

    private async Task ListenAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                await _pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                deadline.CancelAfter(_timeout);
                try
                {
                    byte[] header = new byte[sizeof(int)];
                    await _pipe.ReadExactlyAsync(header, deadline.Token).ConfigureAwait(false);
                    if (!WinFormsSingleInstancePipe.VerifyClient(_pipe, _identity))
                    {
                        throw new UnauthorizedAccessException();
                    }

                    int size = BinaryPrimitives.ReadInt32LittleEndian(header);
                    if (size is < 0 or > WinFormsSingleInstanceProtocol.MaximumPayload)
                    {
                        throw new InvalidDataException();
                    }

                    byte[] payload = new byte[size];
                    await _pipe.ReadExactlyAsync(payload, deadline.Token).ConfigureAwait(false);
                    WinFormsNextInstanceRequest request = WinFormsSingleInstanceProtocol.Decode(payload);
                    WinFormsApplication? application;
                    lock (_gate)
                    {
                        application = _application;
                    }

                    WinFormsSingleInstanceResult result = application is null
                        ? WinFormsSingleInstanceResult.NotReady
                        : await application.DispatchNextInstanceAsync(request, deadline.Token).ConfigureAwait(false);
                    byte[] response = new byte[17];
                    request.Id.TryWriteBytes(response);
                    response[16] = (byte)result;
                    await _pipe.WriteAsync(response, deadline.Token).ConfigureAwait(false);
                    // Disconnect discards unread buffered data. A bounded asynchronous receipt
                    // avoids both that race and an unbounded synchronous WaitForPipeDrain.
                    byte[] receipt = new byte[1];
                    await _pipe.ReadExactlyAsync(receipt, deadline.Token).ConfigureAwait(false);
                    if (receipt[0] != ResponseReceipt)
                    {
                        throw new InvalidDataException();
                    }
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or OperationCanceledException or UnauthorizedAccessException)
                {
                    // Invalid, silent or disconnected clients get no success acknowledgement.
                    // The connection deadline is independent of UI progress and shutdown.
                }
                finally
                {
                    _pipe.Disconnect();
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        finally
        {
            _pipe.Dispose();
        }
    }

    internal static async Task WriteFrameAsync(Stream stream, byte[] payload, CancellationToken cancellationToken)
    {
        byte[] header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    internal Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task stop;
        lock (_gate)
        {
            if (_application is not null)
            {
                throw new InvalidOperationException(SR.WinFormsApplication_SingleInstanceAlreadyClaimed);
            }

            stop = _stopTask ??= StopCoreAsync();
        }

        return stop.WaitAsync(cancellationToken);
    }

    internal Task StopForApplicationAsync(WinFormsApplication application)
    {
        lock (_gate)
        {
            if (_application is not null && !ReferenceEquals(_application, application))
            {
                throw new InvalidOperationException(SR.WinFormsApplication_SingleInstanceAlreadyClaimed);
            }

            return _stopTask ??= StopCoreAsync();
        }
    }

    private async Task StopCoreAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        try
        {
            await Completion.ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _application = null;
            }

            _stop.Dispose();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_application is not null)
            {
                throw new InvalidOperationException(SR.WinFormsApplication_SingleInstanceAlreadyClaimed);
            }
        }

        StopAsync().GetAwaiter().GetResult();
    }
}
