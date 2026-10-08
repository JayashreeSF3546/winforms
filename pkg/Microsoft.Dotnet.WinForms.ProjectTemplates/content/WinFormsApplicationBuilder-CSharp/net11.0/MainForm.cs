using System.IO;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.WinForms;

namespace WinFormsApplicationBuilderApp;

/// <summary>
///  The startup form for the Application Builder sample.
/// </summary>
public partial class MainForm : Form
{
    private readonly GreetingService? _greetingService;
    private readonly IUserSettingsService? _settingsService;
    private readonly ILogger<MainForm>? _logger;

    /// <summary>
    ///  Initializes a new instance for the Windows Forms Designer.
    /// </summary>
    public MainForm()
    {
        InitializeComponent();
    }

    /// <summary>
    ///  Initializes a new instance using application services.
    /// </summary>
    /// <param name="greetingService">Creates the configured greeting.</param>
    /// <param name="settingsService">Loads and saves per-user preferences.</param>
    /// <param name="logger">Logs form activity.</param>
    /// <param name="options">The validated greeting options.</param>
    public MainForm(
        GreetingService greetingService,
        IUserSettingsService settingsService,
        ILogger<MainForm> logger,
        IOptions<GreetingOptions> options)
        : this()
    {
        _greetingService = greetingService;
        _settingsService = settingsService;
        _logger = logger;
        _greetingLabel.Text = $"{options.Value.Prefix}, developer!";
    }

    private async void MainForm_Load(object? sender, EventArgs e)
    {
        if (_settingsService is null)
        {
            return;
        }

        try
        {
            UserPreferences preferences = await _settingsService
                .LoadAsync(new UserPreferences());
            _nameTextBox.Text = preferences.DisplayName;
            _statusLabel.Text = "User preferences loaded.";
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or TimeoutException
                or InvalidOperationException)
        {
            _logger?.LogError("Could not load user preferences.");
            _statusLabel.Text = "Preferences could not be loaded. See Debug output.";
        }
    }

    private void GreetButton_Click(object? sender, EventArgs e)
    {
        if (_greetingService is null)
        {
            return;
        }

        _greetingLabel.Text = _greetingService.CreateGreeting(_nameTextBox.Text);
        _logger?.LogInformation("Displayed the configured greeting.");
    }

    private async void SaveButton_Click(object? sender, EventArgs e)
    {
        if (_settingsService is null)
        {
            return;
        }

        try
        {
            await _settingsService.SaveAsync(new UserPreferences
            {
                DisplayName = _nameTextBox.Text
            });
            _statusLabel.Text = "User preferences saved.";
            _logger?.LogInformation("Saved user preferences.");
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or TimeoutException
                or InvalidOperationException)
        {
            _logger?.LogError("Could not save user preferences.");
            _statusLabel.Text = "Preferences could not be saved. See Debug output.";
        }
    }
}
