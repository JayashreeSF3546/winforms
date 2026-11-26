Imports System
Imports System.Drawing
Imports System.Text.Json
Imports System.Text.Json.Serialization.Metadata
Imports System.Threading
Imports System.Windows.Forms.VisualStyles
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.Hosting
Imports Microsoft.Extensions.Logging
Imports Microsoft.Extensions.WinForms

''' <summary>
'''  Configures optional builder services and startup defaults before creating the main form.
''' </summary>
Friend Module Program
    Friend Const SettingsApplicationId As String = "WinFormsApp.9e5e07e6-0d1b-4661-9395-454a2335f647"

    <STAThread>
    Friend Sub Main()
        Dim builder As WinFormsApplicationBuilder = WinFormsApplication.CreateBuilder().UseHost(New HostBuilder())

        Dim serializerOptions As New JsonSerializerOptions With {
            .TypeInfoResolver = New DefaultJsonTypeInfoResolver()
        }
        Dim typeInfo = DirectCast(serializerOptions.GetTypeInfo(GetType(StartupPreferences)), JsonTypeInfo(Of StartupPreferences))

        builder.Services.AddWinFormsUserSettingsService(
            applicationId:=SettingsApplicationId,
            schemaVersion:=1,
            typeInfo:=typeInfo,
            createDefaults:=Function() New StartupPreferences())
        builder.Services.AddWinFormsExceptionService(
            handleThreadException:=AddressOf HandleThreadException,
            reportLoggingFailure:=Sub(failure) Console.Error.WriteLine(
                $"LOGGING FAILURE {failure.Source}: {failure.Exception.GetType().Name}"))
        builder.ConfigureLogging(Sub(logging) logging.UseWinFormsLogging())

        builder.UseStartupForm(Of Form1)().
            UseHighDpiMode(HighDpiMode.SystemAware).
            UseColorMode(SystemColorMode.System).
            UseTextRendering(False).
            UseLegacyVisualStyles().
            UseFormRevealMode(FormRevealMode.Deferred).
            UseVisualStylesMode(VisualStylesMode.Net11).
            UseDefaultFont(SystemFonts.DefaultFont).
            UseUnhandledExceptionMode(UnhandledExceptionMode.CatchException).
            UseVisualStyleState(VisualStyleState.ClientAndNonClientAreasEnabled)

        Using app As WinFormsApplication = builder.Build()
            app.Run()
        End Using
    End Sub

    Private Sub HandleThreadException(sender As Object, e As ThreadExceptionEventArgs)
        Console.Error.WriteLine($"UI FAILURE {e.Exception.GetType().Name}")
        Application.ExitThread()
    End Sub
End Module
