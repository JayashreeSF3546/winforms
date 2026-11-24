// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;

namespace Microsoft.Extensions.WinForms;

public sealed partial class WinFormsApplicationBuilder
{
    private StartupConfiguration? _startupConfiguration;

    /// <summary>
    ///  Selects process DPI awareness to apply before host construction and UI initialization.
    /// </summary>
    /// <param name="highDpiMode">The requested process DPI awareness.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="InvalidEnumArgumentException">The mode is invalid.</exception>
    /// <exception cref="InvalidOperationException">The builder is built or belongs to another thread.</exception>
    /// <remarks>
    ///  <para>
    ///   Build throws if <see cref="Application.SetHighDpiMode"/> returns false. Do not initialize DPI separately.
    ///   Startup settings require deferred UI and an owning STA without previously created windows.
    ///   They change process/thread defaults during Build and are not restored on disposal.
    ///  </para>
    /// </remarks>
    public WinFormsApplicationBuilder UseHighDpiMode(HighDpiMode highDpiMode)
    {
        VerifyCanConfigure();
        ValidateStartupEnum(highDpiMode, nameof(highDpiMode));
        (_startupConfiguration ??= new()).HighDpiMode = highDpiMode;

        return this;
    }

    /// <summary>
    ///  Selects the application color mode to apply during Build.
    /// </summary>
    /// <param name="colorMode">The application color mode.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="InvalidEnumArgumentException">The mode is invalid.</exception>
    /// <exception cref="InvalidOperationException">The builder is built or belongs to another thread.</exception>
    public WinFormsApplicationBuilder UseColorMode(SystemColorMode colorMode)
    {
        VerifyCanConfigure();
        ValidateStartupEnum(colorMode, nameof(colorMode));
        (_startupConfiguration ??= new()).ColorMode = colorMode;

        return this;
    }

    /// <summary>
    ///  Selects the compatible text-rendering default to apply during Build.
    /// </summary>
    /// <param name="useCompatibleTextRendering">Whether supported controls default to compatible text rendering.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="InvalidOperationException">The builder is built or belongs to another thread.</exception>
    public WinFormsApplicationBuilder UseTextRendering(bool useCompatibleTextRendering)
    {
        VerifyCanConfigure();
        (_startupConfiguration ??= new()).UseCompatibleTextRendering = useCompatibleTextRendering;

        return this;
    }

    /// <summary>
    ///  Selects operating-system visual-style enablement through Application.EnableVisualStyles during Build.
    /// </summary>
    /// <returns>This builder.</returns>
    /// <exception cref="InvalidOperationException">The builder is built or belongs to another thread.</exception>
    /// <remarks>
    ///  <para>This does not select the WinForms rendering version or disable previously enabled styles.</para>
    /// </remarks>
    public WinFormsApplicationBuilder UseLegacyVisualStyles()
    {
        VerifyCanConfigure();
        (_startupConfiguration ??= new()).EnableVisualStyles = true;

        return this;
    }

    /// <summary>
    ///  Selects the default top-level Form reveal mode to apply during Build.
    /// </summary>
    /// <param name="formRevealMode">The default Form reveal mode.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="InvalidEnumArgumentException">The mode is invalid.</exception>
    /// <exception cref="InvalidOperationException">The builder is built or belongs to another thread.</exception>
    public WinFormsApplicationBuilder UseFormRevealMode(FormRevealMode formRevealMode)
    {
        VerifyCanConfigure();
        ValidateStartupEnum(formRevealMode, nameof(formRevealMode));
        (_startupConfiguration ??= new()).FormRevealMode = formRevealMode;

        return this;
    }

    /// <summary>
    ///  Selects the default WinForms visual-styles mode to apply during Build.
    /// </summary>
    /// <param name="visualStylesMode">The default mode, excluding Inherit.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="InvalidEnumArgumentException">The mode is undefined.</exception>
    /// <exception cref="ArgumentException">The mode is Inherit.</exception>
    /// <exception cref="InvalidOperationException">The builder is built or belongs to another thread.</exception>
    /// <remarks>
    ///  <para>The underlying Application default is write-once; conflicting prior initialization fails at Build.</para>
    /// </remarks>
    public WinFormsApplicationBuilder UseVisualStylesMode(VisualStylesMode visualStylesMode)
    {
        VerifyCanConfigure();
        ValidateStartupEnum(visualStylesMode, nameof(visualStylesMode));
        if (visualStylesMode == VisualStylesMode.Inherit)
        {
            throw new ArgumentException(SR.Application_VisualStylesModeInheritInvalidAsDefault, nameof(visualStylesMode));
        }

        (_startupConfiguration ??= new()).VisualStylesMode = visualStylesMode;

        return this;
    }

