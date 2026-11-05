using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.WinForms;

namespace Company.WinFormsHybridApp1;

/// <summary>
///  Stages single-host composition without enabling unsupported Hybrid activation.
/// </summary>
internal static class Program
{
    [STAThread]
    internal static int Main(string[] args)
    {
        try
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            using WinFormsApplication application = CreateApplication(args);
            ApplicationConfiguration.Initialize();
            application.Run();

            return 0;
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            Trace.TraceError(exception.ToString());
            MessageBox.Show(exception.Message, "Hybrid scaffold unavailable", MessageBoxButtons.OK, MessageBoxIcon.Error);

            return 1;
        }
    }

    internal static WinFormsApplication CreateApplication(string[] args)
    {
        EnsureHybridLifecycleReady();
        WinFormsApplication? application = null;
        application = WinFormsApplication.CreateBuilder()
            .UseHost(CreateHostBuilder(args))
            .UseApplicationContext(() =>
            {
                IServiceProvider services = application!.Host!.Services;
                var options = services.GetRequiredService<IOptions<ApplicationOptions>>();
                var logger = services.GetRequiredService<ILogger<HybridForm>>();

                return new ApplicationContext(HybridForm.CreateRuntime(services, options.Value, logger));
            })
            .Build();

        return application;
    }

    internal static void EnsureHybridLifecycleReady()
        => throw new NotSupportedException(
            "This source-only Hybrid scaffold is disabled. Public WinForms initialization/shutdown registration "
            + "and an awaitable BlazorWebView cleanup contract must be approved and integrated before activation. "
            + "Do not remove this gate to run the application.");

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
                services.AddWindowsFormsBlazorWebView();
            });
}
