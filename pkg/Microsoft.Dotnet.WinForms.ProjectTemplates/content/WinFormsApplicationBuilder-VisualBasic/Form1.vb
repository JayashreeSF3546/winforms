Imports Microsoft.Extensions.Logging

''' <summary>
'''  Keeps Designer construction independent from runtime service composition.
''' </summary>
Public Partial Class Form1
    Public Sub New()
        InitializeComponent()
    End Sub

    Friend Sub New(options As ApplicationOptions, logger As ILogger(Of Form1))
        Me.New()
        Text = options.Title
#Disable Warning CA1848 ' A single startup message keeps generated code approachable.
        logger.LogInformation("Main window created.")
#Enable Warning CA1848
    End Sub
End Class
