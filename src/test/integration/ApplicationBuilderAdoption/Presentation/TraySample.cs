// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Windows.Forms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.WinForms;

namespace ApplicationBuilderAdoption;

/// <summary>
///  Uses an explicitly owned application context rather than a hidden main Form.
/// </summary>
public static class TraySample
{
    public static int Run(string[] args)
    {
        IHostBuilder hostBuilder = new HostBuilder()
            .ConfigureHostConfiguration(config => config.AddInMemoryCollection(
                [new("hostBuilder:reloadConfigOnChange", "false")]))
            .ConfigureWinFormsConfiguration(args)
            .ConfigureLogging(logging => logging.AddDebug())
            .ConfigureServices(services => services.AddHostedService<HostTrace>());
        TrayContext? context = null;
        SampleProbe? probe = null;
        WinFormsApplication? application = null;
        using (application = WinFormsApplication.CreateBuilder()
            .UseHost(hostBuilder)
            .UseApplicationContext(() =>
            {
                IServiceProvider services = application!.Host!.Services;
                probe = new SampleProbe(services.GetRequiredService<IConfiguration>());
                SampleProbe.Require(probe.Scenario is "" or "exit" or "host-stop", "Unknown tray probe.");
                context = new TrayContext(services.GetRequiredService<IHostApplicationLifetime>(), probe);

                return context;
            })
            .Build())
        {
            application.Run();
        }

        SampleProbe.Require(context is { ResourcesDisposed: true, AllWindowsDisposed: true },
            "Tray-owned UI was not released.");
        probe!.Complete("tray: no main Form; icon, menu and optional window released");

        return 0;
    }
}

/// <summary>
///  Owns a notification icon, its menu, and a non-lifetime-controlling optional window.
/// </summary>
internal sealed class TrayContext : ApplicationContext
{
    private readonly ContextMenuStrip _menu = new();
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _open = new("Open window");
    private readonly ToolStripMenuItem _exit = new("Exit");
    private readonly ToolStripMenuItem _stopHost = new("Stop host");
    private readonly System.Windows.Forms.Timer _probeTimer = new() { Interval = 25 };
    private Form? _window;
    private int _openedWindows;
    private int _disposedWindows;

    internal bool ResourcesDisposed { get; private set; }
    internal bool AllWindowsDisposed => _openedWindows == _disposedWindows;

    internal TrayContext(IHostApplicationLifetime lifetime, SampleProbe probe)
    {
        _menu.Items.AddRange([_open, _stopHost, _exit]);
        _open.Click += (_, _) => OpenWindow();
        _exit.Click += (_, _) => ExitThread();
        _stopHost.Click += (_, _) => lifetime.StopApplication();
        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Application Builder tray sample",
            ContextMenuStrip = _menu,
            Visible = true
        };
        _icon.DoubleClick += (_, _) => OpenWindow();
        Console.WriteLine("UI ACTIVATED tray; MainForm=null");
        if (probe.Scenario.Length != 0)
        {
            _probeTimer.Tick += (_, _) =>
            {
                _probeTimer.Stop();
                try
                {
                    _open.PerformClick();
                    Form window = _window!;
                    window.Close();
                    SampleProbe.Require(window.IsDisposed && _window is null && _icon.Visible
                        && !ResourcesDisposed && MainForm is null, "Closing an optional window exited the tray lifetime.");
                    Console.WriteLine("WINDOW CLOSED; tray remains active");
                    if (probe.Scenario == "host-stop")
                    {
                        _open.PerformClick();
                        _stopHost.PerformClick();
                    }
                    else
                    {
                        _open.PerformClick();
                        _exit.PerformClick();
                    }
                }
                catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
                {
                    probe.Failure = exception;
                    ExitThread();
                }
            };
            _probeTimer.Start();
        }
    }

    private void OpenWindow()
    {
        if (_window is null)
        {
            Form window = new() { Text = "Optional tray window", ClientSize = new Size(420, 120) };
            window.Controls.Add(new Label
            {
                Text = "Close this window: the tray application keeps running.",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            });
            window.Disposed += (_, _) =>
            {
                _window = null;
                _disposedWindows++;
            };
            _openedWindows++;
            _window = window;
            window.Show();
        }
        else
        {
            _window.Activate();
        }
    }

    protected override void ExitThreadCore()
    {
        ReleaseUi();
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ReleaseUi();
        }

        base.Dispose(disposing);
    }

    private void ReleaseUi()
    {
        if (ResourcesDisposed)
        {
            return;
        }

        _probeTimer.Dispose();
        _window?.Dispose();
        _window = null;
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        ResourcesDisposed = true;
        Console.WriteLine($"TRAY windows opened={_openedWindows} disposed={_disposedWindows}");
        Console.WriteLine("UI DISPOSED tray");
    }
}
