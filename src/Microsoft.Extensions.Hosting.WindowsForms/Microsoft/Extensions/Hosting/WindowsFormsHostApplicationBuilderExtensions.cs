// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.WinForms;
using System.Windows.Forms;

namespace Microsoft.Extensions.Hosting;

/// <summary>
///  Adds the WinForms lifetime coordinator to an existing Generic Host builder.
/// </summary>
public static class WindowsFormsHostApplicationBuilderExtensions
{
    /// <summary>
    ///  Configures a startup form for a conventional Generic Host builder.
    /// </summary>
    public static IHostBuilder UseWindowsFormsLifetime<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TForm>(
        this IHostBuilder builder,
        Action<WinFormsApplicationOptions>? configure = null)
        where TForm : Form
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ConfigureServices((_, services) =>
        {
            WinFormsApplicationOptions options = new();
            options.SetStartupFormFactory(serviceProvider =>
                ActivatorUtilities.CreateInstance<TForm>(serviceProvider));
            configure?.Invoke(options);
            services.AddSingleton(options);
            services.AddWindowsFormsActivation();
            services.AddSingleton(serviceProvider =>
                new WinFormsApplicationLifetime(
                    serviceProvider.GetRequiredService<IHostApplicationLifetime>()));
            services.AddSingleton<WinFormsHostedService>(serviceProvider =>
                new WinFormsHostedService(
                    serviceProvider,
                    serviceProvider.GetRequiredService<IHostApplicationLifetime>(),
                    serviceProvider.GetRequiredService<WinFormsApplicationLifetime>(),
                    options,
                    serviceProvider.GetRequiredService<ILogger<WinFormsHostedService>>()));
            services.AddSingleton<IHostedService>(serviceProvider =>
                serviceProvider.GetRequiredService<WinFormsHostedService>());
        });

        return builder;
    }

    /// <summary>
    ///  Configures a startup form for an application builder.
    /// </summary>
    public static IHostApplicationBuilder UseWindowsFormsLifetime<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TForm>(
        this IHostApplicationBuilder builder,
        Action<WinFormsApplicationOptions>? configure = null)
        where TForm : Form
    {
        ArgumentNullException.ThrowIfNull(builder);
        WinFormsApplicationOptions options = new();
        options.SetStartupFormFactory(serviceProvider =>
            ActivatorUtilities.CreateInstance<TForm>(serviceProvider));
        configure?.Invoke(options);
        builder.Services.AddSingleton(options);
        builder.Services.AddWindowsFormsActivation();
        builder.Services.AddSingleton(serviceProvider =>
            new WinFormsApplicationLifetime(
                serviceProvider.GetRequiredService<IHostApplicationLifetime>()));
        builder.Services.AddSingleton<WinFormsHostedService>(serviceProvider =>
            new WinFormsHostedService(
                serviceProvider,
                serviceProvider.GetRequiredService<IHostApplicationLifetime>(),
                serviceProvider.GetRequiredService<WinFormsApplicationLifetime>(),
                options,
                serviceProvider.GetRequiredService<ILogger<WinFormsHostedService>>()));
        builder.Services.AddSingleton<IHostedService>(serviceProvider =>
            serviceProvider.GetRequiredService<WinFormsHostedService>());
        return builder;
    }

    /// <summary>
    ///  Configures an application context for an application builder.
    /// </summary>
    public static IHostApplicationBuilder UseWindowsFormsLifetime<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TContext>(
        this IHostApplicationBuilder builder,
        Func<IServiceProvider, TContext>? applicationContextFactory = null,
        Action<WinFormsApplicationOptions>? configure = null)
        where TContext : ApplicationContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        WinFormsApplicationOptions options = new();
        options.SetApplicationContextFactory(applicationContextFactory is null
            ? serviceProvider => ActivatorUtilities.CreateInstance<TContext>(serviceProvider)
            : serviceProvider => applicationContextFactory(serviceProvider));
        configure?.Invoke(options);
        builder.Services.AddSingleton(options);
        builder.Services.AddWindowsFormsActivation();
        builder.Services.AddSingleton(serviceProvider =>
            new WinFormsApplicationLifetime(
                serviceProvider.GetRequiredService<IHostApplicationLifetime>()));
        builder.Services.AddSingleton<WinFormsHostedService>(serviceProvider =>
            new WinFormsHostedService(
                serviceProvider,
                serviceProvider.GetRequiredService<IHostApplicationLifetime>(),
                serviceProvider.GetRequiredService<WinFormsApplicationLifetime>(),
                options,
                serviceProvider.GetRequiredService<ILogger<WinFormsHostedService>>()));
        builder.Services.AddSingleton<IHostedService>(serviceProvider =>
            serviceProvider.GetRequiredService<WinFormsHostedService>());
        return builder;
    }
}
