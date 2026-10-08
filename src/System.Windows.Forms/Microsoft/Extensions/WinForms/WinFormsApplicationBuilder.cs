// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Forms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  A builder for a Windows Forms application.
/// </summary>
/// <remarks>
///  <para>
///   The builder records the startup UI object without creating it. The
///   application runtime is responsible for activation on the UI thread.
///  </para>
///  <para>
///   Calling a startup selection method replaces any selection previously
///   made on this builder.
///  </para>
///  <para>
///   Accessing a host-building property configures a builder-owned Generic Host.
///   This mode cannot be combined with <see cref="UseHost(IHost)"/>.
///  </para>
/// </remarks>
public sealed class WinFormsApplicationBuilder : IHostApplicationBuilder
{
    private readonly WinFormsApplicationOptions _options = new();
    private HostApplicationBuilder? _hostBuilder;
    private bool _hostBuilt;

    internal WinFormsApplicationBuilder()
    {
    }

    private WinFormsApplicationBuilder(HostApplicationBuilderSettings settings)
    {
        _hostBuilder = CreateHostBuilder(settings);
    }

    /// <summary>
    ///  Creates a builder for a Windows Forms application.
    /// </summary>
    /// <returns>A new application builder.</returns>
    public static WinFormsApplicationBuilder CreateBuilder()
        => new();

    /// <summary>
    ///  Creates a builder for a Windows Forms application using the specified
    ///  command-line arguments.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>A new application builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="args"/> is null.</exception>
    public static WinFormsApplicationBuilder CreateBuilder(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        return new(new HostApplicationBuilderSettings
        {
            Args = [.. args],
            ContentRootPath = AppContext.BaseDirectory
        });
    }

    /// <summary>
    ///  Creates a builder for a Windows Forms application using the specified
    ///  host settings.
    /// </summary>
    /// <param name="settings">The host settings.</param>
    /// <returns>A new application builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="settings"/> is null.</exception>
    public static WinFormsApplicationBuilder CreateBuilder(HostApplicationBuilderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new(settings);
    }

    /// <summary>
    ///  Gets the configuration for the application.
    /// </summary>
    public IConfigurationManager Configuration => GetHostBuilder().Configuration;

    /// <summary>
    ///  Gets the hosting environment for the application.
    /// </summary>
    public IHostEnvironment Environment => GetHostBuilder().Environment;

    /// <summary>
    ///  Gets the logging builder for the application.
    /// </summary>
    public ILoggingBuilder Logging => GetHostBuilder().Logging;

    /// <summary>
    ///  Gets the metrics builder for the application.
    /// </summary>
    public IMetricsBuilder Metrics => GetHostBuilder().Metrics;

    /// <summary>
    ///  Gets the shared properties for the host-building process.
    /// </summary>
    public IDictionary<object, object> Properties
        => ((IHostApplicationBuilder)GetHostBuilder()).Properties;

    /// <summary>
    ///  Gets the service collection for the application.
    /// </summary>
    public IServiceCollection Services => GetHostBuilder().Services;

    /// <summary>
    ///  Configures the service provider factory and container.
    /// </summary>
    /// <typeparam name="TContainerBuilder">The container builder type.</typeparam>
    /// <param name="factory">The service provider factory.</param>
    /// <param name="configure">The container configuration callback.</param>
    public void ConfigureContainer<TContainerBuilder>(
        IServiceProviderFactory<TContainerBuilder> factory,
        Action<TContainerBuilder>? configure = null)
        where TContainerBuilder : notnull
        => GetHostBuilder().ConfigureContainer(factory, configure);

    /// <summary>
    ///  Selects a startup form to be created by the application runtime.
    /// </summary>
    /// <typeparam name="TForm">The type of the startup form.</typeparam>
    /// <returns>This builder.</returns>
    public WinFormsApplicationBuilder UseStartupForm<TForm>()
        where TForm : Form, new()
    {
        _options.StartupFormFactory = static () => new TForm();
        _options.StartupServiceFormFactory = null;
        _options.StartupForm = null;
        _options.ApplicationContextFactory = null;
        _options.ApplicationContext = null;
        _options.StartupObjectThread = null;

        return this;
    }

    /// <summary>
    ///  Selects a startup form to be created by a factory using the form's
    ///  activation scope.
    /// </summary>
    /// <typeparam name="TForm">The type of the startup form.</typeparam>
    /// <param name="factory">
    ///  A factory that creates the startup form using its activation services.
    /// </param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    ///  The factory returns null when the application runs.
    /// </exception>
    public WinFormsApplicationBuilder UseStartupForm<TForm>(
        Func<IServiceProvider, TForm> factory)
        where TForm : Form
    {
        ArgumentNullException.ThrowIfNull(factory);

        _options.StartupFormFactory = null;
        _options.StartupServiceFormFactory = services => factory(services);
        _options.StartupForm = null;
        _options.ApplicationContextFactory = null;
        _options.ApplicationContext = null;
        _options.StartupObjectThread = null;

        return this;
    }

