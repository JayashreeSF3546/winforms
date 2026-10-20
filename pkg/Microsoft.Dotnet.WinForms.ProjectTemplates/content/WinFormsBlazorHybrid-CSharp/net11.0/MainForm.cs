using System.Windows.Forms;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Extensions.Logging;

namespace WinFormsBlazorHybridApp;

/// <summary>
///  Hosts the Razor application in a Windows Forms WebView.
/// </summary>
public partial class MainForm : Form
{
    /// <summary>
    ///  Initializes a new instance for the Windows Forms Designer.
    /// </summary>
    public MainForm()
    {
        InitializeComponent();
    }

    /// <summary>
    ///  Initializes a new instance using the Application Builder's form scope.
    /// </summary>
    /// <param name="services">The services for this form and its components.</param>
    /// <param name="logger">Logs form startup activity.</param>
    public MainForm(IServiceProvider services, ILogger<MainForm> logger)
        : this()
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(logger);

        _blazorWebView.HostPage = "wwwroot\\index.html";
        _blazorWebView.Services = services;
        _blazorWebView.RootComponents.Add<App>("#app");

        logger.LogInformation("Started the Blazor Hybrid view with the application services.");
    }
}
