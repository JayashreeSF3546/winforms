// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Configuration;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Loads and persists per-user application settings.
/// </summary>
/// <remarks>
///  <para>
///   Implementations should keep mutable user preferences separate from
///   deployment configuration and should notify subscribers only after a
///   successful commit.
///  </para>
///  <para>
///   The default implementation uses a versioned JSON file in the current
///   user's local application data directory.
///  </para>
/// </remarks>
public interface IUserSettingsService
{
    /// <summary>
    ///  Occurs after settings are saved, reset, upgraded, or migrated.
    /// </summary>
    event EventHandler<UserSettingsChangedEventArgs>? Changed;

    /// <summary>
    ///  Gets the schema version used by the current application.
    /// </summary>
    int SchemaVersion { get; }

    /// <summary>
    ///  Gets the version stored in the settings file, or <see langword="null"/>
    ///  when no settings file exists.
    /// </summary>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The stored schema version, if present.</returns>
    Task<int?> GetStoredSchemaVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///  Loads settings, applying any required schema migrations first.
    /// </summary>
    /// <typeparam name="TSettings">The settings model type.</typeparam>
    /// <param name="defaults">The settings value returned when no file exists.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The settings stored for the current user, or <paramref name="defaults"/>.</returns>
    Task<TSettings> LoadAsync<TSettings>(
        TSettings defaults,
        CancellationToken cancellationToken = default)
        where TSettings : class;

    /// <summary>
    ///  Saves the supplied settings using the current schema version.
    /// </summary>
    /// <typeparam name="TSettings">The settings model type.</typeparam>
    /// <param name="settings">The settings to save.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    Task SaveAsync<TSettings>(
        TSettings settings,
        CancellationToken cancellationToken = default)
        where TSettings : class;

    /// <summary>
    ///  Replaces stored settings with the supplied defaults.
    /// </summary>
    /// <typeparam name="TSettings">The settings model type.</typeparam>
    /// <param name="defaults">The default settings to persist.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    Task ResetAsync<TSettings>(
        TSettings defaults,
        CancellationToken cancellationToken = default)
        where TSettings : class;

    /// <summary>
    ///  Applies the registered schema migrations to the settings file.
    /// </summary>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    Task UpgradeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///  Imports user-scoped values from generated application settings if no
    ///  JSON settings file exists.
    /// </summary>
    /// <param name="legacySettings">The generated settings instance to import.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>
    ///  <see langword="true"/> if values were imported; otherwise,
    ///  <see langword="false"/> when a JSON settings file already exists.
    /// </returns>
    Task<bool> MigrateFromLegacySettingsAsync(
        ApplicationSettingsBase legacySettings,
        CancellationToken cancellationToken = default);
}
