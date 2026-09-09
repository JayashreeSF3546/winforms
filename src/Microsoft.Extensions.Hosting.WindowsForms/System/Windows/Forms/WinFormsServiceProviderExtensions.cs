// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Provides access to the application <see cref="UIScope"/> of a built container.
/// </summary>
public static class WinFormsServiceProviderExtensions
{
    /// <summary>
    ///  Gets the application scope, creating it on first use.
    /// </summary>
    /// <param name="services">The application's root service provider.</param>
    /// <returns>The application scope.</returns>
    /// <exception cref="InvalidOperationException">
    ///  The Windows Forms activation services are not registered.
    /// </exception>
    /// <remarks>
    ///  <para>
    ///   The application scope is created once per container and is disposed when the container is
    ///   disposed. It never disposes the root service provider.
    ///  </para>
    /// </remarks>
    public static UIScope GetApplicationScope(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        UIApplicationScopeHolder holder = services.GetService<UIApplicationScopeHolder>()
            ?? throw ActivationServices.MissingServicesException();

        return holder.GetOrCreateApplicationScope();
    }

    /// <summary>
    ///  Gets the activator of the application scope.
    /// </summary>
    /// <param name="services">The application's root service provider.</param>
    /// <returns>The activator bound to the application scope.</returns>
    /// <exception cref="InvalidOperationException">
    ///  The Windows Forms activation services are not registered.
    /// </exception>
    public static IUIActivator GetApplicationActivator(this IServiceProvider services)
        => GetApplicationScope(services).Services.GetRequiredService<IUIActivator>();
}
