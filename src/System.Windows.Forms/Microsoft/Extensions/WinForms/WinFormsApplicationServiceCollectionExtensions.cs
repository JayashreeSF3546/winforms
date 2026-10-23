// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Provides registration methods for shared Windows Forms application services.
/// </summary>
public static class WinFormsApplicationServiceCollectionExtensions
{
    /// <summary>
    ///  Registers default application information, runtime information, and network availability services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddWinFormsApplicationServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IWinFormsApplicationInfo, WinFormsApplicationInfo>();
        services.TryAddSingleton<IWinFormsRuntimeInfo, WinFormsRuntimeInfo>();
        services.TryAddSingleton<WinFormsNetworkAvailabilityService>();
        services.TryAddSingleton<IWinFormsNetworkAvailabilityService>(
            static serviceProvider => (IWinFormsNetworkAvailabilityService)
                (serviceProvider.GetService(typeof(WinFormsNetworkAvailabilityService))
                    ?? throw new InvalidOperationException("The network availability service was not registered.")));

        return services;
    }
}
