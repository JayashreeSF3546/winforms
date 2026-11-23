// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using Microsoft.Extensions.WinForms;

namespace ApplicationBuilderStartupConfiguration.CSharp;

/// <summary>
///  Configures startup defaults through the builder before constructing the main form.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder();

        builder.UseStartupForm<MainForm>()
            .UseHighDpiMode(HighDpiMode.SystemAware)
            .UseColorMode(SystemColorMode.System)
            .UseTextRendering(false)
            .UseLegacyVisualStyles()
            .UseFormRevealMode(FormRevealMode.Deferred)
            .UseVisualStylesMode(VisualStylesMode.Net11)
            .UseDefaultFont(SystemFonts.DefaultFont)
            .UseUnhandledExceptionMode(UnhandledExceptionMode.ThrowException)
            .UseVisualStyleState(VisualStyleState.ClientAndNonClientAreasEnabled);

        using WinFormsApplication app = builder.Build();
        app.Run();
    }
}
