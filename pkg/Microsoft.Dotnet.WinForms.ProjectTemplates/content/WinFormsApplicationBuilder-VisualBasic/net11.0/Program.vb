Imports System.Windows.Forms
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.Logging
Imports Microsoft.Extensions.WinForms

Namespace WinFormsApplicationBuilderApp
    ''' <summary>
    '''  Configures and starts the Windows Forms application.
    ''' </summary>
    Friend Module Program
        <STAThread>
        Friend Sub Main(args As String())
            Application.SetHighDpiMode(HighDpiMode.SystemAware)
            Application.EnableVisualStyles()
            Application.SetCompatibleTextRenderingDefault(False)

            Dim builder As WinFormsApplicationBuilder = WinFormsApplication.CreateBuilder(args)
            builder.Services _
                .AddOptions(Of GreetingOptions)() _
                .Bind(builder.Configuration.GetSection("Greeting")) _
                .Validate(
                    Function(options) Not String.IsNullOrWhiteSpace(options.Prefix),
                    "Greeting:Prefix must not be empty.") _
                .ValidateOnStart()

            builder.Services.AddSingleton(Of GreetingService)()
            builder.Services.AddTransient(Of MainForm)()
            builder.AddUserSettings()
            builder.UseStartupForm(Of MainForm)(
                Function(services) services.GetRequiredService(Of MainForm)())

            Using application As WinFormsApplication = builder.Build()
                application.Run()
            End Using
        End Sub
    End Module
End Namespace
