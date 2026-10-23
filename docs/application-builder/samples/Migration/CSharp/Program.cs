// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Extensions.WinForms;

namespace ApplicationBuilderMigrationSample.CSharp;

/// <summary>
///  Demonstrates shared Application Builder migration behavior in C#.
/// </summary>
internal static class Program
{
    private const string ApplicationId = "WinFormsApplicationBuilder.MigrationSample.CSharp";

    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        MainForm mainForm = new(args);
        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder(args)
            .UseSingleInstance(ApplicationId)
            .UseSplashScreen<SplashForm>(TimeSpan.FromMilliseconds(750))
            .UseStartupForm(mainForm);

        using WinFormsApplication application = builder.Build();
        application.Lifetime.ApplicationStarting +=
            (_, _) => Debug.WriteLine("ApplicationStarting");
        application.Lifetime.ApplicationStarted +=
            (_, _) => Debug.WriteLine("ApplicationStarted");
        application.Lifetime.ApplicationStopping +=
            (_, _) => Debug.WriteLine("ApplicationStopping");
        application.Lifetime.ApplicationStopped +=
            (_, _) => Debug.WriteLine("ApplicationStopped");
        application.ActivationRequested +=
            (_, eventArgs) => mainForm.ShowActivation(eventArgs.Arguments);

        application.Run();
    }
}

/// <summary>
///  Displays startup and forwarded activation arguments.
/// </summary>
internal sealed class MainForm : Form
{
    private readonly Label _activationLabel = new()
    {
        AutoSize = true,
        Location = new Point(16, 52),
        Text = "No startup arguments supplied."
    };

    public MainForm(string[] arguments)
    {
        Text = "Application Builder migration sample (C#)";
        ClientSize = new Size(520, 170);
        StartPosition = FormStartPosition.CenterScreen;

        Label description = new()
        {
            AutoSize = true,
            Location = new Point(16, 16),
            Text = "Close this window to observe coordinated shutdown."
        };

        Button closeButton = new()
        {
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            Location = new Point(420, 120),
            Text = "Close"
        };
        closeButton.Click += (_, _) => Close();

        Controls.Add(description);
        Controls.Add(_activationLabel);
        Controls.Add(closeButton);

        if (arguments.Length > 0)
        {
            _activationLabel.Text = $"Startup arguments: {string.Join(", ", arguments)}";
        }
    }

    internal void ShowActivation(IReadOnlyList<string> arguments)
    {
        _activationLabel.Text = $"Forwarded arguments: {string.Join(", ", arguments)}";
    }
}

/// <summary>
///  Provides a small splash form for the migration sample.
/// </summary>
internal sealed class SplashForm : Form
{
    public SplashForm()
    {
        Text = "Starting";
        ClientSize = new Size(320, 100);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;

        Controls.Add(new Label
        {
            AutoSize = true,
            Location = new Point(24, 34),
            Text = "Starting the migrated application..."
        });
    }
}
