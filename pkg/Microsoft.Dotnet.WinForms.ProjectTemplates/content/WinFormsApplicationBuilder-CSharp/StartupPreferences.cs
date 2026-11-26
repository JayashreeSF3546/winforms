using System.Text.Json.Serialization;

namespace Company.WinFormsBuilderApp1;

/// <summary>
///  Stores a simple user preference for the WinForms settings service example.
/// </summary>
internal sealed class StartupPreferences
{
    public bool UseCompactLayout { get; set; } = true;
}

/// <summary>
///  Supplies source-generated JSON metadata for the user settings service.
/// </summary>
[JsonSerializable(typeof(StartupPreferences))]
internal sealed partial class StartupPreferencesJsonContext : JsonSerializerContext
{
}
