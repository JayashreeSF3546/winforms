// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Configures deferred host identity overrides for a Windows Forms application.
/// </summary>
public static class WinFormsApplicationBuilderEnvironmentExtensions
{
    private static readonly ConditionalWeakTable<WinFormsApplicationBuilder, WinFormsApplicationEnvironmentOptions> s_options = new();

    extension(WinFormsApplicationBuilder builder)
    {
        /// <summary>
        ///  Gets the staged environment, application-name, and content-root overrides.
        /// </summary>
        /// <remarks>
        ///  <para>
        ///   Unset values inherit the selected host. Reading this property does not create an effective
        ///   IHostEnvironment, load configuration, open a file provider, or enable hosting. Non-null
        ///   overrides require UseHost at Build and take precedence over that host's host configuration.
        ///   Resolve the effective IHostEnvironment through host services after application Build.
        ///  </para>
        ///  <para>
        ///   Use ConfigureEnvironment from Visual Basic. Select either these options or an explicit
        ///   UseHostConfiguration recipe, not both. Access and edit only on the owning thread before Build.
        ///  </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        ///  The builder is consumed, belongs to another thread, or already selected another host configuration recipe.
        /// </exception>
        public WinFormsApplicationEnvironmentOptions Environment
        {
            get
            {
                ArgumentNullException.ThrowIfNull(builder);
                _ = builder.Services;

                return s_options.GetValue(builder, static applicationBuilder => new(applicationBuilder));
            }
        }
    }

    /// <summary>
    ///  Configures staged host identity with a callback usable from C# and Visual Basic.
    /// </summary>
    /// <param name="builder">The unbuilt Windows Forms application builder.</param>
    /// <param name="configure">Sets explicit overrides; null option values inherit the selected host's settings.</param>
    /// <returns>The supplied application builder.</returns>
    /// <remarks>
    ///  <para>
    ///   The callback runs immediately over the same options as the Environment extension property.
    ///   Callback failures propagate without consuming the builder or undoing prior valid edits.
    ///  </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="InvalidOperationException">The environment options are unavailable.</exception>
    public static WinFormsApplicationBuilder ConfigureEnvironment(
        this WinFormsApplicationBuilder builder,
        Action<WinFormsApplicationEnvironmentOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        configure(builder.Environment);

        return builder;
    }
}
