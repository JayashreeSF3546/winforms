// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Extensions.WinForms;

namespace ApplicationBuilderMigration.CSharp;

/// <summary>
///  Selects one public-prototype builder driver on the caller's STA.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        bool probe = args.Length > 1 && args[1] == "--probe";
        int separator = probe ? 2 : 1;
        if (args.Length <= separator || args[0] != "shared" || args[separator] != "--")
        {
            throw new ArgumentException("Expected: shared [--probe] -- [argument tokens]");
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        MigrationSession session = new(args.Skip(separator + 1).ToArray());
        // Application-owned preflight, NOT the internal Starting feature.
        if (session.Start(session.Arguments))
        {
            WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder()
                .UseApplicationContext(() =>
                {
                    session.Events.Add("activate");
                    return new ApplicationContext(new MainForm(session, probe));
                });
            using WinFormsApplication application = builder.Build();
            application.Lifetime.ApplicationStarted += (_, _) => session.Events.Add("started");
            application.Lifetime.ApplicationStopping += (_, _) => session.Events.Add("stopping");
            application.Lifetime.ApplicationStopped += (_, _) => session.Events.Add("stopped");
            application.Run();
        }

        session.Events.Add("returned");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            session.Arguments,
            session.Events,
            session.Count,
            FormsModule = typeof(Form).Module.ModuleVersionId,
            FormsPath = typeof(Form).Assembly.Location
        }));
    }
}
