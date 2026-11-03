Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Text.Json
Imports System.Text.Json.Serialization.Metadata
Imports Microsoft.Extensions.Configuration
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.Hosting
Imports Microsoft.Extensions.Logging
Imports Microsoft.Extensions.Options
Imports Microsoft.Extensions.WinForms

''' <summary>
'''  Composes one host and starts the owning STA message loop without the VB Application Framework.
''' </summary>
Friend Module Program
    Friend Const SettingsApplicationId As String = "WinFormsApp.9e5e07e6-0d1b-4661-9395-454a2335f647"

    <STAThread>
    Friend Function Main(args As String()) As Integer
        Try
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException)
            Application.SetHighDpiMode(HighDpiMode.SystemAware)
            Application.EnableVisualStyles()
            Application.SetCompatibleTextRenderingDefault(False)
            Dim winFormsApplication As WinFormsApplication = Nothing
            winFormsApplication = WinFormsApplication.CreateBuilder().
                UseHost(CreateHostBuilder(args)).
                UseApplicationContext(
                    Function()
                        Dim services As IServiceProvider = winFormsApplication.Host.Services
                        Dim options = services.GetRequiredService(Of IOptions(Of ApplicationOptions))()
                        Dim logger = services.GetRequiredService(Of ILogger(Of Form1))()

                        Return New ApplicationContext(New Form1(options.Value, logger))
                    End Function).
                Build()
            Using winFormsApplication
                winFormsApplication.Run()
            End Using

            Return 0
        Catch exception As Exception When Not TypeOf exception Is OutOfMemoryException AndAlso
            Not TypeOf exception Is StackOverflowException AndAlso Not TypeOf exception Is AccessViolationException
            Trace.TraceError(exception.ToString())
            MessageBox.Show(exception.Message, "Application error", MessageBoxButtons.OK, MessageBoxIcon.Error)

            Return 1
        End Try
    End Function

    Friend Function CreateHostBuilder(args As String()) As IHostBuilder
        Dim serializerOptions As New JsonSerializerOptions With {
            .TypeInfoResolver = New DefaultJsonTypeInfoResolver()
        }
        Dim typeInfo = DirectCast(serializerOptions.GetTypeInfo(GetType(UserPreferences)), JsonTypeInfo(Of UserPreferences))

        Return New HostBuilder().
            ConfigureHostConfiguration(
                Sub(configuration)
                    configuration.AddInMemoryCollection(
                        New Dictionary(Of String, String) From {{"hostBuilder:reloadConfigOnChange", "false"}})
                End Sub).
            ConfigureWinFormsConfiguration(args).
            UseDefaultServiceProvider(
                Sub(options)
                    options.ValidateScopes = True
                    options.ValidateOnBuild = True
                End Sub).
            ConfigureLogging(
                Sub(context, logging)
                    logging.AddConfiguration(context.Configuration.GetSection("Logging")).AddDebug()
                End Sub).
            ConfigureServices(
                Sub(context, services)
                    services.AddOptions(Of ApplicationOptions)().
                        Bind(context.Configuration.GetSection("Application")).
                        Validate(Function(options) Not String.IsNullOrWhiteSpace(options.Title),
                                 "Application:Title must not be empty.").
                        ValidateOnStart()
                    services.AddJsonUserSettings(
                        applicationId:=SettingsApplicationId,
                        schemaVersion:=1,
                        typeInfo:=typeInfo,
                        createDefaults:=Function() New UserPreferences())
                End Sub)
    End Function
End Module