    /// <summary>
    ///  Selects an existing form as the startup form.
    /// </summary>
    /// <param name="startupForm">The startup form.</param>
    /// <returns>This builder.</returns>
    public WinFormsApplicationBuilder UseStartupForm(Form startupForm)
    {
        ArgumentNullException.ThrowIfNull(startupForm);

        _options.StartupFormFactory = null;
        _options.StartupServiceFormFactory = null;
        _options.StartupForm = startupForm;
        _options.ApplicationContextFactory = null;
        _options.ApplicationContext = null;
        _options.StartupObjectThread = Thread.CurrentThread;

        return this;
    }

    /// <summary>
    ///  Selects a default <see cref="ApplicationContext"/> for the application.
    /// </summary>
    /// <returns>This builder.</returns>
    public WinFormsApplicationBuilder UseApplicationContext()
    {
        _options.StartupFormFactory = null;
        _options.StartupServiceFormFactory = null;
        _options.StartupForm = null;
        _options.ApplicationContextFactory = static () => new();
        _options.ApplicationContext = null;
        _options.StartupObjectThread = null;

        return this;
    }

    /// <summary>
    ///  Selects an existing application context for the application.
    /// </summary>
    /// <param name="applicationContext">The application context.</param>
    /// <returns>This builder.</returns>
    public WinFormsApplicationBuilder UseApplicationContext(ApplicationContext applicationContext)
    {
        ArgumentNullException.ThrowIfNull(applicationContext);

        _options.StartupFormFactory = null;
        _options.StartupServiceFormFactory = null;
        _options.StartupForm = null;
        _options.ApplicationContextFactory = null;
        _options.ApplicationContext = applicationContext;
        _options.StartupObjectThread = Thread.CurrentThread;

        return this;
    }

    /// <summary>
    ///  Associates a Generic Host with the application.
    /// </summary>
    /// <param name="host">The host to start and stop with the application.</param>
    /// <returns>This builder.</returns>
    /// <remarks>
    ///  <para>
    ///   The application takes ownership of the host and disposes it when the
    ///   application is disposed.
    ///  </para>
    ///  <para>
    ///   A host supplied here cannot be combined with host configuration or
    ///   service registration through this builder.
    ///  </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    ///  A builder-owned host has already been initialized.
    /// </exception>
    public WinFormsApplicationBuilder UseHost(IHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (_hostBuilder is not null)
        {
            throw new InvalidOperationException(
                "A supplied host cannot be combined with host configuration on this builder.");
        }

        _options.Host = host;

        return this;
    }

    /// <summary>
    ///  Enables opt-in logging for unhandled WinForms and CLR exceptions.
    /// </summary>
    /// <returns>This builder.</returns>
    /// <remarks>
    ///  <para>
    ///   When the application runs, exceptions raised through
    ///   <see cref="Application.ThreadException"/>,
    ///   <see cref="AppDomain.UnhandledException"/>, and
    ///   <see cref="TaskScheduler.UnobservedTaskException"/> are forwarded to
    ///   the host's <see cref="ILogger{TCategoryName}"/>. The integration is
    ///   disabled by default.
    ///  </para>
    ///  <para>
    ///   Subscribing to <see cref="Application.ThreadException"/> installs
    ///   custom WinForms exception handling. UI-thread exceptions are logged
    ///   and treated as handled; the default WinForms exception dialog is not
    ///   shown. Applications that need user-facing error UI should provide
    ///   their own <see cref="Application.ThreadException"/> handler instead.
    ///  </para>
    ///  <para>
    ///   Unobserved task exceptions are logged without calling
    ///   <see cref="UnobservedTaskExceptionEventArgs.SetObserved"/>.
    ///   Logging includes exception details, which may contain application
    ///   data; configure providers and retention accordingly.
    ///  </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    ///  The builder has already been built.
    /// </exception>
    public WinFormsApplicationBuilder EnableExceptionLogging()
    {
        if (_hostBuilt)
        {
            throw new InvalidOperationException("Exception logging must be configured before the builder is built.");
        }

        if (_options.Host is null)
        {
            _ = Logging;
        }

        _options.EnableExceptionLogging = true;

        return this;
    }

    /// <summary>
    ///  Registers the default JSON user-settings service.
    /// </summary>
    /// <returns>This builder.</returns>
    public WinFormsApplicationBuilder AddUserSettings()
        => AddUserSettings(new UserSettingsOptions());

    /// <summary>
    ///  Registers the default JSON user-settings service with the specified options.
    /// </summary>
    /// <param name="options">The user-settings options.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    ///  A user-settings service is already registered or an external host was supplied.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///  The application identifier or explicit file path is invalid.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///  The schema version or lock timeout is invalid.
    /// </exception>
    public WinFormsApplicationBuilder AddUserSettings(UserSettingsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return AddUserSettingsService(
            new JsonUserSettingsService(options),
            includeInConfiguration: false,
            settingKeys: []);
    }

