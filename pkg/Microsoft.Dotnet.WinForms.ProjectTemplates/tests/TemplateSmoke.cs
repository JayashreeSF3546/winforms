// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.WinForms;

/// <summary>
///  Exercises the generated C# composition without adding test switches to the application.
/// </summary>
internal static class TemplateSmoke
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            switch (args.Single())
            {
                case "services":
                    CheckServices();
                    break;
                case "invalid-options":
                    using (IHost host = Program.CreateHostBuilder(["--Application:Title="]).Build())
                    {
                        bool rejected = false;
                        try
                        {
                            host.StartAsync().GetAwaiter().GetResult();
                        }
                        catch (OptionsValidationException)
                        {
                            rejected = true;
                        }

                        Check(rejected, "Invalid options must fail host startup.");
                    }

                    break;
                case "designer":
                    using (Form1 form = new())
                    {
                        Check(!form.IsHandleCreated && form.Text == "Form1", "Parameterless construction must be UI-only.");
                    }

                    break;
                case "ui":
                case "host-stop":
                    CheckUi(args[0] == "host-stop");
                    break;
                default:
                    throw new ArgumentException("Unknown template smoke scenario.");
            }

            Console.WriteLine($"PASS C#: {args[0]}");

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);

            return 1;
        }
    }

    private static void CheckServices()
    {
        const string prefix = "WinFormsApp.";
        Check(Program.SettingsApplicationId.StartsWith(prefix, StringComparison.Ordinal)
            && Guid.TryParse(Program.SettingsApplicationId[prefix.Length..], out _), "Invalid generated settings identity.");
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinForms", Program.SettingsApplicationId);
        Check(!Directory.Exists(directory), "The settings test requires a newly generated, unused application identity.");
        try
        {
            using (IHost host = Program.CreateHostBuilder([]).Build())
            {
                host.StartAsync().GetAwaiter().GetResult();
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json")));
                string? title = document.RootElement.GetProperty("Application").GetProperty("Title").GetString();
                Check(host.Services.GetRequiredService<IOptions<ApplicationOptions>>().Value.Title == title, "Deployment JSON was not applied.");
                Check(host.Services.GetRequiredService<IConfiguration>()["hostBuilder:reloadConfigOnChange"] == "false", "Watchers must default off.");
                var settings = host.Services.GetRequiredService<IUserSettingsService<UserPreferences>>();
                Check(!Directory.Exists(directory), "Registration/resolution must not create storage.");
                Check(settings.LoadAsync().GetAwaiter().GetResult().LastFolder == string.Empty, "Incorrect fresh preferences.");
                UserSettingsCommit commit = settings.SaveAsync(new UserPreferences { LastFolder = "template-test" }).GetAwaiter().GetResult();
                commit.NotificationCompletion.GetAwaiter().GetResult();
                Check(settings.LoadAsync().GetAwaiter().GetResult().LastFolder == "template-test", "Settings round trip failed.");
                host.StopAsync().GetAwaiter().GetResult();
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        string? previous = Environment.GetEnvironmentVariable("Application__Title");
        try
        {
            Environment.SetEnvironmentVariable("Application__Title", "environment-title");
            CheckTitle([], "environment-title");
            CheckTitle(["--Application:Title=command-title"], "command-title");
        }
        finally
        {
            Environment.SetEnvironmentVariable("Application__Title", previous);
        }
    }

    private static void CheckTitle(string[] args, string expected)
    {
        using IHost host = Program.CreateHostBuilder(args).Build();
        host.StartAsync().GetAwaiter().GetResult();
        Check(host.Services.GetRequiredService<IOptions<ApplicationOptions>>().Value.Title == expected, "Configuration precedence failed.");
        host.StopAsync().GetAwaiter().GetResult();
    }

    private static void CheckUi(bool hostStop)
    {
        bool dispatched = false;
        bool disposed = false;
        bool titleMatches = false;
        IHostApplicationLifetime? lifetime = null;
        EventHandler idle = (_, _) =>
        {
            if (dispatched || Application.OpenForms.Count == 0)
            {
                return;
            }

            dispatched = true;
            Form form = Application.OpenForms[0]!;
            titleMatches = form is Form1 && form.Text == "template-ui";
            form.Disposed += (_, _) => disposed = true;
            if (hostStop)
            {
                lifetime!.StopApplication();
            }
            else
            {
                form.Close();
            }
        };
        Application.Idle += idle;
        try
        {
            string[] args = ["--Application:Title=template-ui"];
            if (hostStop)
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                ApplicationConfiguration.Initialize();
                WinFormsApplication? application = null;
                using (application = WinFormsApplication.CreateBuilder()
                    .UseHost(Program.CreateHostBuilder(args))
                    .UseApplicationContext(() =>
                    {
                        IServiceProvider services = application!.Host!.Services;

                        return new ApplicationContext(new Form1(services.GetRequiredService<IOptions<ApplicationOptions>>().Value,
                            services.GetRequiredService<ILogger<Form1>>()));
                    }).Build())
                {
                    lifetime = application.Host!.Services.GetRequiredService<IHostApplicationLifetime>();
                    application.Run();
                }
            }
            else
            {
                Check(Program.Main(args) == 0, "Generated Main failed.");
            }
        }
        finally
        {
            Application.Idle -= idle;
        }

        Check(dispatched && titleMatches && disposed && Application.OpenForms.Count == 0, "UI dispatch/cleanup failed.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
