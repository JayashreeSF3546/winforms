// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Owns the local named-pipe endpoint used to forward single-instance activations.
/// </summary>
internal sealed class WinFormsSingleInstanceCoordinator : IDisposable
{
    private const int ProtocolMagic = 0x57464142;
    private const byte ProtocolVersion = 1;
    private const int MaximumArgumentCount = 32767;
    private const int MaximumArgumentLength = 256 * 1024;
    private const int MaximumPayloadLength = 1024 * 1024;
    private const int ActivationTimeoutMilliseconds = 2500;
    private const int ClientReadTimeoutMilliseconds = 5000;

    private static readonly UTF8Encoding s_utf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly NamedPipeServerStream _server;
    private readonly Func<string[], bool> _onActivationReceived;
    private readonly Action<Exception> _onListenerFailure;
    private readonly CancellationTokenSource _shutdownSource = new();
    private readonly Task _listenerTask;
    private int _stopRequested;
    private int _disposed;

    private WinFormsSingleInstanceCoordinator(
        NamedPipeServerStream server,
        Func<string[], bool> onActivationReceived,
        Action<Exception> onListenerFailure)
    {
        _server = server;
        _onActivationReceived = onActivationReceived;
        _onListenerFailure = onListenerFailure;
        _listenerTask = ListenAsync();

        _ = _listenerTask.ContinueWith(
            static (task, state) =>
            {
                WinFormsSingleInstanceCoordinator coordinator =
                    (WinFormsSingleInstanceCoordinator)state!;
                coordinator._onListenerFailure(task.Exception!.GetBaseException());
            },
            this,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>
    ///  Tries to create the primary instance's pipe endpoint.
    /// </summary>
    internal static bool TryCreatePrimary(
        string pipeName,
        Func<string[], bool> onActivationReceived,
        Action<Exception> onListenerFailure,
        out WinFormsSingleInstanceCoordinator? coordinator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentNullException.ThrowIfNull(onActivationReceived);
        ArgumentNullException.ThrowIfNull(onListenerFailure);

        try
        {
            NamedPipeServerStream server = new(
                pipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            coordinator = new(server, onActivationReceived, onListenerFailure);

            return true;
        }
        catch (IOException)
        {
            coordinator = null;

            return false;
        }
    }

    /// <summary>
    ///  Sends arguments to the primary instance and waits for acceptance.
    /// </summary>
    internal static void SendActivation(string pipeName, IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentNullException.ThrowIfNull(arguments);

        byte[] payload = SerializeArguments(arguments);
        SendActivationAsync(pipeName, payload).GetAwaiter().GetResult();
    }

    /// <summary>
    ///  Stops accepting activation requests.
    /// </summary>
    internal void Stop()
    {
        if (Interlocked.Exchange(ref _stopRequested, 1) != 0)
        {
            return;
        }

        _shutdownSource.Cancel();
        _server.Dispose();
    }

    /// <summary>
    ///  Stops the listener and waits for its asynchronous operation to finish.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            Stop();
            _listenerTask.GetAwaiter().GetResult();
        }
        finally
        {
            _shutdownSource.Dispose();
        }
    }

    private static async Task SendActivationAsync(string pipeName, byte[] payload)
    {
        using NamedPipeClientStream client = new(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using CancellationTokenSource timeoutSource =
            new(TimeSpan.FromMilliseconds(ActivationTimeoutMilliseconds));

        try
        {
            await client.ConnectAsync(timeoutSource.Token).ConfigureAwait(false);

            byte[] length = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(length, payload.Length);
            await client.WriteAsync(length.AsMemory(), timeoutSource.Token).ConfigureAwait(false);
            await client.WriteAsync(payload.AsMemory(), timeoutSource.Token).ConfigureAwait(false);
            await client.FlushAsync(timeoutSource.Token).ConfigureAwait(false);

            byte[] acknowledgement = new byte[1];
            await client.ReadExactlyAsync(acknowledgement.AsMemory(), timeoutSource.Token).ConfigureAwait(false);

            if (acknowledgement[0] != 1)
            {
                throw new InvalidOperationException(
                    "The primary Windows Forms application rejected the activation request.");
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or OperationCanceledException
                or ObjectDisposedException)
        {
            throw new InvalidOperationException(
                "Could not forward the activation request to the primary Windows Forms application.",
                exception);
        }
    }

    private async Task ListenAsync()
    {
        CancellationToken shutdownToken = _shutdownSource.Token;

        while (!shutdownToken.IsCancellationRequested)
        {
            try
            {
                await _server.WaitForConnectionAsync(shutdownToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException) when (shutdownToken.IsCancellationRequested)
            {
                return;
            }

            using CancellationTokenSource clientTimeoutSource =
                CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
            clientTimeoutSource.CancelAfter(ClientReadTimeoutMilliseconds);

            try
            {
                await HandleConnectionAsync(clientTimeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!shutdownToken.IsCancellationRequested)
            {
            }
            catch (IOException)
            {
            }
            finally
            {
                if (_server.IsConnected)
                {
                    _server.Disconnect();
                }
            }
        }
    }

    private async Task HandleConnectionAsync(CancellationToken cancellationToken)
    {
        byte acknowledgement = 0;

        try
        {
            byte[] payload = await ReadPayloadAsync(_server, cancellationToken).ConfigureAwait(false);
            string[] arguments = DeserializeArguments(payload);
            acknowledgement = _onActivationReceived(arguments) ? (byte)1 : (byte)0;
        }
        catch (InvalidDataException)
        {
        }
        catch (EndOfStreamException)
        {
        }
        catch (DecoderFallbackException)
        {
        }

        await _server.WriteAsync(
            new[] { acknowledgement }.AsMemory(),
            cancellationToken).ConfigureAwait(false);
        await _server.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static byte[] SerializeArguments(IReadOnlyList<string> arguments)
    {
        if (arguments.Count > MaximumArgumentCount)
        {
            throw new ArgumentException("Too many arguments were provided for activation.", nameof(arguments));
        }

        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream, s_utf8, leaveOpen: true);
        writer.Write(ProtocolMagic);
        writer.Write(ProtocolVersion);
        writer.Write(arguments.Count);

        foreach (string argument in arguments)
        {
            ArgumentNullException.ThrowIfNull(argument);
            byte[] bytes = s_utf8.GetBytes(argument);

            if (bytes.Length > MaximumArgumentLength
                || stream.Length + sizeof(int) + bytes.Length > MaximumPayloadLength)
            {
                throw new ArgumentException("The activation arguments exceed the supported size.", nameof(arguments));
            }

            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        return stream.ToArray();
    }

    private static async Task<byte[]> ReadPayloadAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        byte[] lengthBuffer = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(lengthBuffer.AsMemory(), cancellationToken).ConfigureAwait(false);

        int length = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);

        if (length is <= 0 or > MaximumPayloadLength)
        {
            throw new InvalidDataException("The activation payload length is invalid.");
        }

        byte[] payload = new byte[length];
        await stream.ReadExactlyAsync(payload.AsMemory(), cancellationToken).ConfigureAwait(false);

        return payload;
    }

    private static string[] DeserializeArguments(byte[] payload)
    {
        using MemoryStream stream = new(payload, writable: false);
        using BinaryReader reader = new(stream, s_utf8, leaveOpen: true);

        if (reader.ReadInt32() != ProtocolMagic || reader.ReadByte() != ProtocolVersion)
        {
            throw new InvalidDataException("The activation protocol header is invalid.");
        }

        int count = reader.ReadInt32();

        if (count is < 0 or > MaximumArgumentCount)
        {
            throw new InvalidDataException("The activation argument count is invalid.");
        }

        string[] arguments = new string[count];

        for (int i = 0; i < count; i++)
        {
            int length = reader.ReadInt32();

            if (length is < 0 or > MaximumArgumentLength
                || length > stream.Length - stream.Position)
            {
                throw new InvalidDataException("An activation argument length is invalid.");
            }

            byte[] bytes = reader.ReadBytes(length);

            if (bytes.Length != length)
            {
                throw new InvalidDataException("An activation argument is incomplete.");
            }

            arguments[i] = s_utf8.GetString(bytes);
        }

        if (stream.Position != stream.Length)
        {
            throw new InvalidDataException("The activation payload contains trailing data.");
        }

        return arguments;
    }
}
