// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Text.Json.Nodes;
using System.Windows.Forms;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.WinForms;

namespace ApplicationBuilderSample.Infrastructure.CSharp;

/// <summary>
///  Defines structured log messages used by the sample.
/// </summary>
internal static class SampleLog
{
    private static readonly Action<ILogger, string, int, Exception?> s_backgroundStatus =
        LoggerMessage.Define<string, int>(
            LogLevel.Information,
            new EventId(1, "BackgroundStatus"),
            "Background status for {DisplayName}; refresh interval is {RefreshIntervalSeconds} seconds.");
    private static readonly Action<ILogger, Exception?> s_workerStopped =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(2, "WorkerStopped"),
            "Background status worker observed graceful shutdown.");
    private static readonly Action<ILogger, int, Exception?> s_settingsSaved =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(3, "SettingsSaved"),
            "Saved user preferences at settings schema version {SettingsSchemaVersion}.");
    private static readonly Action<ILogger, Exception?> s_settingsSaveFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(4, "SettingsSaveFailed"),
            "Could not save user preferences.");
    private static readonly Action<ILogger, string, Exception?> s_healthChecksCompleted =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(5, "HealthChecksCompleted"),
            "Health checks completed with status {HealthStatus}.");

    /// <summary>
    ///  Logs current configuration from the background worker.
    /// </summary>
    internal static void BackgroundStatus(ILogger logger, string displayName, int intervalSeconds)
        => s_backgroundStatus(logger, displayName, intervalSeconds, null);

    /// <summary>
    ///  Logs background-worker shutdown.
    /// </summary>
    internal static void WorkerStopped(ILogger logger)
        => s_workerStopped(logger, null);

    /// <summary>
    ///  Logs a successful user-settings save.
    /// </summary>
    internal static void SettingsSaved(ILogger logger, int schemaVersion)
        => s_settingsSaved(logger, schemaVersion, null);

    /// <summary>
    ///  Logs a failed user-settings save.
    /// </summary>
    internal static void SettingsSaveFailed(ILogger logger, Exception exception)
        => s_settingsSaveFailed(logger, exception);

    /// <summary>
    ///  Logs the result of a health-check run.
    /// </summary>
    internal static void HealthChecksCompleted(ILogger logger, string healthStatus)
        => s_healthChecksCompleted(logger, healthStatus, null);
}

/// <summary>
///  Starts the infrastructure sample with the Generic Host.
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
        builder.Logging.SetMinimumLevel(LogLevel.Information);
        builder.EnableExceptionLogging();

        builder.Services
            .AddOptions<SampleOptions>()
            .Bind(builder.Configuration.GetSection(SampleOptions.SectionName))
            .Validate(
                static options => !string.IsNullOrWhiteSpace(options.DisplayName),
                "Sample:DisplayName must not be empty.")
            .Validate(
                static options => options.RefreshIntervalSeconds is >= 2 and <= 60,
                "Sample:RefreshIntervalSeconds must be between 2 and 60.")
            .ValidateOnStart();

        builder.Services
            .AddOptions<UserPreferences>()
            .Bind(builder.Configuration.GetSection("UserSettings"));

        UserSettingsOptions settingsOptions = new()
        {
            ApplicationId = "WinFormsApplicationBuilder.InfrastructureSample",
            SchemaVersion = 2
        };
        settingsOptions.Migrations.Add(new PreferencesV1Migration());
        builder.AddUserSettings(
            settingsOptions,
            includeInConfiguration: true,
            nameof(UserPreferences.Theme),
            nameof(UserPreferences.ShowTips));

        builder.Services.AddHostedService<StatusWorker>();
        builder.Services
            .AddHealthChecks()
            .AddCheck<SettingsHealthCheck>(
                "user-settings",
                failureStatus: HealthStatus.Degraded);

        string environmentName = builder.Environment.EnvironmentName;
        builder.UseStartupForm(services => new MainForm(
            services.GetRequiredService<IOptionsMonitor<SampleOptions>>(),
            services.GetRequiredService<IOptionsMonitor<UserPreferences>>(),
            services.GetRequiredService<IUserSettingsService>(),
            services.GetRequiredService<HealthCheckService>(),
            services.GetRequiredService<ILogger<MainForm>>(),
            environmentName));

        using WinFormsApplication application = builder.Build();
        application.Run();
    }
}

