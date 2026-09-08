// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Windows.Forms;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Configures the WinForms message loop hosted by <see cref="WinFormsApplication"/>.
/// </summary>
public sealed class WinFormsApplicationOptions
{
    /// <summary>
    ///  Gets or sets the high DPI mode applied before the first form is created.
    /// </summary>
    public HighDpiMode HighDpiMode { get; set; } = HighDpiMode.SystemAware;

    /// <summary>
    ///  Gets or sets a value indicating whether visual styles are enabled.
    /// </summary>
    public bool EnableVisualStyles { get; set; } = true;

    /// <summary>
    ///  Gets or sets a value indicating whether compatible text rendering is enabled.
    /// </summary>
    public bool UseCompatibleTextRenderingDefault { get; set; }

    /// <summary>
    ///  Gets or sets the default application font.
    /// </summary>
    public Font? DefaultFont { get; set; }

    /// <summary>
    ///  Gets or sets the application color mode.
    /// </summary>
    public SystemColorMode? ColorMode { get; set; }

    /// <summary>
    ///  Gets the startup form factory, when one has been configured.
    /// </summary>
    internal Func<IServiceProvider, Form>? StartupFormFactory { get; private set; }

    /// <summary>
    ///  Gets the application context factory.
    /// </summary>
    internal Func<IServiceProvider, ApplicationContext>? ApplicationContextFactory { get; private set; }

    internal void SetStartupFormFactory(Func<IServiceProvider, Form> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ThrowIfApplicationContextConfigured();
        StartupFormFactory = factory;
    }

    internal void SetApplicationContextFactory(Func<IServiceProvider, ApplicationContext> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (StartupFormFactory is not null)
        {
            throw new InvalidOperationException(
                "A startup form and an application context cannot both be configured.");
        }

        ApplicationContextFactory = factory;
    }

    internal WinFormsApplicationOptions Clone()
        => new()
        {
            HighDpiMode = HighDpiMode,
            EnableVisualStyles = EnableVisualStyles,
            UseCompatibleTextRenderingDefault = UseCompatibleTextRenderingDefault,
            DefaultFont = DefaultFont,
            ColorMode = ColorMode,
            StartupFormFactory = StartupFormFactory,
            ApplicationContextFactory = ApplicationContextFactory
        };

    private void ThrowIfApplicationContextConfigured()
    {
        if (ApplicationContextFactory is not null)
        {
            throw new InvalidOperationException(
                "An application context and a startup form cannot both be configured.");
        }
    }
}
