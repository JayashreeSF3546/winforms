// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System;

internal sealed class RecordedEmfRecord
{
    public RecordedEmfRecord(EmfRecordType type, int size, byte[] data)
    {
        Type = type;
        Size = size;
        Data = data;
    }

    public EmfRecordType Type { get; }

    public int Size { get; }

    public byte[] Data { get; }

    public override string ToString()
    {
        return $"{Type} ({Size} bytes)";
    }
}
