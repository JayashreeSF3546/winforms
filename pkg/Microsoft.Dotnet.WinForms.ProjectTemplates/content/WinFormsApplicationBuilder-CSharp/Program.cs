using System.Windows.Forms.VisualStyles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.WinForms;

namespace Company.WinFormsBuilderApp1;

/// <summary>
///  Configures optional builder services and startup defaults before creating the main form.
/// </summary>
internal static class Program
{
    internal const string SettingsApplicationId = "WinFormsApp.9e5e07e6-0d1b-4661-9395-454a2335f647";

    [STAThread]
    internal static void Main()
    {
        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder().UseHost(new HostBuilder());

        builder.Services.AddWinFormsUserSettingsService(
            applicationId: SettingsApplicationId,
            schemaVersion: 1,
            typeInfo: StartupPreferencesJsonContext.Default.StartupPreferences,
            createDefaults: static () => new StartupPreferences());
        builder.Services.AddWinFormsExceptionService(
            handleThreadException: HandleThreadException,
            reportLoggingFailure: static failure => Console.Error.WriteLine(
                $"LOGGING FAILURE {failure.Source}: {failure.Exception.GetType().Name}"));
        builder.Logging.UseWinFormsLogging();

        builder.UseStartupForm<Form1>()
            .UseHighDpiMode(HighDpiMode.SystemAware)
            .UseColorMode(SystemColorMode.System)
            .UseTextRendering(false)
            .UseLegacyVisualStyles()
            .UseFormRevealMode(FormRevealMode.Deferred)
            .UseVisualStylesMode(VisualStylesMode.Net11)
            .UseDefaultFont(SystemFonts.DefaultFont)
            .UseUnhandledExceptionMode(UnhandledExceptionMode.CatchException)
            .UseVisualStyleState(VisualStyleState.ClientAndNonClientAreasEnabled);

        using WinFormsApplication app = builder.Build();
        app.Run();
    }

    private static void HandleThreadException(object? sender, ThreadExceptionEventArgs e)
    {
        Console.Error.WriteLine($"UI FAILURE {e.Exception.GetType().Name}");
        Application.ExitThread();
    }
}
