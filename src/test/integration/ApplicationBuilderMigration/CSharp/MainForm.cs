// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using System.Windows.Forms;

namespace ApplicationBuilderMigration.CSharp;

/// <summary>
///  Displays preserved tokens, retained work state, and the selected driver's events.
/// </summary>
internal sealed class MainForm : Form
{
    private readonly MigrationSession _session;
    private readonly TextBox _trace = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill };
    private readonly CheckBox _veto = new() { Text = "Veto ordinary close", AutoSize = true };
    private readonly Button _process = new() { Text = "Process item", AutoSize = true };
    private readonly Button _close = new() { Text = "Close", AutoSize = true };

    internal MainForm(MigrationSession session, bool probe)
    {
        _session = session;
        Text = "C# — shared builder (public prototype)";
        Width = 720;
        Height = 460;
        FlowLayoutPanel commands = new() { Dock = DockStyle.Bottom, AutoSize = true };
        commands.Controls.AddRange([_process, _veto, _close]);
        Controls.Add(_trace);
        Controls.Add(commands);
        _process.Click += (_, _) =>
        {
            _session.ProcessItem();
            Render();
        };
        _close.Click += (_, _) => Close();
        FormClosing += (_, e) =>
        {
            e.Cancel = _veto.Checked;
            _session.Events.Add(e.Cancel ? "close-vetoed" : "closing");
            Render();
        };
        Disposed += (_, _) => _session.Events.Add("form-disposed");
        Shown += (_, _) =>
        {
            _session.Events.Add("shown");
            Render();
            // Explicit test-only mode. Normal launches wait for the user.
            if (probe)
            {
                BeginInvoke(new Action(() =>
                {
                    _process.PerformClick();
                    _veto.Checked = true;
                    _close.PerformClick();
                    _veto.Checked = false;
                    _close.PerformClick();
                }));
            }
        };
    }

    private void Render()
        => _trace.Text = $"Processed items: {_session.Count}\r\n"
            + $"Arguments (JSON tokens): {JsonSerializer.Serialize(_session.Arguments)}\r\n"
            + string.Join("\r\n", _session.Events);
}
