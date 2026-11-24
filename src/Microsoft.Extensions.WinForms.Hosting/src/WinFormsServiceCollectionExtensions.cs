// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.WinForms;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///  Registers opt-in settings, exception handling, and scoped dialogs for a Windows Forms application.
/// </summary>
public static class WinFormsServiceCollectionExtensions
{
    /// <summary>
    ///  Registers the replaceable scoped dialog service for a Windows Forms application.
    /// </summary>
    /// <param name="services">The application's service collection.</param>
    /// <returns>The supplied service collection.</returns>
    /// <remarks>
    ///  <para>
    ///   Select UseScopedStartupForm or UseScopedApplicationContext and an unbuilt host on the application
    ///   builder. Registration creates no UI or scopes. The singleton service uses the application's
    ///   owning STA and existing UI scope tree; each modal invocation receives a distinct service scope.
    ///   A previously registered IWinFormsDialogService implementation is preserved.
    ///  </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddWinFormsDialogs(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IWinFormsDialogService, WinFormsDialogService>();
        return services;
    }

    /// <summary>
    ///  Registers the typed JSON user settings service with explicit identity and serialization metadata.
    /// </summary>
    /// <typeparam name="T">The settings JSON object model.</typeparam>
    /// <param name="services">The application's service collection.</param>
    /// <param name="applicationId">A stable application identifier valid as one Windows directory name.</param>
    /// <param name="schemaVersion">The current positive schema version.</param>
    /// <param name="typeInfo">Serialization metadata, preferably from a source-generated JSON context.</param>
    /// <param name="createDefaults">A factory for fresh defaults, executed away from the caller's UI thread.</param>
    /// <param name="configure">Optional storage, validation, and migration configuration.</param>
    /// <returns>The supplied service collection.</returns>
    /// <remarks>
    ///  <para>
    ///   Uses <see cref="UserSettingsServiceCollectionExtensions.AddJsonUserSettings{T}"/> without adding
    ///   automatic loading, saving, migration, or a second settings store.
    ///  </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentException">The application identity or storage configuration is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The schema version or lock timeout is not positive.</exception>
    public static IServiceCollection AddWinFormsUserSettingsService<T>(
        this IServiceCollection services,
        string applicationId,
        int schemaVersion,
        JsonTypeInfo<T> typeInfo,
        Func<T> createDefaults,
        Action<JsonUserSettingsOptions<T>>? configure = null) where T : class
        => services.AddJsonUserSettings(applicationId, schemaVersion, typeInfo, createDefaults, configure);

    /// <summary>
    ///  Registers exception logging with an explicit UI exception policy and diagnostic fallback.
    /// </summary>
    /// <param name="services">The application's service collection.</param>
    /// <param name="handleThreadException">The application's UI exception policy.</param>
    /// <param name="reportLoggingFailure">A non-throwing fallback that must not reuse the failing logger.</param>
    /// <param name="configure">Optional exception notification and disclosure settings, copied during registration.</param>
    /// <returns>The supplied service collection.</returns>
    /// <remarks>
    ///  <para>
    ///   The coordinated host activates one <see cref="WinFormsExceptionLogging"/> scope before UI startup.
    ///   Subscriptions end with the application lifetime; logging providers remain host-owned.
    ///   No logging destinations or unhandled exception mode are selected. Do not separately call Attach
    ///   or install competing UI exception handlers. Repeated registrations retain the first policy.
    ///  </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public static IServiceCollection AddWinFormsExceptionService(
        this IServiceCollection services,
        ThreadExceptionEventHandler handleThreadException,
        Action<WinFormsLoggingFailure> reportLoggingFailure,
        Action<WinFormsExceptionLoggingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(handleThreadException);
        ArgumentNullException.ThrowIfNull(reportLoggingFailure);
        WinFormsExceptionLoggingOptions options = new();
        configure?.Invoke(options);
        bool logAppDomainExceptions = options.LogAppDomainExceptions;
        bool logUnobservedTaskExceptions = options.LogUnobservedTaskExceptions;
        bool includeExceptionDetails = options.IncludeExceptionDetails;

        services.AddLogging();
        services.TryAddSingleton(provider => WinFormsExceptionLogging.Attach(
            provider.GetRequiredService<WinFormsApplication>(),
            provider.GetRequiredService<ILogger<WinFormsExceptionLogging>>(),
            handleThreadException,
            reportLoggingFailure,
            snapshot =>
            {
                snapshot.LogAppDomainExceptions = logAppDomainExceptions;
                snapshot.LogUnobservedTaskExceptions = logUnobservedTaskExceptions;
                snapshot.IncludeExceptionDetails = includeExceptionDetails;
            }));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, WinFormsExceptionService>());

        return services;
    }
}
