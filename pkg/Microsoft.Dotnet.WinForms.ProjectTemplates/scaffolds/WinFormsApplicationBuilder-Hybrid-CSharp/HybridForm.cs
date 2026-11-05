using Company.WinFormsHybridApp1.Components;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Extensions.Logging;

namespace Company.WinFormsHybridApp1;

/// <summary>
///  Keeps Designer construction independent of the proposed runtime WebView composition.
/// </summary>
public partial class HybridForm : Form
{
    public HybridForm()
    {
        InitializeComponent();
    }

    internal static HybridForm CreateRuntime(IServiceProvider services, ApplicationOptions options, ILogger<HybridForm> logger)
    {
        Program.EnsureHybridLifecycleReady();

        return new HybridForm(services, options, logger);
    }

    private HybridForm(IServiceProvider services, ApplicationOptions options, ILogger<HybridForm> logger)
        : this()
    {
        Text = options.Title;
        BlazorWebView webView = new()
        {
            Dock = DockStyle.Fill,
            HostPage = Path.Combine("wwwroot", "index.html"),
            Services = services
        };
        webView.RootComponents.Add<App>("#app");
        webView.UrlLoading += (_, e) =>
        {
            if (e.Url is { IsAbsoluteUri: true, Scheme: "https", Host: "0.0.0.0", IsDefaultPort: true, UserInfo: "" })
            {
                return;
            }

            e.UrlLoadingStrategy = UrlLoadingStrategy.CancelLoad;
            LogBlockedNavigation(logger);
        };
        contentHost.Controls.Add(webView);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Navigation outside the application origin was blocked.")]
    private static partial void LogBlockedNavigation(ILogger logger);
}
