namespace WinFormsApplicationBuilderApp;

/// <summary>
///  Configuration values for the greeting shown by the sample form.
/// </summary>
public sealed class GreetingOptions
{
    /// <summary>
    ///  Gets or sets the greeting prefix loaded from appsettings.json.
    /// </summary>
    public string Prefix { get; set; } = "Hello";
}
