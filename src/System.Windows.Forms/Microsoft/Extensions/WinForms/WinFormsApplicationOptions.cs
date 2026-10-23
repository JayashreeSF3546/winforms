// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.ObjectModel;
using System.Windows.Forms;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Stores the options used to build a Windows Forms application.
/// </summary>
internal sealed class WinFormsApplicationOptions
{
    /// <summary>
    ///  Gets or sets the factory for creating the startup form.
    /// </summary>
    internal Func<Form>? StartupFormFactory { get; set; }

    /// <summary>
    ///  Gets or sets the service-aware factory for creating the startup form.
    /// </summary>
    internal Func<IServiceProvider, Form>? StartupServiceFormFactory { get; set; }

    /// <summary>
    ///  Gets or sets the existing startup form.
    /// </summary>
    internal Form? StartupForm { get; set; }

    /// <summary>
    ///  Gets or sets the factory for creating the splash form.
    /// </summary>
    internal Func<Form>? SplashScreenFactory { get; set; }

    /// <summary>
    ///  Gets or sets the minimum splash display duration.
    /// </summary>
    internal TimeSpan MinimumSplashScreenDisplayDuration { get; set; }

    /// <summary>
    ///  Gets or sets the factory for creating the application context.
    /// </summary>
    internal Func<ApplicationContext>? ApplicationContextFactory { get; set; }

    /// <summary>
    ///  Gets or sets the existing application context.
    /// </summary>
    internal ApplicationContext? ApplicationContext { get; set; }

    /// <summary>
    ///  Gets or sets the thread that configured a caller-supplied startup object.
    /// </summary>
    internal Thread? StartupObjectThread { get; set; }

    /// <summary>
    ///  Gets or sets the generic host coordinated by the application.
    /// </summary>
    internal IHost? Host { get; set; }

    /// <summary>
    ///  Gets or sets the command-line arguments supplied to the application.
    /// </summary>
    internal ReadOnlyCollection<string> CommandLineArgs { get; set; } = Array.AsReadOnly(Array.Empty<string>());

    /// <summary>
    ///  Gets or sets whether the application accepts forwarded single-instance activations.
    /// </summary>
    internal bool UseSingleInstance { get; set; }

    /// <summary>
    ///  Gets or sets the explicit identity used by single-instance applications.
    /// </summary>
    internal string? SingleInstanceApplicationId { get; set; }

    /// <summary>
    ///  Gets or sets whether the host was supplied by the caller.
    /// </summary>
    internal bool HostWasSupplied { get; set; }

    /// <summary>
    ///  Gets or sets whether WinForms and CLR exceptions are forwarded to the host logger.
    /// </summary>
    internal bool EnableExceptionLogging { get; set; }

    /// <summary>
    ///  Creates a copy of these options.
    /// </summary>
    /// <returns>A new options instance with the same configured startup target.</returns>
    internal WinFormsApplicationOptions Clone()
        => new()
        {
            StartupFormFactory = StartupFormFactory,
            StartupServiceFormFactory = StartupServiceFormFactory,
            StartupForm = StartupForm,
            SplashScreenFactory = SplashScreenFactory,
            MinimumSplashScreenDisplayDuration = MinimumSplashScreenDisplayDuration,
            ApplicationContextFactory = ApplicationContextFactory,
            ApplicationContext = ApplicationContext,
            StartupObjectThread = StartupObjectThread,
            Host = Host,
            HostWasSupplied = HostWasSupplied,
            CommandLineArgs = CommandLineArgs,
            UseSingleInstance = UseSingleInstance,
            SingleInstanceApplicationId = SingleInstanceApplicationId,
            EnableExceptionLogging = EnableExceptionLogging
        };
}
