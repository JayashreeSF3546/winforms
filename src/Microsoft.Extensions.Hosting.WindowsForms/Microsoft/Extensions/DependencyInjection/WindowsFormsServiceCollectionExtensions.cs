// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.WinForms;
using System.Windows.Forms;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///  Registers the WinForms Generic Host lifetime coordinator.
/// </summary>
public static class WindowsFormsServiceCollectionExtensions
{
    /// <summary>
    ///  Adds a WinForms lifetime with an empty application context.
    /// </summary>
    public static IServiceCollection AddWindowsFormsLifetime(
        this IServiceCollection services,
        Action<WinFormsApplicationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        WinFormsApplicationOptions options = new();
        configure?.Invoke(options);
        return AddLifetime(services, options);
    }

    /// <summary>
    ///  Adds a WinForms lifetime with a startup form.
    /// </summary>
    public static IServiceCollection AddWindowsFormsLifetime<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TForm>(
        this IServiceCollection services,
        Action<WinFormsApplicationOptions>? configure = null)
        where TForm : Form
    {
        ArgumentNullException.ThrowIfNull(services);
        WinFormsApplicationOptions options = new();
        options.SetStartupFormFactory(serviceProvider =>
            ActivatorUtilities.CreateInstance<TForm>(serviceProvider));
        configure?.Invoke(options);
        return AddLifetime(services, options);
    }

    /// <summary>
    ///  Adds a WinForms lifetime with an application context.
    /// </summary>
    public static IServiceCollection AddWindowsFormsLifetime<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TContext>(
        this IServiceCollection services,
        Func<IServiceProvider, TContext>? applicationContextFactory = null,
        Action<WinFormsApplicationOptions>? configure = null)
        where TContext : ApplicationContext
    {
        ArgumentNullException.ThrowIfNull(services);
        WinFormsApplicationOptions options = new();
        options.SetApplicationContextFactory(applicationContextFactory is null
            ? serviceProvider => ActivatorUtilities.CreateInstance<TContext>(serviceProvider)
            : serviceProvider => applicationContextFactory(serviceProvider));
        configure?.Invoke(options);
        return AddLifetime(services, options);
    }

    private static IServiceCollection AddLifetime(
        IServiceCollection services,
        WinFormsApplicationOptions options)
    {
        services.AddSingleton(options);
        services.AddWindowsFormsActivation();
        services.AddSingleton(serviceProvider =>
            new WinFormsApplicationLifetime(
                serviceProvider.GetRequiredService<Microsoft.Extensions.Hosting.IHostApplicationLifetime>()));
        services.AddSingleton<WinFormsHostedService>(serviceProvider =>
            new WinFormsHostedService(
                serviceProvider,
                serviceProvider.GetRequiredService<IHostApplicationLifetime>(),
                serviceProvider.GetRequiredService<WinFormsApplicationLifetime>(),
                options,
                serviceProvider.GetRequiredService<ILogger<WinFormsHostedService>>()));
        services.AddSingleton<IHostedService>(serviceProvider =>
            serviceProvider.GetRequiredService<WinFormsHostedService>());
        return services;
    }
}