/// <summary>
///  Defines validated application configuration.
/// </summary>
internal sealed class SampleOptions
{
    /// <summary>
    ///  Gets the configuration section name.
    /// </summary>
    internal const string SectionName = "Sample";

    /// <summary>
    ///  Gets or sets the application display name.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    ///  Gets or sets the background worker interval in seconds.
    /// </summary>
    public int RefreshIntervalSeconds { get; set; }

    /// <summary>
    ///  Gets or sets a Development-only marker used to demonstrate User Secrets.
    /// </summary>
    public string? DiagnosticToken { get; set; }
}

/// <summary>
///  Stores preferences for the current Windows user.
/// </summary>
internal sealed class UserPreferences
{
    /// <summary>
    ///  Gets or sets the preferred theme.
    /// </summary>
    public string Theme { get; set; } = "System";

    /// <summary>
    ///  Gets or sets whether startup tips are enabled.
    /// </summary>
    public bool ShowTips { get; set; } = true;
}

/// <summary>
///  Renames the theme preference from settings schema version one.
/// </summary>
internal sealed class PreferencesV1Migration : IUserSettingsMigration
{
    /// <inheritdoc/>
    public int FromVersion => 1;

    /// <inheritdoc/>
    public JsonObject Migrate(JsonObject settings)
    {
        if (settings.TryGetPropertyValue("themeName", out JsonNode? themeName))
        {
            settings["theme"] = themeName?.DeepClone();
            settings.Remove("themeName");
        }

        if (!settings.ContainsKey("showTips"))
        {
            settings["showTips"] = true;
        }

        return settings;
    }
}

/// <summary>
///  Periodically reports current configuration using structured logging.
/// </summary>
internal sealed class StatusWorker(
    IOptionsMonitor<SampleOptions> options,
    ILogger<StatusWorker> logger) : BackgroundService
{
    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                SampleOptions currentOptions = options.CurrentValue;
                SampleLog.BackgroundStatus(
                    logger,
                    currentOptions.DisplayName,
                    currentOptions.RefreshIntervalSeconds);

                await Task.Delay(
                    TimeSpan.FromSeconds(currentOptions.RefreshIntervalSeconds),
                    stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            SampleLog.WorkerStopped(logger);
        }
    }
}

/// <summary>
///  Reports whether the current user's settings store can be read.
/// </summary>
internal sealed class SettingsHealthCheck(IUserSettingsService settingsService) : IHealthCheck
{
    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        int? storedVersion = await settingsService
            .GetStoredSchemaVersionAsync(cancellationToken)
            .ConfigureAwait(false);
        string description = storedVersion is int version
            ? $"User settings schema version {version} is readable."
            : "No user settings file exists yet; defaults are available.";

        return HealthCheckResult.Healthy(description);
    }
}

/// <summary>
///  Displays configuration, health, and per-user preference operations.
/// </summary>
internal sealed class MainForm : Form
{
    private readonly IOptionsMonitor<SampleOptions> _sampleOptions;
    private readonly IOptionsMonitor<UserPreferences> _userPreferences;
    private readonly IUserSettingsService _settingsService;
    private readonly HealthCheckService _healthCheckService;
    private readonly ILogger<MainForm> _logger;
    private readonly IDisposable? _sampleOptionsSubscription;
    private readonly IDisposable? _preferencesSubscription;
    private readonly Label _optionsLabel = new();
    private readonly Label _preferencesLabel = new();
    private readonly Label _healthLabel = new();
    private readonly Label _statusLabel = new();
    private readonly ComboBox _themeSelector = new();
    private readonly CheckBox _showTipsCheckBox = new();

