// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Provides the settings snapshot associated with a committed change.
/// </summary>
public sealed class UserSettingsChangedEventArgs : EventArgs
{
    internal UserSettingsChangedEventArgs(int schemaVersion, JsonObject settings)
    {
        SchemaVersion = schemaVersion;
        using JsonDocument document = JsonDocument.Parse(settings.ToJsonString());
        Settings = document.RootElement.Clone();
    }

    /// <summary>
    ///  Gets the schema version of the committed settings.
    /// </summary>
    public int SchemaVersion { get; }

    /// <summary>
    ///  Gets an immutable JSON snapshot of the committed settings object.
    /// </summary>
    public JsonElement Settings { get; }
}
