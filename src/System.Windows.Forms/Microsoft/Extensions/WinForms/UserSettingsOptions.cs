// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Configures the default JSON user-settings service.
/// </summary>
public sealed class UserSettingsOptions
{
    /// <summary>
    ///  Gets or sets an application-specific identifier used to derive the
    ///  default user-settings path.
    /// </summary>
    public string? ApplicationId { get; set; }

    /// <summary>
    ///  Gets or sets an explicit path for the user-settings JSON file.
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    ///  Gets or sets the current settings schema version.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    ///  Gets the ordered schema migrations registered for this settings store.
    /// </summary>
    public IList<IUserSettingsMigration> Migrations { get; } = [];

    /// <summary>
    ///  Gets or sets the JSON serialization options used for settings values.
    /// </summary>
    public JsonSerializerOptions SerializerOptions { get; set; } = new(JsonSerializerDefaults.Web);

    /// <summary>
    ///  Gets or sets how long an operation waits to acquire the settings-file
    ///  lock.
    /// </summary>
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