    /// <summary>
    ///  Initializes the reference sample window.
    /// </summary>
    public MainForm(
        IOptionsMonitor<SampleOptions> sampleOptions,
        IOptionsMonitor<UserPreferences> userPreferences,
        IUserSettingsService settingsService,
        HealthCheckService healthCheckService,
        ILogger<MainForm> logger,
        string environmentName)
    {
        _sampleOptions = sampleOptions;
        _userPreferences = userPreferences;
        _settingsService = settingsService;
        _healthCheckService = healthCheckService;
        _logger = logger;

        Text = "WinForms Application Builder - Infrastructure";
        ClientSize = new Size(760, 330);

        Label environmentLabel = new()
        {
            AutoSize = true,
            Location = new Point(16, 16),
            Text = $"Environment: {environmentName}"
        };

        _optionsLabel.AutoSize = true;
        _optionsLabel.Location = new Point(16, 48);

        _preferencesLabel.AutoSize = true;
        _preferencesLabel.Location = new Point(16, 80);

        Label themeLabel = new()
        {
            AutoSize = true,
            Location = new Point(16, 122),
            Text = "Theme:"
        };

        _themeSelector.DropDownStyle = ComboBoxStyle.DropDownList;
        _themeSelector.Items.AddRange(["System", "Light", "Dark"]);
        _themeSelector.Location = new Point(72, 118);
        _themeSelector.Width = 120;
        _themeSelector.SelectedItem = "System";

        _showTipsCheckBox.AutoSize = true;
        _showTipsCheckBox.Location = new Point(220, 122);
        _showTipsCheckBox.Text = "Show startup tips";
        _showTipsCheckBox.Checked = true;

        Button saveButton = new()
        {
            Location = new Point(16, 164),
            Text = "Save user settings",
            Width = 150
        };
        saveButton.Click += SaveSettings;

        Button healthButton = new()
        {
            Location = new Point(180, 164),
            Text = "Run health checks",
            Width = 150
        };
        healthButton.Click += RunHealthChecks;

        _healthLabel.AutoSize = true;
        _healthLabel.Location = new Point(16, 210);
        _healthLabel.Text = "Health checks have not run.";

        _statusLabel.AutoSize = true;
        _statusLabel.Location = new Point(16, 244);

        Controls.AddRange([
            environmentLabel,
            _optionsLabel,
            _preferencesLabel,
            themeLabel,
            _themeSelector,
            _showTipsCheckBox,
            saveButton,
            healthButton,
            _healthLabel,
            _statusLabel
        ]);

        _sampleOptionsSubscription = _sampleOptions.OnChange((_, _) => UpdateOptionsDisplay());
        _preferencesSubscription = _userPreferences.OnChange((_, _) => UpdateOptionsDisplay());
        Shown += (_, _) => UpdateOptionsDisplay();
        UpdateOptionsDisplay();
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _sampleOptionsSubscription?.Dispose();
            _preferencesSubscription?.Dispose();
        }

        base.Dispose(disposing);
    }

    private async void SaveSettings(object? sender, EventArgs e)
    {
        UserPreferences preferences = new()
        {
            Theme = _themeSelector.SelectedItem?.ToString() ?? "System",
            ShowTips = _showTipsCheckBox.Checked
        };

        try
        {
            await _settingsService.SaveAsync(preferences).ConfigureAwait(true);
            _statusLabel.Text = "Settings saved for this Windows user.";
            SampleLog.SettingsSaved(_logger, _settingsService.SchemaVersion);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or TimeoutException
                or InvalidOperationException)
        {
            SampleLog.SettingsSaveFailed(_logger, exception);
            _statusLabel.Text = $"Settings were not saved: {exception.Message}";
        }
    }

    private async void RunHealthChecks(object? sender, EventArgs e)
    {
        HealthReport report = await _healthCheckService.CheckHealthAsync().ConfigureAwait(true);
        string details = string.Join(
            "; ",
            report.Entries.Select(entry =>
                $"{entry.Key}: {entry.Value.Status} ({entry.Value.Description})"));
        _healthLabel.Text = $"Health: {report.Status}. {details}";
        SampleLog.HealthChecksCompleted(_logger, report.Status.ToString());
    }

    private void UpdateOptionsDisplay()
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(new Action(UpdateOptionsDisplay));
            return;
        }

        SampleOptions sample = _sampleOptions.CurrentValue;
        UserPreferences preferences = _userPreferences.CurrentValue;
        _optionsLabel.Text =
            $"Configuration: {sample.DisplayName}; worker interval: {sample.RefreshIntervalSeconds}s; " +
            $"Development secret configured: {!string.IsNullOrWhiteSpace(sample.DiagnosticToken)}";
        _preferencesLabel.Text =
            $"Current per-user settings: theme {preferences.Theme}; startup tips {preferences.ShowTips}";

        if (_themeSelector.Items.Contains(preferences.Theme))
        {
            _themeSelector.SelectedItem = preferences.Theme;
        }

        _showTipsCheckBox.Checked = preferences.ShowTips;
    }
}
