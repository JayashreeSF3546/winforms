// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Drawing.Imaging;

namespace System.Windows.Forms.Tests;

public class GraphicsRecorderTests
{
    [WinFormsFact]
    public void Record_NullPaint_ThrowsArgumentNullException()
    {
        Action action = () => GraphicsRecorder.Record(
            new Size(100, 100),
            paint: null!);

        action.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("paint");
    }

    [WinFormsFact]
    public void Record_NullOptions_ThrowsArgumentNullException()
    {
        Action action = () => GraphicsRecorder.Record(
            options: null!,
            paint: graphics => { });

        action.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("options");
    }

    [WinFormsFact]
    public void Record_OptionsWithNullPaint_ThrowsArgumentNullException()
    {
        Action action = () => GraphicsRecorder.Record(
            new RecorderOptions(),
            paint: null!);

        action.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("paint");
    }

    [WinFormsFact]
    public void Record_EmptyPaint_CapturesHeaderAndEofRecords()
    {
        RecordedGraphics recording = GraphicsRecorder.Record(
            new Size(100, 100),
            graphics =>
            {
            });

        recording.Records.Should().NotBeEmpty();
        recording.Records.Should().Contain(record => record.Type == EmfRecordType.EMR_HEADER);
        recording.Records.Should().Contain(record => record.Type == EmfRecordType.EMR_EOF);
    }

    [WinFormsFact]
    public void Record_DrawLine_CapturesRecords()
    {
        RecordedGraphics recording = GraphicsRecorder.Record(
            new Size(100, 100),
            graphics =>
            {
                graphics.DrawLine(Pens.Black, 0, 0, 50, 50);
            });

        recording.Records.Should().NotBeEmpty();
        recording.Records.Should().Contain(record => record.Type == EmfRecordType.EMR_HEADER);
        recording.Records.Should().Contain(record => record.Type == EmfRecordType.EMR_EOF);
    }

    [WinFormsFact]
    public void Record_FillRectangle_CapturesRecords()
    {
        RecordedGraphics recording = GraphicsRecorder.Record(
            new Size(100, 100),
            graphics =>
            {
                graphics.FillRectangle(Brushes.Red, new Rectangle(10, 10, 20, 20));
            });

        recording.Records.Should().NotBeEmpty();
        recording.Records.Should().Contain(record => record.Type == EmfRecordType.EMR_HEADER);
        recording.Records.Should().Contain(record => record.Type == EmfRecordType.EMR_EOF);
    }

    [WinFormsFact]
    public void Record_Dump_ReturnsRecordNames()
    {
        RecordedGraphics recording = GraphicsRecorder.Record(
            new Size(100, 100),
            graphics =>
            {
                graphics.DrawLine(Pens.Black, 0, 0, 50, 50);
            });

        string dump = recording.Dump();

        dump.Should().NotBeNullOrWhiteSpace();
        dump.Should().Contain(nameof(EmfRecordType.EMR_HEADER));
        dump.Should().Contain(nameof(EmfRecordType.EMR_EOF));
    }

    [WinFormsFact]
    public void Record_EmfPlusDual_DrawString_CapturesGdiComment()
    {
        RecordedGraphics recording = GraphicsRecorder.Record(
            new RecorderOptions
            {
                Size = new Size(200, 100),
                EmfType = EmfType.EmfPlusDual
            },
            graphics =>
            {
                graphics.DrawString(
                    "Hello",
                    SystemFonts.DefaultFont,
                    Brushes.Black,
                    PointF.Empty);
            });

        recording.Records.Should().NotBeEmpty();
        recording.Records.Should().Contain(record => record.Type == EmfRecordType.EMR_HEADER);
        recording.Records.Should().Contain(record => record.Type == EmfRecordType.EMR_EOF);
        recording.Records.Should().Contain(record => record.Type == EmfRecordType.EMR_GDICOMMENT);
    }

    [WinFormsFact]
    public void Record_EmfOnly_DrawString_DoesNotCaptureGdiComment()
    {
        RecordedGraphics recording = GraphicsRecorder.Record(
            new RecorderOptions
            {
                Size = new Size(200, 100),
                EmfType = EmfType.EmfOnly
            },
            graphics =>
            {
                graphics.DrawString(
                    "Hello",
                    SystemFonts.DefaultFont,
                    Brushes.Black,
                    PointF.Empty);
            });

        recording.Records.Should().NotBeEmpty();
        recording.Records.Should().Contain(record => record.Type == EmfRecordType.EMR_HEADER);
        recording.Records.Should().Contain(record => record.Type == EmfRecordType.EMR_EOF);
        recording.Records.Should().NotContain(record => record.Type == EmfRecordType.EMR_GDICOMMENT);
    }

    [WinFormsFact]
    public void Contains_WhenRecordExists_ReturnsTrue()
    {
        RecordedGraphics recording = GraphicsRecorder.Record(
            new Size(100, 100),
            graphics =>
            {
            });

        recording.Contains(EmfRecordType.EMR_HEADER).Should().BeTrue();
    }

    [WinFormsFact]
    public void Contains_WhenRecordDoesNotExist_ReturnsFalse()
    {
        RecordedGraphics recording = GraphicsRecorder.Record(
            new Size(100, 100),
            graphics =>
            {
            });

        recording.Contains(EmfRecordType.EMR_EXTTEXTOUTW).Should().BeFalse();
    }
}
