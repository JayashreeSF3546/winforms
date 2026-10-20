using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;

namespace WinFormsBlazorHybridApp;

/// <summary>
///  Logs component failures without displaying exception details in the UI.
/// </summary>
public sealed class LoggingErrorBoundary : ErrorBoundary
{
    [Inject]
    private ILogger<LoggingErrorBoundary> Logger { get; set; } = null!;

    protected override Task OnErrorAsync(Exception exception)
    {
        Logger.LogError(exception, "An unhandled exception occurred in a Blazor component.");
        return Task.CompletedTask;
    }
}
