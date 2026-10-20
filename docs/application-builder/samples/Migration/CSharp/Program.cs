// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Windows.Forms;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.WinForms;

namespace ApplicationBuilderSample.Migration.CSharp;

/// <summary>
///  Defines the structured startup log message for the migration sample.
/// </summary>
internal static class MigrationLog
{
    private static readonly Action<ILogger, Exception?> s_started =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(1, "ApplicationStarted"),
            "The migrated WinForms application started.");

    /// <summary>
    ///  Logs that the migrated application is ready.
    /// </summary>
    internal static void Started(ILogger logger)
        => s_started(logger, null);
}

/// <summary>
///  Starts an existing WinForms UI through the Application Builder.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder(args);
        builder.Services
            .AddOptions<MigrationOptions>()
            .Bind(builder.Configuration.GetSection("LegacyApplication"))
            .Validate(
                static options => !string.IsNullOrWhiteSpace(options.DisplayName),
                "LegacyApplication:DisplayName must not be empty.")
            .ValidateOnStart();

        builder.Services.AddTransient<MainForm>();
        builder.UseStartupForm<MainForm>(
            static services => services.GetRequiredService<MainForm>());

        using WinFormsApplication application = builder.Build();
        application.Run();
    }
}

/// <summary>
///  Holds the small set of settings retained from the existing application.
/// </summary>
internal sealed class MigrationOptions
{
    /// <summary>
    ///  Gets or sets the window title.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;
}

/// <summary>
///  Represents an existing Form whose UI can remain in place during migration.
/// </summary>
internal sealed class MainForm : Form
{
    /// <summary>
    ///  Initializes the existing window with host-provided configuration and logging.
    /// </summary>
    public MainForm(IOptions<MigrationOptions> options, ILogger<MainForm> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        Text = options.Value.DisplayName;
        ClientSize = new Size(520, 180);

        Label description = new()
        {
            AutoSize = true,
            Location = new Point(16, 20),
            Text = "The existing Form and controls stay in place."
        };

        Label migrationStep = new()
        {
            AutoSize = true,
            Location = new Point(16, 52),
            Text = "Startup, configuration, and logging now come from one host."
        };

        Button closeButton = new()
        {
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            Location = new Point(410, 120),
            Text = "Close"
        };
        closeButton.Click += (_, _) => Close();

        Controls.AddRange([description, migrationStep, closeButton]);
        Shown += (_, _) => MigrationLog.Started(logger);
    }
}
