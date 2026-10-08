using Microsoft.Extensions.Options;

namespace WinFormsApplicationBuilderApp;

/// <summary>
///  Creates a greeting using the configured greeting prefix.
/// </summary>
public sealed class GreetingService
{
    private readonly IOptions<GreetingOptions> _options;

    /// <summary>
    ///  Initializes a new instance of the <see cref="GreetingService"/> class.
    /// </summary>
    /// <param name="options">The configured greeting options.</param>
    public GreetingService(IOptions<GreetingOptions> options)
    {
        _options = options;
    }

    /// <summary>
    ///  Creates a greeting for the specified display name.
    /// </summary>
    /// <param name="name">The name to greet.</param>
    /// <returns>The configured greeting.</returns>
    public string CreateGreeting(string name)
    {
        string displayName = string.IsNullOrWhiteSpace(name) ? "developer" : name.Trim();

        return $"{_options.Value.Prefix}, {displayName}!";
    }
}
