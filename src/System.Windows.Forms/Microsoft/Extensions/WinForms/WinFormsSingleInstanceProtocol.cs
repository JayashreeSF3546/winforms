// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.ObjectModel;
using System.Text;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Describes one activation, independently of the original startup arguments.
/// </summary>
internal sealed class WinFormsNextInstanceRequest : EventArgs
{
    internal WinFormsNextInstanceRequest(Guid id, IEnumerable<string> arguments, bool requestForeground)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        // Enumerate only the permitted prefix; even a lazy infinite input is bounded.
        string[] snapshot = [.. arguments.Take(WinFormsSingleInstanceProtocol.MaximumArguments + 1)];
        ArgumentOutOfRangeException.ThrowIfGreaterThan(snapshot.Length, WinFormsSingleInstanceProtocol.MaximumArguments);
        foreach (string argument in snapshot)
        {
            ArgumentNullException.ThrowIfNull(argument);
        }

        Id = id;
        CommandLine = Array.AsReadOnly(snapshot);
        BringToForeground = requestForeground;
    }

    internal Guid Id { get; }
    internal ReadOnlyCollection<string> CommandLine { get; }
    internal bool BringToForeground { get; set; }
}

/// <summary>
///  Reports only the outcome actually observed, never exactly-once delivery or focus success.
/// </summary>
internal enum WinFormsSingleInstanceResult : byte
{
    Primary,
    Handled,
    NotReady,
    Stopping,
    HandlerFailed,
    HandledFocusFailed,
    Unavailable,
    Rejected,
    Unknown
}

/// <summary>
///  Encodes a versioned, bounded binary frame with separately length-prefixed UTF-8 arguments.
/// </summary>
internal static class WinFormsSingleInstanceProtocol
{
    internal const int MaximumArguments = 256;
    internal const int MaximumPayload = 65536;
    private const int Version = 1;
    private static readonly UTF8Encoding s_encoding = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    internal static byte[] Encode(WinFormsNextInstanceRequest request)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream, s_encoding, leaveOpen: true);
        writer.Write(Version);
        writer.Write(request.Id.ToByteArray());
        writer.Write(request.BringToForeground);
        writer.Write(request.CommandLine.Count);
        foreach (string argument in request.CommandLine)
        {
            int size = s_encoding.GetByteCount(argument);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(stream.Length + sizeof(int) + size, MaximumPayload);
            writer.Write(size);
            writer.Write(s_encoding.GetBytes(argument));
        }

        return stream.ToArray();
    }

    internal static WinFormsNextInstanceRequest Decode(byte[] payload)
    {
        try
        {
            if (payload.Length > MaximumPayload)
            {
                throw new InvalidDataException();
            }

            using MemoryStream stream = new(payload, writable: false);
            using BinaryReader reader = new(stream, s_encoding);
            if (reader.ReadInt32() != Version)
            {
                throw new InvalidDataException();
            }

            Guid id = new(reader.ReadBytes(16));
            byte foreground = reader.ReadByte();
            int count = reader.ReadInt32();
            if (foreground > 1 || count < 0 || count > MaximumArguments)
            {
                throw new InvalidDataException();
            }

            string[] arguments = new string[count];
            for (int i = 0; i < count; i++)
            {
                int size = reader.ReadInt32();
                if (size < 0 || size > stream.Length - stream.Position)
                {
                    throw new InvalidDataException();
                }

                arguments[i] = s_encoding.GetString(reader.ReadBytes(size));
            }

            if (stream.Position != stream.Length)
            {
                throw new InvalidDataException();
            }

            return new(id, arguments, foreground != 0);
        }
        catch (Exception exception) when (exception is EndOfStreamException or ArgumentException)
        {
            throw new InvalidDataException(null, exception);
        }
    }
}
