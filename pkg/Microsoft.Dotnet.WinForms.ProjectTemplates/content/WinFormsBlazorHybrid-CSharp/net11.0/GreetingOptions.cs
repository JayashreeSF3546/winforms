namespace WinFormsBlazorHybridApp;

/// <summary>
///  Configuration values used by the Blazor Hybrid greeting.
/// </summary>
public sealed class GreetingOptions
{
    /// <summary>
    ///  Gets or sets the greeting prefix loaded from appsettings.json.
    /// </summary>
    public string Prefix { get; set; } = "Hello";
}
