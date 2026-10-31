// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.WinForms;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///  Registers optional desktop execution of standard health diagnostics.
/// </summary>
public static class WinFormsHealthServiceCollectionExtensions
{
    /// <summary>
    ///  Adds the standard health infrastructure and an idempotently registered desktop diagnostics service.
    /// </summary>
    /// <param name="services">The application's host service collection.</param>
    /// <returns>The standard builder used to register application health checks.</returns>
    /// <remarks>
    ///  <para>
    ///   Existing HealthCheckService registrations are preserved. Standard publisher infrastructure remains
    ///   inactive unless the application registers publishers. No checks or readiness gates execute here.
    ///   The application must supply IHostApplicationLifetime, normally through Generic Host.
    ///  </para>
    /// </remarks>
    [CLSCompliant(false)]
    public static IHealthChecksBuilder AddWinFormsHealthDiagnostics(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<WinFormsHealthDiagnostics>();

        return services.AddHealthChecks();
    }
}
