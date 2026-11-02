// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.WinForms;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///  Registers the optional, replaceable per-application JSON user settings service.
/// </summary>
public static class UserSettingsServiceCollectionExtensions
{
    /// <summary>
    ///  Registers a singleton settings service using explicit serialization metadata and fresh defaults.
    /// </summary>
    /// <typeparam name="T">The settings JSON object model.</typeparam>
    /// <param name="services">The application's service collection.</param>
    /// <param name="applicationId">A stable application identifier valid as one Windows directory name.</param>
    /// <param name="schemaVersion">The current positive schema version, independent of executable version.</param>
    /// <param name="typeInfo">Serialization metadata, preferably from a source-generated JSON context.</param>
    /// <param name="createDefaults">A factory for fresh defaults, executed away from the caller's UI thread.</param>
    /// <param name="configure">Optional storage, validation, and migration configuration.</param>
    /// <returns>The supplied service collection.</returns>
    /// <remarks>
    ///  <para>
    ///   Configuration is snapshotted during registration. No directories or files are created until an operation.
    ///   Third-party implementations may replace IUserSettingsService without depending on WinForms internals.
    ///   Defaults, validators, migrations, and legacy mappings must be UI-free and must not reenter the service.
    ///  </para>
    /// </remarks>
    public static IServiceCollection AddJsonUserSettings<T>(
        this IServiceCollection services,
        string applicationId,
        int schemaVersion,
        JsonTypeInfo<T> typeInfo,
        Func<T> createDefaults,
        Action<JsonUserSettingsOptions<T>>? configure = null) where T : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schemaVersion);
        ArgumentNullException.ThrowIfNull(typeInfo);
        ArgumentNullException.ThrowIfNull(createDefaults);
        JsonUserSettingsOptions<T> options = new();
        configure?.Invoke(options);
        JsonUserSettingsService<T>.Registration registration = new(applicationId, schemaVersion, typeInfo, createDefaults, options);
        services.AddSingleton<IUserSettingsService<T>>(_ => new JsonUserSettingsService<T>(registration));

        return services;
    }
}
