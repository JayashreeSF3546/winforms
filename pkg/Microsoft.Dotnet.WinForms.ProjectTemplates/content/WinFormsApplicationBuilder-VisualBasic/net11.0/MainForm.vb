Imports System.IO
Imports System.Windows.Forms
Imports Microsoft.Extensions.Logging
Imports Microsoft.Extensions.Options
Imports Microsoft.Extensions.WinForms

Namespace WinFormsApplicationBuilderApp
    ''' <summary>
    '''  The startup form for the Application Builder sample.
    ''' </summary>
    Public Partial Class MainForm
        Private ReadOnly _greetingService As GreetingService
        Private ReadOnly _settingsService As IUserSettingsService
        Private ReadOnly _logger As ILogger(Of MainForm)
        Private ReadOnly _options As IOptions(Of GreetingOptions)

        ''' <summary>
        '''  Initializes a new instance for the Windows Forms Designer.
        ''' </summary>
        Public Sub New()
            InitializeComponent()
        End Sub

        ''' <summary>
        '''  Initializes a new instance using application services.
        ''' </summary>
        ''' <param name="greetingService">Creates the configured greeting.</param>
        ''' <param name="settingsService">Loads and saves per-user preferences.</param>
        ''' <param name="logger">Logs form activity.</param>
        ''' <param name="options">The validated greeting options.</param>
        Public Sub New(
            greetingService As GreetingService,
            settingsService As IUserSettingsService,
            logger As ILogger(Of MainForm),
            options As IOptions(Of GreetingOptions))
            Me.New()

            _greetingService = greetingService
            _settingsService = settingsService
            _logger = logger
            _options = options
            greetingLabel.Text = $"{_options.Value.Prefix}, developer!"
        End Sub

        Private Async Sub MainForm_Load(sender As Object, e As EventArgs) Handles MyBase.Load
            If _settingsService Is Nothing Then
                Return
            End If

            Try
                Dim preferences As UserPreferences =
                    Await _settingsService.LoadAsync(New UserPreferences())
                nameTextBox.Text = preferences.DisplayName
                statusLabel.Text = "User preferences loaded."
            Catch exception As Exception When TypeOf exception Is IOException OrElse _
                TypeOf exception Is UnauthorizedAccessException OrElse _
                TypeOf exception Is InvalidDataException OrElse _
                TypeOf exception Is TimeoutException OrElse _
                TypeOf exception Is InvalidOperationException
                _logger?.LogError("Could not load user preferences.")
                statusLabel.Text = "Preferences could not be loaded. See Debug output."
            End Try
        End Sub

        Private Sub GreetButton_Click(sender As Object, e As EventArgs) Handles greetButton.Click
            If _greetingService Is Nothing Then
                Return
            End If

            greetingLabel.Text = _greetingService.CreateGreeting(nameTextBox.Text)
            _logger?.LogInformation("Displayed the configured greeting.")
        End Sub

        Private Async Sub SaveButton_Click(sender As Object, e As EventArgs) Handles saveButton.Click
            If _settingsService Is Nothing Then
                Return
            End If

            Try
                Await _settingsService.SaveAsync(
                    New UserPreferences With {
                        .DisplayName = nameTextBox.Text
                    })
                statusLabel.Text = "User preferences saved."
                _logger?.LogInformation("Saved user preferences.")
            Catch exception As Exception When TypeOf exception Is IOException OrElse _
                TypeOf exception Is UnauthorizedAccessException OrElse _
                TypeOf exception Is InvalidDataException OrElse _
                TypeOf exception Is TimeoutException OrElse _
                TypeOf exception Is InvalidOperationException
                _logger?.LogError("Could not save user preferences.")
                statusLabel.Text = "Preferences could not be saved. See Debug output."
            End Try
        End Sub
    End Class
End Namespace
