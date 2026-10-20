// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Nodes;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Migrates user settings from one schema version to the next.
/// </summary>
public interface IUserSettingsMigration
{
    /// <summary>
    ///  Gets the schema version this migration reads.
    /// </summary>
    int FromVersion { get; }

    /// <summary>
    ///  Migrates a settings object to the next schema version.
    /// </summary>
    /// <param name="settings">A copy of the settings object to migrate.</param>
    /// <returns>The migrated settings object.</returns>
    JsonObject Migrate(JsonObject settings);
}
