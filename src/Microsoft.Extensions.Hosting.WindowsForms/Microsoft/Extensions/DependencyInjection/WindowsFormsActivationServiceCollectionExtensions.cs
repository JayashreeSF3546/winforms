// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.WinForms;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///  Registers Designer-safe Windows Forms activation and UI scope services.
/// </summary>
public static class WindowsFormsActivationServiceCollectionExtensions
{
    /// <summary>
    ///  Adds the Windows Forms activation services. The call is idempotent, and
    ///  <c>WinFormsApplicationBuilder.Build()</c> and the WinForms lifetime extensions call it
    ///  automatically.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection.</returns>
    /// <remarks>
    ///  <para>
    ///   Registration adds no hosted service, starts nothing, and creates no scope. The application
    ///   scope is created on first use and disposed with the container.
    ///  </para>
    /// </remarks>
    public static IServiceCollection AddWindowsFormsActivation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<UIApplicationScopeHolder>();
        services.TryAddScoped<UIScopeHolder>();
        services.TryAddScoped<IUIActivator, UIActivator>();

        return services;
    }
}
