// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.WinForms;

namespace ApplicationBuilderStartupConfiguration.CSharp;

/// <summary>
///  Configures optional builder services and startup defaults before creating the main form.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder().UseHost(new HostBuilder());

        builder.Services.AddWinFormsUserSettingsService(
            "ApplicationBuilderStartupConfiguration",
            schemaVersion: 1,
            StartupPreferencesJsonContext.Default.StartupPreferences,
            static () => new StartupPreferences());
        builder.Services.AddWinFormsExceptionService(
            HandleThreadException,
            static failure => Console.Error.WriteLine($"LOGGING FAILURE {failure.Source}: {failure.Exception.GetType().Name}"));
        builder.Logging.UseWinFormsLogging();

        builder.UseStartupForm<MainForm>()
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
        Console.Error.WriteLine($"----------------------UI FAILURE {e.Exception.GetType().Name}");
        Application.ExitThread();
    }
}
