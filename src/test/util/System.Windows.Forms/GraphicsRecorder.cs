// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace System;

internal static class GraphicsRecorder
{
    private delegate int EnhMetaFileProc(
        IntPtr hdc,
        IntPtr lpht,
        IntPtr lpmr,
        int nHandles,
        IntPtr data);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool EnumEnhMetaFile(
        IntPtr hdc,
        IntPtr hmf,
        EnhMetaFileProc proc,
        IntPtr param,
        IntPtr rect);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteEnhMetaFile(IntPtr hmf);

    public static RecordedGraphics Record(Size size, Action<Graphics> paint)
    {
        return Record(
            new RecorderOptions
            {
                Size = size
            },
            paint);
    }

    public static RecordedGraphics Record(RecorderOptions options, Action<Graphics> paint)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(paint);

        using Bitmap referenceBitmap = new(1, 1);
        using Graphics referenceGraphics = Graphics.FromImage(referenceBitmap);

        IntPtr referenceHdc = referenceGraphics.GetHdc();

        try
        {
            using MemoryStream stream = new();

            using Metafile metafile = new(
                stream,
                referenceHdc,
                new Rectangle(Point.Empty, options.Size),
                MetafileFrameUnit.Pixel,
                options.EmfType);

            using (Graphics graphics = Graphics.FromImage(metafile))
            {
                paint(graphics);
            }

            IntPtr hemf = metafile.GetHenhmetafile();

            try
            {
                return Enumerate(hemf);
            }
            finally
            {
                DeleteEnhMetaFile(hemf);
            }
        }
        finally
        {
            referenceGraphics.ReleaseHdc(referenceHdc);
        }
    }

    private static RecordedGraphics Enumerate(IntPtr hemf)
    {
        List<RecordedEmfRecord> records = new();

        EnhMetaFileProc callback = (hdc, table, recordPointer, handles, data) =>
        {
            RecordedEmfRecord record = ReadRecord(recordPointer);
            records.Add(record);

            return 1;
        };

        bool result = EnumEnhMetaFile(
            IntPtr.Zero,
            hemf,
            callback,
            IntPtr.Zero,
            IntPtr.Zero);

        if (!result)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return new RecordedGraphics(records);
    }

    private static RecordedEmfRecord ReadRecord(IntPtr recordPointer)
    {
        uint type = (uint)Marshal.ReadInt32(recordPointer, 0);
        int size = Marshal.ReadInt32(recordPointer, 4);

        byte[] data = new byte[size];
        Marshal.Copy(recordPointer, data, 0, size);

        return new RecordedEmfRecord((EmfRecordType)type, size, data);
    }
}

internal sealed class RecorderOptions
{
    public Size Size { get; init; } = new(100, 100);

    public EmfType EmfType { get; init; } = EmfType.EmfPlusDual;
}
