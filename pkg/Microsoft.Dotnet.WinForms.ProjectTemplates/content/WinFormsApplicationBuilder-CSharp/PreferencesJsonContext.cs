using System.Text.Json.Serialization;

namespace Company.WinFormsBuilderApp1;

/// <summary>
///  Supplies explicit serialization metadata without reflecting over the preferences model.
/// </summary>
[JsonSerializable(typeof(UserPreferences))]
internal sealed partial class PreferencesJsonContext : JsonSerializerContext
{
}
