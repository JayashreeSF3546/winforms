using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.WinForms;

namespace WinFormsApplicationBuilderApp;

/// <summary>
///  Configures and starts the Windows Forms application.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder(args);
        builder.Services
            .AddOptions<GreetingOptions>()
            .Bind(builder.Configuration.GetSection("Greeting"))
            .Validate(
                static options => !string.IsNullOrWhiteSpace(options.Prefix),
                "Greeting:Prefix must not be empty.")
            .ValidateOnStart();

        builder.Services.AddSingleton<GreetingService>();
        builder.Services.AddTransient<MainForm>();
        builder.AddUserSettings();
        builder.UseStartupForm<MainForm>(
            static services => services.GetRequiredService<MainForm>());

        using WinFormsApplication application = builder.Build();
        application.Run();
    }
}
