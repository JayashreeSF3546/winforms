// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Windows.Forms;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Builds a Generic Host that owns one WinForms UI thread and message loop.
/// </summary>
public sealed class WinFormsApplicationBuilder
{
    private readonly HostApplicationBuilder _hostBuilder;
    private bool _built;

    /// <summary>
    ///  Initializes a new instance of the <see cref="WinFormsApplicationBuilder"/> class.
    /// </summary>
    /// <param name="args">Optional command-line arguments used by the Generic Host.</param>
    public WinFormsApplicationBuilder(string[]? args = null)
    {
        _hostBuilder = Host.CreateApplicationBuilder(args ?? []);
        Options = new WinFormsApplicationOptions();
    }

    /// <summary>
    ///  Gets the services configured for the application.
    /// </summary>
    public IServiceCollection Services => _hostBuilder.Services;

    /// <summary>
    ///  Gets the application configuration.
    /// </summary>
    public ConfigurationManager Configuration => _hostBuilder.Configuration;

    /// <summary>
    ///  Gets the logging builder.
    /// </summary>
    public ILoggingBuilder Logging => _hostBuilder.Logging;

    /// <summary>
    ///  Gets the host environment.
    /// </summary>
    public IHostEnvironment Environment => _hostBuilder.Environment;

    /// <summary>
    ///  Gets the WinForms-specific options.
    /// </summary>
    public WinFormsApplicationOptions Options { get; }

    /// <summary>
    ///  Configures a startup form. The form is created on the UI thread by the
    ///  Generic Host service provider.
    /// </summary>
    public WinFormsApplicationBuilder UseStartupForm<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TForm>()
        where TForm : Form
    {
        Options.SetStartupFormFactory(serviceProvider =>
            ActivatorUtilities.CreateInstance<TForm>(serviceProvider));

        return this;
    }

    /// <summary>
    ///  Configures an existing form as the startup form.
    /// </summary>
    public WinFormsApplicationBuilder UseStartupForm(Form startupForm)
    {
        ArgumentNullException.ThrowIfNull(startupForm);
        Options.SetStartupFormFactory(_ => startupForm);

        return this;
    }

    /// <summary>
    ///  Configures an application context created on the UI thread.
    /// </summary>
    public WinFormsApplicationBuilder UseApplicationContext<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TContext>()
        where TContext : ApplicationContext
    {
        Options.SetApplicationContextFactory(serviceProvider =>
            ActivatorUtilities.CreateInstance<TContext>(serviceProvider));

        return this;
    }

    /// <summary>
    ///  Configures an application context factory.
    /// </summary>
    public WinFormsApplicationBuilder UseApplicationContext(
        Func<IServiceProvider, ApplicationContext> applicationContextFactory)
    {
        Options.SetApplicationContextFactory(applicationContextFactory);

        return this;
    }

    /// <summary>
    ///  Configures the high DPI mode applied before activation.
    /// </summary>
    public WinFormsApplicationBuilder UseHighDpiMode(HighDpiMode highDpiMode)
    {
        Options.HighDpiMode = highDpiMode;
        return this;
    }

    /// <summary>
    ///  Configures whether visual styles are enabled before activation.
    /// </summary>
    public WinFormsApplicationBuilder UseVisualStyles(bool enable = true)
    {
        Options.EnableVisualStyles = enable;
        return this;
    }

    /// <summary>
    ///  Configures the compatible text rendering default.
    /// </summary>
    public WinFormsApplicationBuilder UseCompatibleTextRenderingDefault(bool enable = true)
    {
        Options.UseCompatibleTextRenderingDefault = enable;
        return this;
    }

    /// <summary>
    ///  Configures the default font applied before activation.
    /// </summary>
    public WinFormsApplicationBuilder UseDefaultFont(Font defaultFont)
    {
        ArgumentNullException.ThrowIfNull(defaultFont);
        Options.DefaultFont = defaultFont;
        return this;
    }

    /// <summary>
    ///  Configures the application color mode.
    /// </summary>
    public WinFormsApplicationBuilder UseColorMode(SystemColorMode colorMode)
    {
        Options.ColorMode = colorMode;
        return this;
    }

    /// <summary>
    ///  Builds the configured WinForms application.
    /// </summary>
    public WinFormsApplication Build()
    {
        if (_built)
        {
            throw new InvalidOperationException("This application builder has already built an application.");
        }

        _built = true;
        WinFormsApplicationOptions options = Options.Clone();
        Services.AddSingleton(options);
        Services.AddSingleton(serviceProvider =>
            new WinFormsApplicationLifetime(
                serviceProvider.GetRequiredService<IHostApplicationLifetime>()));
        Services.AddSingleton(serviceProvider =>
            new WinFormsHostedService(
                serviceProvider,
                serviceProvider.GetRequiredService<IHostApplicationLifetime>(),
                serviceProvider.GetRequiredService<WinFormsApplicationLifetime>(),
                options,
                serviceProvider.GetRequiredService<ILogger<WinFormsHostedService>>()));
        Services.AddSingleton<IHostedService>(serviceProvider =>
            serviceProvider.GetRequiredService<WinFormsHostedService>());

        IHost host = _hostBuilder.Build();
        return new WinFormsApplication(host);
    }
}
