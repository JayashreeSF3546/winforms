// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Windows.Forms;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.WinForms;

namespace ApplicationBuilderSample.SystemTray.CSharp;

/// <summary>
///  Defines structured log messages for tray application lifetime events.
/// </summary>
internal static class TrayLog
{
    private static readonly Action<ILogger, Exception?> s_started =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(1, "TrayApplicationStarted"),
            "Tray application started. Use the notification icon to open or exit.");
    private static readonly Action<ILogger, Exception?> s_windowShown =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(2, "MainWindowShown"),
            "Displayed the tray application's main window.");
    private static readonly Action<ILogger, Exception?> s_windowHidden =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(3, "MainWindowHidden"),
            "Main window hidden; the application remains in the notification area.");

    /// <summary>
    ///  Logs tray application startup.
    /// </summary>
    internal static void Started(ILogger logger)
        => s_started(logger, null);

    /// <summary>
    ///  Logs that the main window was shown.
    /// </summary>
    internal static void WindowShown(ILogger logger)
        => s_windowShown(logger, null);

    /// <summary>
    ///  Logs that the main window was hidden to the tray.
    /// </summary>
    internal static void WindowHidden(ILogger logger)
        => s_windowHidden(logger, null);
}

/// <summary>
///  Starts a tray application whose context controls the WinForms lifetime.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        HostApplicationBuilder hostBuilder = Host.CreateApplicationBuilder(args);
        hostBuilder.Services.AddTransient<TrayMainForm>();
        IHost host = hostBuilder.Build();

        TrayApplicationContext context =
            new(
                host.Services,
                host.Services.GetRequiredService<ILogger<TrayApplicationContext>>());
        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder()
            .UseHost(host)
            .UseApplicationContext(context);

        using WinFormsApplication application = builder.Build();
        application.Run();
    }
}

/// <summary>
///  Owns the notification icon and creates the window on demand.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly IServiceProvider _services;
    private readonly ILogger<TrayApplicationContext> _logger;
    private readonly ContextMenuStrip _contextMenu = new();
    private readonly NotifyIcon _notifyIcon;
    private TrayMainForm? _mainForm;
    private bool _isExiting;

    /// <summary>
    ///  Initializes the tray icon and its lifetime commands.
    /// </summary>
    public TrayApplicationContext(
        IServiceProvider services,
        ILogger<TrayApplicationContext> logger)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(logger);

        _services = services;
        _logger = logger;

        _contextMenu.Items.Add(new ToolStripMenuItem("Open", null, (_, _) => ShowMainForm()));
        _contextMenu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitApplication()));

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = _contextMenu,
            Icon = SystemIcons.Application,
            Text = "WinForms tray sample",
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => ShowMainForm();

        TrayLog.Started(_logger);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _contextMenu.Dispose();

            if (_mainForm is not null)
            {
                _mainForm.FormClosing -= OnMainFormClosing;
                _mainForm.Dispose();
                _mainForm = null;
            }
        }

        base.Dispose(disposing);
    }

    private void ShowMainForm()
    {
        if (_mainForm is null || _mainForm.IsDisposed)
        {
            _mainForm = _services.GetRequiredService<TrayMainForm>();
            _mainForm.FormClosing += OnMainFormClosing;
        }

        _mainForm.Show();
        _mainForm.WindowState = FormWindowState.Normal;
        _mainForm.Activate();
        TrayLog.WindowShown(_logger);
    }

    private void OnMainFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_isExiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            _mainForm?.Hide();
            TrayLog.WindowHidden(_logger);
        }
    }

    private void ExitApplication()
    {
        _isExiting = true;
        _mainForm?.Close();
        ExitThread();
    }
}

/// <summary>
///  Shows the user how to restore the application from its tray icon.
/// </summary>
internal sealed class TrayMainForm : Form
{
    /// <summary>
    ///  Initializes the tray application's main window.
    /// </summary>
    public TrayMainForm()
    {
        Text = "WinForms Application Builder - System Tray";
        ClientSize = new Size(520, 160);

        Label description = new()
        {
            AutoSize = true,
            Location = new Point(16, 20),
            Text = "Closing this window hides it; use the tray menu to exit."
        };

        Button hideButton = new()
        {
            Location = new Point(16, 64),
            Text = "Hide to notification area"
        };
        hideButton.Click += (_, _) => Hide();

        Controls.AddRange([description, hideButton]);
    }
}
