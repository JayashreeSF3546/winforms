using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.WinForms;

namespace Company.WinFormsBuilderApp1;

/// <summary>
///  Composes one host and starts the owning STA message loop.
/// </summary>
internal static class Program
{
    internal const string SettingsApplicationId = "WinFormsApp.9e5e07e6-0d1b-4661-9395-454a2335f647";

    [STAThread]
    internal static int Main(string[] args)
    {
        try
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            ApplicationConfiguration.Initialize();
            WinFormsApplication? application = null;
            using (application = WinFormsApplication.CreateBuilder()
                .UseHost(CreateHostBuilder(args))
                .UseApplicationContext(() =>
                {
                    IServiceProvider services = application!.Host!.Services;
                    var options = services.GetRequiredService<IOptions<ApplicationOptions>>();
                    var logger = services.GetRequiredService<ILogger<Form1>>();

                    return new ApplicationContext(new Form1(options.Value, logger));
                })
                .Build())
            {
                application.Run();
            }

            return 0;
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            Trace.TraceError(exception.ToString());
            MessageBox.Show(exception.Message, "Application error", MessageBoxButtons.OK, MessageBoxIcon.Error);

            return 1;
        }
    }

    internal static IHostBuilder CreateHostBuilder(string[] args)
        => new HostBuilder()
            .ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["hostBuilder:reloadConfigOnChange"] = "false" }))
            .ConfigureWinFormsConfiguration(args)
            .UseDefaultServiceProvider(options =>
            {
                options.ValidateScopes = true;
                options.ValidateOnBuild = true;
            })
            .ConfigureLogging((context, logging) => logging
                .AddConfiguration(context.Configuration.GetSection("Logging"))
                .AddDebug())
            .ConfigureServices((context, services) =>
            {
                services.AddOptions<ApplicationOptions>()
                    .Bind(context.Configuration.GetSection("Application"))
                    .Validate(options => !string.IsNullOrWhiteSpace(options.Title), "Application:Title must not be empty.")
                    .ValidateOnStart();
                services.AddJsonUserSettings(
                    applicationId: SettingsApplicationId,
                    schemaVersion: 1,
                    typeInfo: PreferencesJsonContext.Default.UserPreferences,
                    createDefaults: () => new UserPreferences());
            });
}
