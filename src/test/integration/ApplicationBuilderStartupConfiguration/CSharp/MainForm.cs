// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Windows.Forms;

namespace ApplicationBuilderStartupConfiguration.CSharp;

/// <summary>
///  Displays the defaults observed by controls created after the builder applies startup configuration.
/// </summary>
internal sealed class MainForm : Form
{
    public MainForm()
    {
        Text = "Application Builder - Startup Configuration";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(720, 390);
        StartPosition = FormStartPosition.CenterScreen;

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 3
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        Label description = new()
        {
            Text = "These defaults were applied by builder.Build(), before this form was created.",
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 12)
        };
        TextBox settings = new()
        {
            Multiline = true,
            ReadOnly = true,
            Dock = DockStyle.Fill,
            ScrollBars = ScrollBars.Vertical,
            AccessibleName = "Startup configuration",
            Lines =
            [
                $"HighDpiMode: {Application.HighDpiMode}",
                $"ColorMode: {Application.ColorMode}",
                $"UseCompatibleTextRendering (label): {description.UseCompatibleTextRendering}",
                $"UseVisualStyles: {Application.UseVisualStyles}",
                $"DefaultFormRevealMode: {Application.DefaultFormRevealMode}",
                $"DefaultVisualStylesMode: {Application.DefaultVisualStylesMode}",
                $"Inherited Font: {Font.Name}, {Font.SizeInPoints} pt",
                "UnhandledExceptionMode: ThrowException (configured; no public getter)",
                $"VisualStyleState: {Application.VisualStyleState}"
            ]
        };
        Button close = new()
        {
            Text = "Close",
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0, 12, 0, 0)
        };
        close.Click += (_, _) => Close();

        layout.Controls.Add(description, 0, 0);
        layout.Controls.Add(settings, 0, 1);
        layout.Controls.Add(close, 0, 2);
        Controls.Add(layout);
        AcceptButton = close;
        CancelButton = close;
    }
}
