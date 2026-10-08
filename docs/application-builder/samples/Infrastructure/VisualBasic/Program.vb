' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Imports System.Drawing
Imports System.IO
Imports System.Text.Json.Nodes
Imports System.Threading
Imports System.Windows.Forms
Imports Microsoft.Extensions.Configuration
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.Diagnostics.HealthChecks
Imports Microsoft.Extensions.Hosting
Imports Microsoft.Extensions.Logging
Imports Microsoft.Extensions.Options
Imports Microsoft.Extensions.WinForms

Namespace Infrastructure.VisualBasic
    ''' <summary>
    '''  Defines structured log messages used by the sample.
    ''' </summary>
    Friend NotInheritable Class SampleLog
        Private Shared ReadOnly s_backgroundStatus As Action(Of ILogger, String, Integer, Exception) =
            LoggerMessage.Define(Of String, Integer)(
                LogLevel.Information,
                New EventId(1, "BackgroundStatus"),
                "Background status for {DisplayName}; refresh interval is {RefreshIntervalSeconds} seconds.")
        Private Shared ReadOnly s_workerStopped As Action(Of ILogger, Exception) =
            LoggerMessage.Define(
                LogLevel.Information,
                New EventId(2, "WorkerStopped"),
                "Background status worker observed graceful shutdown.")
        Private Shared ReadOnly s_settingsSaved As Action(Of ILogger, Integer, Exception) =
            LoggerMessage.Define(Of Integer)(
                LogLevel.Information,
                New EventId(3, "SettingsSaved"),
                "Saved user preferences at settings schema version {SettingsSchemaVersion}.")
        Private Shared ReadOnly s_settingsSaveFailed As Action(Of ILogger, Exception) =
            LoggerMessage.Define(
                LogLevel.Error,
                New EventId(4, "SettingsSaveFailed"),
                "Could not save user preferences.")
        Private Shared ReadOnly s_healthChecksCompleted As Action(Of ILogger, String, Exception) =
            LoggerMessage.Define(Of String)(
                LogLevel.Information,
                New EventId(5, "HealthChecksCompleted"),
                "Health checks completed with status {HealthStatus}.")

        Friend Shared Sub BackgroundStatus(logger As ILogger, displayName As String, intervalSeconds As Integer)
            s_backgroundStatus(logger, displayName, intervalSeconds, Nothing)
        End Sub

        Friend Shared Sub WorkerStopped(logger As ILogger)
            s_workerStopped(logger, Nothing)
        End Sub

        Friend Shared Sub SettingsSaved(logger As ILogger, schemaVersion As Integer)
            s_settingsSaved(logger, schemaVersion, Nothing)
        End Sub

        Friend Shared Sub SettingsSaveFailed(logger As ILogger, exception As Exception)
            s_settingsSaveFailed(logger, exception)
        End Sub

        Friend Shared Sub HealthChecksCompleted(logger As ILogger, healthStatus As String)
            s_healthChecksCompleted(logger, healthStatus, Nothing)
        End Sub
    End Class

    ''' <summary>
    '''  Starts the infrastructure sample with the Generic Host.
    ''' </summary>
    Friend NotInheritable Class Program
        <STAThread>
        Friend Shared Sub Main(args As String())
            Application.SetHighDpiMode(HighDpiMode.SystemAware)
            Application.EnableVisualStyles()
            Application.SetCompatibleTextRenderingDefault(False)

            Dim builder As WinFormsApplicationBuilder = WinFormsApplication.CreateBuilder(args)
            builder.Logging.SetMinimumLevel(LogLevel.Information)
            builder.EnableExceptionLogging()

            builder.Services.
                AddOptions(Of SampleOptions)().
                Bind(builder.Configuration.GetSection(SampleOptions.SectionName)).
                Validate(
                    Function(options) Not String.IsNullOrWhiteSpace(options.DisplayName),
                    "Sample:DisplayName must not be empty.").
                Validate(
                    Function(options) options.RefreshIntervalSeconds >= 2 AndAlso options.RefreshIntervalSeconds <= 60,
                    "Sample:RefreshIntervalSeconds must be between 2 and 60.").
                ValidateOnStart()

            builder.Services.
                AddOptions(Of UserPreferences)().
                Bind(builder.Configuration.GetSection("UserSettings"))

            Dim settingsOptions As New UserSettingsOptions With {
                .ApplicationId = "WinFormsApplicationBuilder.InfrastructureSample.VisualBasic",
                .SchemaVersion = 2
            }
            settingsOptions.Migrations.Add(New PreferencesV1Migration())
            builder.AddUserSettings(
                settingsOptions,
                includeInConfiguration:=True,
                NameOf(UserPreferences.Theme),
                NameOf(UserPreferences.ShowTips))

            builder.Services.AddHostedService(Of StatusWorker)()
            builder.Services.
                AddHealthChecks().
                AddCheck(Of SettingsHealthCheck)(
                    "user-settings",
                    failureStatus:=HealthStatus.Degraded)

            Dim environmentName As String = builder.Environment.EnvironmentName
            builder.UseStartupForm(
                Function(services) New MainForm(
                    services.GetRequiredService(Of IOptionsMonitor(Of SampleOptions))(),
                    services.GetRequiredService(Of IOptionsMonitor(Of UserPreferences))(),
                    services.GetRequiredService(Of IUserSettingsService)(),
                    services.GetRequiredService(Of HealthCheckService)(),
                    services.GetRequiredService(Of ILogger(Of MainForm))(),
                    environmentName))

            Using application As WinFormsApplication = builder.Build()
                application.Run()
            End Using
        End Sub
    End Class

    ''' <summary>
    '''  Defines validated application configuration.
    ''' </summary>
    Friend NotInheritable Class SampleOptions
        Friend Const SectionName As String = "Sample"

        Public Property DisplayName As String = String.Empty

        Public Property RefreshIntervalSeconds As Integer

        Public Property DiagnosticToken As String
    End Class

    ''' <summary>
    '''  Stores preferences for the current Windows user.
    ''' </summary>
    Friend NotInheritable Class UserPreferences
        Public Property Theme As String = "System"

        Public Property ShowTips As Boolean = True
    End Class

    ''' <summary>
    '''  Renames the theme preference from settings schema version one.
    ''' </summary>
    Friend NotInheritable Class PreferencesV1Migration
        Implements IUserSettingsMigration

        Public ReadOnly Property FromVersion As Integer Implements IUserSettingsMigration.FromVersion
            Get
                Return 1
            End Get
        End Property

        Public Function Migrate(settings As JsonObject) As JsonObject Implements IUserSettingsMigration.Migrate
            Dim themeName As JsonNode = Nothing

            If settings.TryGetPropertyValue("themeName", themeName) Then
                settings("theme") = themeName?.DeepClone()
                settings.Remove("themeName")
            End If

            If Not settings.ContainsKey("showTips") Then
                settings("showTips") = True
            End If

            Return settings
        End Function
    End Class

    ''' <summary>
    '''  Periodically reports current configuration using structured logging.
    ''' </summary>
    Friend NotInheritable Class StatusWorker
        Inherits BackgroundService

        Private ReadOnly _options As IOptionsMonitor(Of SampleOptions)
        Private ReadOnly _logger As ILogger(Of StatusWorker)

        Public Sub New(options As IOptionsMonitor(Of SampleOptions), logger As ILogger(Of StatusWorker))
            _options = options
            _logger = logger
        End Sub

        Protected Overrides Async Function ExecuteAsync(stoppingToken As CancellationToken) As Task
            Try
                While Not stoppingToken.IsCancellationRequested
                    Dim currentOptions As SampleOptions = _options.CurrentValue
                    SampleLog.BackgroundStatus(
                        _logger,
                        currentOptions.DisplayName,
                        currentOptions.RefreshIntervalSeconds)

                    Await Task.Delay(
                        TimeSpan.FromSeconds(currentOptions.RefreshIntervalSeconds),
                        stoppingToken).ConfigureAwait(False)
                End While
            Catch ex As OperationCanceledException When stoppingToken.IsCancellationRequested
                SampleLog.WorkerStopped(_logger)
            End Try
        End Function
    End Class

    ''' <summary>
    '''  Reports whether the current user's settings store can be read.
    ''' </summary>
    Friend NotInheritable Class SettingsHealthCheck
        Implements IHealthCheck

        Private ReadOnly _settingsService As IUserSettingsService

        Public Sub New(settingsService As IUserSettingsService)
            _settingsService = settingsService
        End Sub

        Public Async Function CheckHealthAsync(
            context As HealthCheckContext,
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of HealthCheckResult) _
            Implements IHealthCheck.CheckHealthAsync

            Dim storedVersion As Integer? =
                Await _settingsService.GetStoredSchemaVersionAsync(cancellationToken).ConfigureAwait(False)
            Dim description As String =
                If(
                    storedVersion.HasValue,
                    $"User settings schema version {storedVersion.Value} is readable.",
                    "No user settings file exists yet; defaults are available.")

            Return HealthCheckResult.Healthy(description)
        End Function
    End Class

    ''' <summary>
    '''  Displays configuration, health, and per-user preference operations.
    ''' </summary>
    Friend NotInheritable Class MainForm
        Inherits Form

        Private ReadOnly _sampleOptions As IOptionsMonitor(Of SampleOptions)
        Private ReadOnly _userPreferences As IOptionsMonitor(Of UserPreferences)
        Private ReadOnly _settingsService As IUserSettingsService
        Private ReadOnly _healthCheckService As HealthCheckService
        Private ReadOnly _logger As ILogger(Of MainForm)
        Private ReadOnly _sampleOptionsSubscription As IDisposable
        Private ReadOnly _preferencesSubscription As IDisposable
        Private ReadOnly _optionsLabel As New Label()
        Private ReadOnly _preferencesLabel As New Label()
        Private ReadOnly _healthLabel As New Label()
        Private ReadOnly _statusLabel As New Label()
        Private ReadOnly _themeSelector As New ComboBox()
        Private ReadOnly _showTipsCheckBox As New CheckBox()

        Public Sub New(
            sampleOptions As IOptionsMonitor(Of SampleOptions),
            userPreferences As IOptionsMonitor(Of UserPreferences),
            settingsService As IUserSettingsService,
            healthCheckService As HealthCheckService,
            logger As ILogger(Of MainForm),
            environmentName As String)

            _sampleOptions = sampleOptions
            _userPreferences = userPreferences
            _settingsService = settingsService
            _healthCheckService = healthCheckService
            _logger = logger

            Text = "WinForms Application Builder - Infrastructure"
            ClientSize = New Size(760, 330)

            Dim environmentLabel As New Label With {
                .AutoSize = True,
                .Location = New Point(16, 16),
                .Text = $"Environment: {environmentName}"
            }

            _optionsLabel.AutoSize = True
            _optionsLabel.Location = New Point(16, 48)

            _preferencesLabel.AutoSize = True
            _preferencesLabel.Location = New Point(16, 80)

            Dim themeLabel As New Label With {
                .AutoSize = True,
                .Location = New Point(16, 122),
                .Text = "Theme:"
            }

            _themeSelector.DropDownStyle = ComboBoxStyle.DropDownList
            _themeSelector.Items.AddRange({"System", "Light", "Dark"})
            _themeSelector.Location = New Point(72, 118)
            _themeSelector.Width = 120
            _themeSelector.SelectedItem = "System"

            _showTipsCheckBox.AutoSize = True
            _showTipsCheckBox.Location = New Point(220, 122)
            _showTipsCheckBox.Text = "Show startup tips"
            _showTipsCheckBox.Checked = True

            Dim saveButton As New Button With {
                .Location = New Point(16, 164),
                .Text = "Save user settings",
                .Width = 150
            }
            AddHandler saveButton.Click, AddressOf SaveSettingsAsync

            Dim healthButton As New Button With {
                .Location = New Point(180, 164),
                .Text = "Run health checks",
                .Width = 150
            }
            AddHandler healthButton.Click, AddressOf RunHealthChecksAsync

            _healthLabel.AutoSize = True
            _healthLabel.Location = New Point(16, 210)
            _healthLabel.Text = "Health checks have not run."

            _statusLabel.AutoSize = True
            _statusLabel.Location = New Point(16, 244)

            Controls.AddRange({
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
            })

            _sampleOptionsSubscription = _sampleOptions.OnChange(
                Sub(options, name) UpdateOptionsDisplay())
            _preferencesSubscription = _userPreferences.OnChange(
                Sub(options, name) UpdateOptionsDisplay())
            AddHandler Shown, Sub(sender, e) UpdateOptionsDisplay()
            UpdateOptionsDisplay()
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then
                _sampleOptionsSubscription.Dispose()
                _preferencesSubscription.Dispose()
            End If

            MyBase.Dispose(disposing)
        End Sub

        Private Async Sub SaveSettingsAsync(sender As Object, e As EventArgs)
            Dim preferences As New UserPreferences With {
                .Theme = If(_themeSelector.SelectedItem?.ToString(), "System"),
                .ShowTips = _showTipsCheckBox.Checked
            }

            Try
                Await _settingsService.SaveAsync(preferences).ConfigureAwait(True)
                _statusLabel.Text = "Settings saved for this Windows user."
                SampleLog.SettingsSaved(_logger, _settingsService.SchemaVersion)
            Catch ex As Exception When TypeOf ex Is IOException OrElse _
                TypeOf ex Is UnauthorizedAccessException OrElse _
                TypeOf ex Is TimeoutException OrElse _
                TypeOf ex Is InvalidOperationException

                SampleLog.SettingsSaveFailed(_logger, ex)
                _statusLabel.Text = $"Settings were not saved: {ex.Message}"
            End Try
        End Sub

        Private Async Sub RunHealthChecksAsync(sender As Object, e As EventArgs)
            Dim report As HealthReport =
                Await _healthCheckService.CheckHealthAsync().ConfigureAwait(True)
            Dim details As String = String.Join(
                "; ",
                report.Entries.Select(
                    Function(entry) $"{entry.Key}: {entry.Value.Status} ({entry.Value.Description})"))
            _healthLabel.Text = $"Health: {report.Status}. {details}"
            SampleLog.HealthChecksCompleted(_logger, report.Status.ToString())
        End Sub

        Private Sub UpdateOptionsDisplay()
            If IsDisposed OrElse Not IsHandleCreated Then
                Return
            End If

            If InvokeRequired Then
                BeginInvoke(New MethodInvoker(AddressOf UpdateOptionsDisplay))
                Return
            End If

            Dim sample As SampleOptions = _sampleOptions.CurrentValue
            Dim preferences As UserPreferences = _userPreferences.CurrentValue
            _optionsLabel.Text =
                $"Configuration: {sample.DisplayName}; worker interval: {sample.RefreshIntervalSeconds}s; " &
                $"Development secret configured: {Not String.IsNullOrWhiteSpace(sample.DiagnosticToken)}"
            _preferencesLabel.Text =
                $"Current per-user settings: theme {preferences.Theme}; startup tips {preferences.ShowTips}"

            If _themeSelector.Items.Contains(preferences.Theme) Then
                _themeSelector.SelectedItem = preferences.Theme
            End If

            _showTipsCheckBox.Checked = preferences.ShowTips
        End Sub
    End Class
End Namespace
