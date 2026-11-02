// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Forms;

namespace ApplicationBuilderInfrastructure.CSharp;

/// <summary>
///  Starts the C# front end with shared, explicitly composed sample services.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
        => SampleApplication.Run(args, session => new InfrastructureForm(session));
}

/// <summary>
///  Presents native C# UI while root-owned, UI-free operations remain joinable after UI cleanup.
/// </summary>
public sealed class InfrastructureForm : Form
{
    private readonly SampleSession _session;
    private readonly NumericUpDown _value = new() { Minimum = 0, Maximum = int.MaxValue, Value = 7, Width = 120 };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new System.Drawing.Size(660, 0) };
    private readonly FlowLayoutPanel _commands = new() { AutoSize = true, WrapContents = true, Dock = DockStyle.Top };
    private bool _draining;
    private bool _allowClose;

    public InfrastructureForm(SampleSession session)
    {
        _session = session;
        Text = "C# infrastructure sample";
        Width = 740;
        Height = 300;
        _status.Text = session.Describe();
        FlowLayoutPanel content = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(12) };
        content.Controls.Add(_status);
        content.Controls.Add(_value);
        content.Controls.Add(_commands);
        Controls.Add(content);
        _commands.Width = 680;
        foreach (string command in new[] { "Load", "Save", "Reset", "Upgrade", "Recover", "Health", "Reload", "Wait" })
        {
            Button button = new() { Text = command, AutoSize = true };
            button.Click += async (_, _) => await RunCommandAsync(command);
            _commands.Controls.Add(button);
        }

        Button exception = new() { Text = "Controlled exception", AutoSize = true };
        exception.Click += (_, _) => throw new SampleUiException();
        _commands.Controls.Add(exception);
        Button stop = new() { Text = "Host stop", AutoSize = true };
        stop.Click += (_, _) => _session.RequestHostStop();
        content.Controls.Add(stop);
        Button failure = new() { Text = "Worker failure", AutoSize = true };
        failure.Click += (_, _) => _session.RequestWorkerFailure();
        content.Controls.Add(failure);
        Button close = new() { Text = "Close / cancel pending", AutoSize = true };
        close.Click += (_, _) => Close();
        content.Controls.Add(close);
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try
        {
            if (_session.Scenario.Length == 0)
            {
                await RunCommandAsync("Load");
            }
            else
            {
                await SampleApplication.RunScenarioAsync(_session, Close, PostControlledException);
            }
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            _session.AutomationFailure = exception;
            Console.Error.WriteLine($"SCENARIO FAILED: {exception.GetType().Name}");
            _session.RequestHostStop();
        }
    }

    private void PostControlledException()
        => BeginInvoke((Action)(() => throw new SampleUiException()));

    private async Task RunCommandAsync(string command)
    {
        _commands.Enabled = false;
        try
        {
            SampleResult result = await _session.ExecuteAsync(command, (int)_value.Value);
            if (!IsDisposed && !Disposing && !_draining)
            {
                _status.Text = result.Text;
                if (result.Value is int value)
                {
                    _value.Value = value;
                }
            }
        }
        finally
        {
            if (!IsDisposed && !Disposing && !_draining)
            {
                _commands.Enabled = true;
            }
        }
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.Cancel || _allowClose)
        {
            return;
        }

        e.Cancel = true;
        if (_draining)
        {
            return;
        }

        _draining = true;
        _commands.Enabled = false;
        _session.StopAccepting();
        try
        {
            await _session.DrainAsync();
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            _session.AutomationFailure = exception;
            Console.Error.WriteLine($"DRAIN FAILED: {exception.GetType().Name}");
        }

        if (!IsDisposed && !Disposing)
        {
            _allowClose = true;
            BeginInvoke(Close);
        }
    }
}
