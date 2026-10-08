Imports Microsoft.Extensions.Options

Namespace WinFormsApplicationBuilderApp
    ''' <summary>
    '''  Creates a greeting using the configured greeting prefix.
    ''' </summary>
    Public NotInheritable Class GreetingService
        Private ReadOnly _options As IOptions(Of GreetingOptions)

        ''' <summary>
        '''  Initializes a new instance of the <see cref="GreetingService"/> class.
        ''' </summary>
        ''' <param name="options">The configured greeting options.</param>
        Public Sub New(options As IOptions(Of GreetingOptions))
            _options = options
        End Sub

        ''' <summary>
        '''  Creates a greeting for the specified display name.
        ''' </summary>
        ''' <param name="name">The name to greet.</param>
        ''' <returns>The configured greeting.</returns>
        Public Function CreateGreeting(name As String) As String
            Dim displayName As String =
                If(String.IsNullOrWhiteSpace(name), "developer", name.Trim())

            Return $"{_options.Value.Prefix}, {displayName}!"
        End Function
    End Class
End Namespace