    /// <summary>
    ///  Registers the default JSON user-settings service with an optional configuration projection.
    /// </summary>
    /// <param name="options">The user-settings options.</param>
    /// <param name="includeInConfiguration">
    ///  <see langword="true"/> to project settings under
    ///  <c>UserSettings</c>; otherwise, <see langword="false"/>.
    /// </param>
    /// <param name="settingKeys">
    ///  Optional settings paths to project when
    ///  <paramref name="includeInConfiguration"/> is <see langword="true"/>.
    /// </param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">
    ///  <paramref name="options"/> or <paramref name="settingKeys"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///  A user-settings service is already registered or an external host was supplied.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///  The application identifier or explicit file path is invalid, or settings keys were provided
    ///  while configuration projection is disabled.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///  The schema version or lock timeout is invalid.
    /// </exception>
    public WinFormsApplicationBuilder AddUserSettings(
        UserSettingsOptions options,
        bool includeInConfiguration,
        params string[] settingKeys)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(settingKeys);

        return AddUserSettingsService(
            new JsonUserSettingsService(options),
            includeInConfiguration,
            settingKeys);
    }

    /// <summary>
    ///  Registers a user-settings service with the application.
    /// </summary>
    /// <param name="settingsService">The settings service.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">
    ///  <paramref name="settingsService"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///  A user-settings service is already registered or an external host was supplied.
    /// </exception>
    public WinFormsApplicationBuilder AddUserSettings(IUserSettingsService settingsService)
        => AddUserSettingsService(settingsService, includeInConfiguration: false, settingKeys: []);

    /// <summary>
    ///  Registers a user-settings service with an optional configuration projection.
    /// </summary>
    /// <param name="settingsService">The settings service.</param>
    /// <param name="includeInConfiguration">
    ///  <see langword="true"/> to project settings under
    ///  <c>UserSettings</c>; otherwise, <see langword="false"/>.
    /// </param>
    /// <param name="settingKeys">
    ///  Optional settings paths to project when
    ///  <paramref name="includeInConfiguration"/> is <see langword="true"/>.
    /// </param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">
    ///  <paramref name="settingsService"/> or <paramref name="settingKeys"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///  A user-settings service is already registered or an external host was supplied.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///  Configuration projection is disabled but settings keys were provided.
    /// </exception>
    public WinFormsApplicationBuilder AddUserSettings(
        IUserSettingsService settingsService,
        bool includeInConfiguration,
        params string[] settingKeys)
    {
        ArgumentNullException.ThrowIfNull(settingsService);

        return AddUserSettingsService(settingsService, includeInConfiguration, settingKeys);
    }

    private WinFormsApplicationBuilder AddUserSettingsService(
        IUserSettingsService settingsService,
        bool includeInConfiguration,
        string[] settingKeys)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(settingKeys);

        if (!includeInConfiguration && settingKeys.Length != 0)
        {
            throw new ArgumentException(
                "Settings keys require configuration projection to be enabled.",
                nameof(settingKeys));
        }

        IServiceCollection services = Services;

        if (services.Any(descriptor => descriptor.ServiceType == typeof(IUserSettingsService)))
        {
            throw new InvalidOperationException("A user-settings service is already registered.");
        }

        if (includeInConfiguration)
        {
            Configuration.AddUserSettings(settingsService, settingKeys);
        }

        services.AddSingleton(settingsService);

        return this;
    }

    /// <summary>
    ///  Builds a Windows Forms application from the builder's current options.
    /// </summary>
    /// <returns>The configured application.</returns>
    /// <exception cref="InvalidOperationException">
    ///  The host for this builder has already been built.
    /// </exception>
    public WinFormsApplication Build()
    {
        if (_hostBuilder is null)
        {
            return new(_options.Clone());
        }

        if (_hostBuilt)
        {
            throw new InvalidOperationException("The host for this builder has already been built.");
        }

        _hostBuilt = true;

        WinFormsApplicationOptions options = _options.Clone();
        options.Host = _hostBuilder.Build();

        return new(options);
    }

    internal WinFormsApplicationOptions Options => _options;

    private HostApplicationBuilder GetHostBuilder()
    {
        if (_options.Host is not null)
        {
            throw new InvalidOperationException(
                "Host configuration cannot be accessed after a host has been supplied.");
        }

        return _hostBuilder ??= CreateHostBuilder();
    }

    private static HostApplicationBuilder CreateHostBuilder(HostApplicationBuilderSettings? settings = null)
    {
        HostApplicationBuilderSettings hostSettings = settings is null
            ? new()
            : new()
            {
                ApplicationName = settings.ApplicationName,
                Args = settings.Args is null ? null : [.. settings.Args],
                Configuration = settings.Configuration,
                ContentRootPath = settings.ContentRootPath,
                DisableDefaults = settings.DisableDefaults,
                EnvironmentName = settings.EnvironmentName
            };

        hostSettings.ContentRootPath ??= AppContext.BaseDirectory;

        return Host.CreateApplicationBuilder(hostSettings);
    }
}
