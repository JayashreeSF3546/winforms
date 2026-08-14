// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;

namespace System;

internal sealed class RecordedGraphics
{
    public RecordedGraphics(IReadOnlyList<RecordedEmfRecord> records)
    {
        Records = records;
    }

    public IReadOnlyList<RecordedEmfRecord> Records { get; }

    public string Dump()
    {
        StringBuilder builder = new();

        foreach (RecordedEmfRecord record in Records)
        {
            builder.AppendLine(record.ToString());
        }

        return builder.ToString();
    }

    public bool Contains(EmfRecordType type)
    {
        return Records.Any(record => record.Type == type);
    }
}
