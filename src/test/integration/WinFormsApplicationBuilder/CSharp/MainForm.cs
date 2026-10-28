// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace WinFormsApplicationBuilder.CSharp;

/// <summary>
///  Provides an ordinary close action or an explicit application shutdown action.
/// </summary>
internal sealed class MainForm : Form
{
    public MainForm()
        : this("Close this default startup form to finish the application.", "Close window", requestShutdown: null)
    {
    }

    internal MainForm(string description, string actionText, Action? requestShutdown, bool vetoClose = false)
    {
        Text = "Application Builder - C#";
        ClientSize = new Size(560, 180);
        StartPosition = FormStartPosition.CenterScreen;
        FlowLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(12)
        };
        Label instructions = new()
        {
            Text = description,
            AutoSize = true,
            MaximumSize = new Size(520, 0)
        };
        Button action = new() { Text = actionText, AutoSize = true };
        action.Click += (_, _) =>
        {
            if (requestShutdown is null)
            {
                Close();
            }
            else
            {
                requestShutdown();
            }
        };
        layout.Controls.Add(instructions);
        layout.Controls.Add(action);
        if (vetoClose)
        {
            CheckBox veto = new() { Text = "Cancel ordinary window closing", Checked = true, AutoSize = true };
            FormClosing += (_, e) => e.Cancel = veto.Checked;
            layout.Controls.Add(veto);
        }

        Controls.Add(layout);
        Disposed += (_, _) => Debug.WriteLine("UI disposed");
    }
}
