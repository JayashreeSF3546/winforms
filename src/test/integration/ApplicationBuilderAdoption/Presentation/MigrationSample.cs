// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Forms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.WinForms;

namespace ApplicationBuilderAdoption;

/// <summary>
///  Changes only application startup while retaining the caller's existing Form and event handlers.
/// </summary>
public static class MigrationSample
{
    public static int Run(string[] args, Form legacyForm)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddCommandLine(args).Build();
        string stage = configuration["Migration:Stage"] ?? "builder";
        SampleProbe.Require(stage is "classic" or "builder" or "hosted", "Migration:Stage must be classic, builder, or hosted.");
        SampleProbe probe = new(configuration);
        SampleProbe.Require(probe.Scenario is "" or "smoke", "Unknown migration probe.");
        probe.OnShown(legacyForm, () =>
        {
            ((Button)legacyForm.Controls["Increment"]!).PerformClick();
            SampleProbe.Require(legacyForm.Controls["Count"]!.Text == "Count: 1", "The original button handler did not run.");
            Console.WriteLine("LEGACY HANDLER Count: 1");
            legacyForm.Close();

            return Task.CompletedTask;
        });

        if (stage == "classic")
        {
            Application.Run(legacyForm);
        }
        else
        {
            var builder = WinFormsApplication.CreateBuilder().UseStartupForm(legacyForm);
            if (stage == "hosted")
            {
                builder.UseHost(new HostBuilder()
                    .ConfigureHostConfiguration(config => config.AddInMemoryCollection(
                        [new("hostBuilder:reloadConfigOnChange", "false")]))
                    .ConfigureWinFormsConfiguration(args)
                    .ConfigureLogging(logging => logging.AddDebug())
                    .ConfigureServices(services => services.AddHostedService<HostTrace>()));
            }

            using WinFormsApplication application = builder.Build();
            application.Run();
        }

        SampleProbe.Require(legacyForm.IsDisposed, "The original Form was not disposed.");
        probe.Complete($"migration {stage}: original Form disposed");

        return 0;
    }
}
