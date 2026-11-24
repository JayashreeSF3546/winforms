// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Stages standard application configuration providers for the Windows Forms application's selected host.
/// </summary>
public static class WinFormsApplicationBuilderConfigurationExtensions
{
    private static readonly ConditionalWeakTable<WinFormsApplicationBuilder, IConfigurationBuilder> s_builders = new();

    extension(WinFormsApplicationBuilder builder)
    {
        /// <summary>
        ///  Gets the staged configuration builder whose sources are appended to the selected host during Build.
        /// </summary>
        /// <remarks>
        ///  <para>
        ///   Reading this property loads no providers, selects no defaults, and preserves hostless behavior.
        ///   Add providers with standard IConfigurationBuilder extensions. Nonempty configuration requires
        ///   an unbuilt host selected with UseHost. Effective values are available through that host's
        ///   IConfiguration after application Build, not through this provider recipe.
        ///  </para>
        ///  <para>
        ///   Sources.Clear removes only staged sources, not the supplied host's configuration. Retained
        ///   builders remain ordinary configuration builders; later recipe edits do not update a built host.
        ///   Do not call Build here to obtain the application's configuration: that creates a separate,
        ///   caller-owned root. Host identity must be configured on IHostBuilder before application Build.
        ///  </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        ///  The builder is consumed, belongs to another thread, or already selected another configuration builder.
        /// </exception>
        public IConfigurationBuilder Configuration
        {
            get
            {
                ArgumentNullException.ThrowIfNull(builder);
                _ = builder.Services;

                return s_builders.GetValue(builder, static applicationBuilder =>
                {
                    ConfigurationBuilder configuration = new();
                    applicationBuilder.UseConfiguration(configuration);

                    return configuration;
                });
            }
        }
    }

    /// <summary>
    ///  Configures staged application providers with a callback usable from C# and Visual Basic.
    /// </summary>
    /// <param name="builder">The unbuilt Windows Forms application builder.</param>
    /// <param name="configure">Adds standard configuration sources or changes staged builder properties.</param>
    /// <returns>The supplied application builder.</returns>
    /// <remarks>
    ///  <para>
    ///   The callback runs immediately over the same recipe as the Configuration extension property.
    ///   Providers are loaded by the selected host during application Build. Callback failures propagate
    ///   without consuming the application builder; edits made before failure remain.
    ///  </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="InvalidOperationException">The configuration property is unavailable.</exception>
    public static WinFormsApplicationBuilder ConfigureConfiguration(
        this WinFormsApplicationBuilder builder,
        Action<IConfigurationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        configure(builder.Configuration);

        return builder;
    }
}
