// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Configures standard logging through the Windows Forms application's existing service collection.
/// </summary>
public static class WinFormsApplicationBuilderLoggingExtensions
{
    private static readonly ConditionalWeakTable<WinFormsApplicationBuilder, ApplicationLoggingBuilder> s_builders = new();

    extension(WinFormsApplicationBuilder builder)
    {
        /// <summary>
        ///  Gets the standard logging builder backed by the application's services.
        /// </summary>
        /// <remarks>
        ///  <para>
        ///   Access selects no providers and creates no loggers or service provider. The selected Generic
        ///   Host supplies logging services. Select an unbuilt host with UseHost before Build when adding
        ///   logging registrations. Access on the builder's owning thread before Build; retained service
        ///   collections become read-only with the builder.
        ///  </para>
        ///  <para>
        ///   This C# extension property is supplied by the optional hosting package. Other languages can
        ///   use ConfigureLogging. Logging destinations and exception handling remain explicit choices.
        ///   Registrations are appended to the selected host. Clear or replace that host's own providers
        ///   through IHostBuilder.ConfigureLogging, not through this staged service collection.
        ///  </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
        /// <exception cref="InvalidOperationException">The builder is consumed or belongs to another thread.</exception>
        public ILoggingBuilder Logging
        {
            get
            {
                ArgumentNullException.ThrowIfNull(builder);
                _ = builder.Services;
                return s_builders.GetValue(builder, static applicationBuilder =>
                {
                    IServiceCollection services = applicationBuilder.Services;
                    return new ApplicationLoggingBuilder(services);
                });
            }
        }
    }

    /// <summary>
    ///  Configures logging with a callback usable from both C# and Visual Basic.
    /// </summary>
    /// <param name="builder">The unbuilt Windows Forms application builder.</param>
    /// <param name="configure">Configures standard providers, filters, options, or configuration binding.</param>
    /// <returns>The supplied application builder.</returns>
    /// <remarks>
    ///  <para>
    ///   Uses the same logging builder and service collection as the C# Logging property. The callback
    ///   runs immediately. Callback failures propagate; any registrations already made remain in the collection.
    ///   This method does not select defaults, start logging, or subscribe to exception notifications.
    ///  </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="InvalidOperationException">The builder is consumed or belongs to another thread.</exception>
    public static WinFormsApplicationBuilder ConfigureLogging(
        this WinFormsApplicationBuilder builder,
        Action<ILoggingBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        configure(builder.Logging);
        return builder;
    }

    /// <summary>
    ///  Borrows the builder's service collection without owning a logger factory or provider.
    /// </summary>
    private sealed class ApplicationLoggingBuilder(IServiceCollection services) : ILoggingBuilder
    {
        public IServiceCollection Services { get; } = services;
    }
}