    /// <summary>
    ///  Selects the process default font to apply after DPI configuration during Build.
    /// </summary>
    /// <param name="font">A caller-owned font that must remain valid while used as the process default.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">The font is null.</exception>
    /// <exception cref="InvalidOperationException">The builder is built or belongs to another thread.</exception>
    /// <remarks>
    ///  <para>The builder and application do not dispose or restore the font.</para>
    /// </remarks>
    public WinFormsApplicationBuilder UseDefaultFont(Font font)
    {
        VerifyCanConfigure();
        ArgumentNullException.ThrowIfNull(font);
        (_startupConfiguration ??= new()).DefaultFont = font;

        return this;
    }

    /// <summary>
    ///  Selects the owning thread's UI exception-routing mode to apply during Build.
    /// </summary>
    /// <param name="mode">The UI exception-routing mode.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="InvalidEnumArgumentException">The mode is invalid.</exception>
    /// <exception cref="InvalidOperationException">The builder is built or belongs to another thread.</exception>
    /// <remarks>
    ///  <para>This does not attach an exception handler or configure logging.</para>
    /// </remarks>
    public WinFormsApplicationBuilder UseUnhandledExceptionMode(UnhandledExceptionMode mode)
    {
        VerifyCanConfigure();
        ValidateStartupEnum(mode, nameof(mode));
        (_startupConfiguration ??= new()).UnhandledExceptionMode = mode;

        return this;
    }

    /// <summary>
    ///  Selects the operating-system visual-style state to apply during Build.
    /// </summary>
    /// <param name="visualStyleState">The client and nonclient visual-style selection.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="InvalidEnumArgumentException">The state is invalid.</exception>
    /// <exception cref="InvalidOperationException">The builder is built or belongs to another thread.</exception>
    public WinFormsApplicationBuilder UseVisualStyleState(VisualStyleState visualStyleState)
    {
        VerifyCanConfigure();
        ValidateStartupEnum(visualStyleState, nameof(visualStyleState));
        (_startupConfiguration ??= new()).VisualStyleState = visualStyleState;

        return this;
    }

    private static void ValidateStartupEnum<T>(T value, string parameterName) where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new InvalidEnumArgumentException(parameterName, Convert.ToInt32(value), typeof(T));
        }
    }

    private void ApplyStartupConfiguration()
    {
        if (_startupConfiguration is not { } configuration)
        {
            return;
        }

        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            throw new InvalidOperationException(SR.ThreadMustBeSTA);
        }

        if (_startupForm is not null || NativeWindow.AnyHandleCreated)
        {
            throw new InvalidOperationException(SR.WinFormsApplication_StartupConfigurationTooLate);
        }

        // Application setters mutate process/thread state and cannot be rolled back after failure.
        _built = true;
        _services?.MakeReadOnly();
        configuration.Apply();
        _startupConfiguration = null;
    }

    /// <summary>
    ///  Records explicit selections and applies them in initialization order without owning their resources.
    /// </summary>
    private sealed class StartupConfiguration
    {
        internal HighDpiMode? HighDpiMode { get; set; }
        internal SystemColorMode? ColorMode { get; set; }
        internal bool? UseCompatibleTextRendering { get; set; }
        internal bool EnableVisualStyles { get; set; }
        internal FormRevealMode? FormRevealMode { get; set; }
        internal VisualStylesMode? VisualStylesMode { get; set; }
        internal Font? DefaultFont { get; set; }
        internal UnhandledExceptionMode? UnhandledExceptionMode { get; set; }
        internal VisualStyleState? VisualStyleState { get; set; }

        internal void Apply()
        {
            if (HighDpiMode is { } dpi && !Application.SetHighDpiMode(dpi))
            {
                throw new InvalidOperationException(SR.WinFormsApplication_HighDpiConfigurationFailed);
            }

            if (EnableVisualStyles)
            {
                Application.EnableVisualStyles();
            }

            if (UseCompatibleTextRendering is { } textRendering)
            {
                Application.SetCompatibleTextRenderingDefault(textRendering);
            }

            if (DefaultFont is { } font)
            {
                Application.SetDefaultFont(font);
            }

            if (ColorMode is { } color)
            {
                Application.SetColorMode(color);
            }

            if (VisualStylesMode is { } styles)
            {
                Application.SetDefaultVisualStylesMode(styles);
            }

            if (FormRevealMode is { } reveal)
            {
                Application.SetDefaultFormRevealMode(reveal);
            }

            if (VisualStyleState is { } state)
            {
                Application.VisualStyleState = state;
            }

            if (UnhandledExceptionMode is { } exceptionMode)
            {
                Application.SetUnhandledExceptionMode(exceptionMode);
            }
        }
    }
}
