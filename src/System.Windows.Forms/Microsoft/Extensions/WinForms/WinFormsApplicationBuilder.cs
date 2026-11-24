// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Selects startup UI and optional startup defaults for a Windows Forms application.
/// </summary>
/// <remarks>
///  <para>
///   Configure the application before creating startup controls. Building does not activate the UI.
///   The builder must be configured and built on the thread that will own the application.
///  </para>
///  <para>
///   Explicit startup defaults are applied during Build before host construction. They require deferred
///   UI, an owning STA without previously created windows, and are not restored on disposal.
///  </para>
/// </remarks>
public sealed partial class WinFormsApplicationBuilder
{
    private readonly Thread _ownerThread = Thread.CurrentThread;
    private Form? _startupForm;
    private Func<ApplicationContext>? _contextFactory;
    private Func<IServiceProvider, Form>? _serviceFormFactory;
    private Func<IServiceProvider, WinFormsUiScope, ApplicationContext>? _scopedContextFactory;
    private IHostBuilder? _hostBuilder;
    private IConfigurationBuilder? _configuration;
    private ServiceCollection? _services;
    private WinFormsApplicationFeatures? _features;
    private WinFormsSingleInstance? _singleInstance;
    private EventHandler<WinFormsNextInstanceRequest>? _nextInstance;
    private bool _built;

    internal WinFormsApplicationBuilder()
    {
    }

    /// <summary>
    ///  Gets the services to add to the application's selected host.
    /// </summary>
    /// <remarks>
    ///  <para>
    ///   Select an unbuilt host with <see cref="UseHost"/> before building an application with service registrations.
    ///   Registrations are appended after the host builder's own service configuration and become read-only
    ///   when Build consumes the builder. Access this property on the builder's owning thread before Build.
    ///   Reading an empty collection does not enable hosting.
    ///  </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    ///  The builder has been built or the calling thread does not own it.
    /// </exception>
    public IServiceCollection Services
    {
        get
        {
            VerifyCanConfigure();
            return _services ??= new ServiceCollection();
        }
    }

    /// <summary>
    ///  Transfers an explicitly elected primary lease only on successful Build.
    /// </summary>
    internal WinFormsApplicationBuilder UseSingleInstance(
        WinFormsSingleInstance primary, EventHandler<WinFormsNextInstanceRequest> nextInstance)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(nextInstance);
        VerifyCanConfigure();
        if (_singleInstance is not null)
        {
            throw new InvalidOperationException(SR.WinFormsApplication_SingleInstanceAlreadyClaimed);
        }

        _singleInstance = primary;
        _nextInstance = nextInstance;

        return this;
    }

    /// <summary>
    ///  Selects the internal shared lifecycle prototype without changing default builder behavior.
    /// </summary>
    internal WinFormsApplicationBuilder ConfigureApplicationFeatures(Action<WinFormsApplicationFeatures> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        VerifyCanConfigure();
        if (_features is not null)
        {
            throw new InvalidOperationException(SR.WinFormsApplication_FeaturesAlreadyConfigured);
        }

        WinFormsApplicationFeatures features = new();
        configure(features);
        _features = features;

        return this;
    }

    /// <summary>
    ///  Selects a Generic Host builder whose host is owned by this application.
    /// </summary>
    /// <param name="hostBuilder">An unbuilt Generic Host builder.</param>
    /// <returns>This builder.</returns>
    /// <remarks>
    ///  <para>
    ///   Build consumes the host builder and registers the UI shutdown barrier before building the host.
    ///   Use Run to start the application and its host. Do not separately build or run the supplied builder.
    ///   Hosted startup must not wait for UI dispatch. Shutdown waits for UI cleanup even after host timeout.
    ///  </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="hostBuilder"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    ///  The builder cannot be configured, or a host builder has already been selected.
    /// </exception>
    public WinFormsApplicationBuilder UseHost(IHostBuilder hostBuilder)
    {
        ArgumentNullException.ThrowIfNull(hostBuilder);
        VerifyCanConfigure();
        if (_hostBuilder is not null)
        {
            throw new InvalidOperationException(SR.WinFormsApplication_HostAlreadySelected);
        }

        _hostBuilder = hostBuilder;
        return this;
    }

    /// <summary>
    ///  Selects application configuration sources to append to the selected host during Build.
    /// </summary>
    /// <param name="configuration">An unbuilt configuration builder whose sources and properties are borrowed.</param>
    /// <returns>This builder.</returns>
    /// <remarks>
    ///  <para>
    ///   Sources and properties are snapshotted when Build consumes this builder, then appended after the
    ///   host's application configuration callbacks and before its service configuration. The selected host
    ///   builds and owns the effective configuration root. This method does not load providers.
    ///  </para>
    ///  <para>
    ///   Nonempty sources or properties require UseHost. An empty selection preserves hostless behavior.
    ///   Retained configuration builders follow standard IConfigurationBuilder semantics: later recipe edits
    ///   do not update a built application. Do not mutate shared configuration sources during or after Build.
    ///   These sources do not select the host environment, application name, or content root.
    ///  </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    ///  Configuration is already selected, the application builder is consumed, or the calling thread does not own it.
    /// </exception>
    public WinFormsApplicationBuilder UseConfiguration(IConfigurationBuilder configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        VerifyCanConfigure();
        if (_configuration is not null)
        {
            throw new InvalidOperationException(SR.WinFormsApplication_ConfigurationAlreadySelected);
        }

        _configuration = configuration;

        return this;
    }

    /// <summary>
    ///  Selects a startup form to be constructed when the application activates its startup UI.
    /// </summary>
    /// <typeparam name="TForm">The form type, with a public parameterless constructor.</typeparam>
    /// <returns>This builder.</returns>
    /// <exception cref="InvalidOperationException">
    ///  The builder has been built, startup UI is already selected, or the calling thread does not own the builder.
    /// </exception>
    public WinFormsApplicationBuilder UseStartupForm<TForm>() where TForm : Form, new()
    {
        VerifyCanSelectStartup();
        _contextFactory = static () => new ApplicationContext(CreateStartupForm<TForm>());

        return this;
    }

    /// <summary>
    ///  Selects an existing startup form.
    /// </summary>
    /// <param name="startupForm">A form created on the thread that owns the builder.</param>
    /// <returns>This builder.</returns>
    /// <remarks>
    ///  <para>
    ///   Ownership transfers on successful <see cref="Build()"/>. The caller must configure the application
    ///   before constructing this form. A handleless form's creation thread cannot be validated.
    ///  </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="startupForm"/> is null.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="startupForm"/> is disposed.</exception>
    /// <exception cref="InvalidOperationException">
    ///  Startup UI is already selected, the builder has been built, or the builder or form belongs to another thread.
    /// </exception>
    public WinFormsApplicationBuilder UseStartupForm(Form startupForm)
    {
        ArgumentNullException.ThrowIfNull(startupForm);
        VerifyCanSelectStartup();
        WinFormsApplicationOptions.ValidateStartupForm(startupForm);
        _startupForm = startupForm;

        return this;
    }

    /// <summary>
    ///  Selects a factory for the application's root context.
    /// </summary>
    /// <param name="contextFactory">
    ///  A factory that creates a non-null context on the application's owning thread.
    /// </param>
    /// <returns>This builder.</returns>
    /// <remarks>
    ///  <para>
    ///   The factory is deferred until startup activation. The application owns the successfully activated context.
    ///  </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="contextFactory"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    ///  The builder has been built, startup UI is already selected, or the calling thread does not own the builder.
    /// </exception>
    public WinFormsApplicationBuilder UseApplicationContext(Func<ApplicationContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        VerifyCanSelectStartup();
        _contextFactory = contextFactory;

        return this;
    }

    /// <summary>
    ///  Prototypes explicit host-backed startup activation without changing parameterless activation.
    /// </summary>
    internal WinFormsApplicationBuilder UseStartupFormFactory(Func<IServiceProvider, Form> formFactory)
    {
        ArgumentNullException.ThrowIfNull(formFactory);
        VerifyCanSelectStartup();
        _serviceFormFactory = formFactory;

        return this;
    }

    /// <summary>
    ///  Selects a startup form factory with its own dependency injection scope.
    /// </summary>
    /// <param name="formFactory">
    ///  Creates a fresh, unshown form on the owning STA using its form-scoped services.
    /// </param>
    /// <returns>This builder.</returns>
    /// <remarks>
    ///  <para>
    ///   Select a host with <see cref="UseHost"/> before Build. The factory runs after host startup
    ///   and synchronization-context installation, not during Build. Construct the form explicitly;
    ///   do not return a container-owned or previously scoped form.
    ///  </para>
    ///  <para>
    ///   Forms must perform service-dependent cleanup before calling base.Dispose. UI scopes use
    ///   synchronous disposal; async-only disposable services are unsupported.
    ///  </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="formFactory"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    ///  Startup UI is already selected, the builder has been built, or the calling thread does not own it.
    /// </exception>
    public WinFormsApplicationBuilder UseScopedStartupForm(Func<IServiceProvider, Form> formFactory)
    {
        ArgumentNullException.ThrowIfNull(formFactory);
        return UseScopedApplicationContext((_, scopes) => new ApplicationContext(scopes.CreateForm(formFactory).Form));
    }

    /// <summary>
    ///  Selects a context factory with application-scoped services and explicit form lifetime management.
    /// </summary>
    /// <param name="contextFactory">
    ///  Creates a fresh context on the owning STA. Create its main form through the supplied UI scope.
    /// </param>
    /// <returns>This builder.</returns>
    /// <remarks>
    ///  <para>
    ///   Select a host with <see cref="UseHost"/> before Build. The factory is deferred until startup
    ///   activation. The application owns the context and UI scope. Each form created through the
    ///   coordinator has a separate service scope; it does not inherit application-scoped instances.
    ///   Forms require base-last disposal, and async-only disposable scoped services are unsupported.
    ///  </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="contextFactory"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    ///  Startup UI is already selected, the builder has been built, or the calling thread does not own it.
    /// </exception>
    public WinFormsApplicationBuilder UseScopedApplicationContext(
        Func<IServiceProvider, WinFormsUiScope, ApplicationContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        VerifyCanSelectStartup();
        _scopedContextFactory = contextFactory;

        return this;
    }

    /// <summary>
    ///  Builds an application without creating or showing startup UI.
    /// </summary>
    /// <returns>The application, which owns any supplied startup form.</returns>
    /// <exception cref="ObjectDisposedException">The selected startup form is disposed.</exception>
    /// <exception cref="InvalidOperationException">
    ///  No startup UI is selected, the builder has been built, or the builder or startup form belongs to another thread.
    ///  Explicit startup configuration also requires an STA without previously created windows, deferred startup UI,
    ///  and application defaults that can still be changed.
    /// </exception>
    /// <remarks>
    ///  <para>
    ///   Selected startup defaults affect process or thread state before the optional host is built.
    ///   Once applying them begins, the builder is consumed even if a setter or host construction fails.
    ///   Partial settings are not rolled back.
    ///  </para>
    /// </remarks>
    public WinFormsApplication Build()
        => Build(host: null);

    internal WinFormsApplication Build(IHost? host)
    {
        VerifyCanConfigure();
        if (_startupForm is null && _contextFactory is null && _serviceFormFactory is null && _scopedContextFactory is null)
        {
            throw new InvalidOperationException(SR.WinFormsApplication_MissingStartup);
        }

        if ((_serviceFormFactory is not null || _scopedContextFactory is not null) && _hostBuilder is null && host is null)
        {
            throw new InvalidOperationException(SR.WinFormsApplication_ServiceActivationRequiresHost);
        }

        if (_services is { Count: > 0 } && _hostBuilder is null)
        {
            throw new InvalidOperationException(SR.WinFormsApplication_ServiceRegistrationRequiresHost);
        }

        if (_configuration is not null
            && (_configuration.Sources.Count > 0 || _configuration.Properties.Count > 0)
            && _hostBuilder is null)
        {
            throw new InvalidOperationException(SR.WinFormsApplication_ConfigurationRequiresHost);
        }

        if (_startupForm is not null)
        {
            WinFormsApplicationOptions.ValidateStartupForm(_startupForm);
        }

        WinFormsApplicationFeatures? features = _features?.Snapshot();
        ApplyStartupConfiguration();
        WinFormsApplicationOptions options = new(_ownerThread, _startupForm, _contextFactory, _serviceFormFactory, _scopedContextFactory);
        WinFormsApplication application = new(options, host, features);
        if (_hostBuilder is not null)
        {
            // Generic Host build is single-use, including when its build operation fails.
            _built = true;
            _services?.MakeReadOnly();
            if (_configuration is not null)
            {
                IConfigurationSource[] sources = [.. _configuration.Sources];
                KeyValuePair<string, object>[] properties = [.. _configuration.Properties];
                _hostBuilder.ConfigureAppConfiguration((_, configuration) =>
                {
                    foreach ((string key, object value) in properties)
                    {
                        configuration.Properties[key] = value;
                    }

                    foreach (IConfigurationSource source in sources)
                    {
                        configuration.Add(source);
                    }
                });
            }

            if (_services is { Count: > 0 } services)
            {
                _hostBuilder.ConfigureServices((_, hostServices) =>
                {
                    foreach (ServiceDescriptor descriptor in services)
                    {
                        hostServices.Add(descriptor);
                    }
                });
            }

            WinFormsApplicationHost adapter = WinFormsApplicationHost.Build(_hostBuilder, application);
            try
            {
                application.SetHost(adapter);
            }
            catch
            {
                adapter.DisposeHost();
                throw;
            }
        }

        _built = true;
        _services?.MakeReadOnly();
        try
        {
            application.AttachSingleInstance(_singleInstance, _nextInstance);
        }
        catch
        {
            // The failed Build has not transferred an existing Form or primary lease.
            if (application.Host is WinFormsApplicationHost adapter)
            {
                adapter.DisposeHost();
            }

            throw;
        }

        _startupForm = null;
        _contextFactory = null;
        _serviceFormFactory = null;
        _scopedContextFactory = null;
        _hostBuilder = null;
        _configuration = null;
        _features = null;
        _singleInstance = null;
        _nextInstance = null;

        return application;
    }

    private static TForm CreateStartupForm<TForm>() where TForm : Form, new()
    {
        try
        {
            return new TForm();
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Throw(exception.InnerException);
            throw;
        }
    }

    private void VerifyCanSelectStartup()
    {
        VerifyCanConfigure();
        if (_startupForm is not null || _contextFactory is not null || _serviceFormFactory is not null || _scopedContextFactory is not null)
        {
            throw new InvalidOperationException(SR.WinFormsApplication_StartupAlreadySelected);
        }
    }

    private void VerifyCanConfigure()
    {
        if (Thread.CurrentThread != _ownerThread)
        {
            throw new InvalidOperationException(SR.WinFormsApplication_WrongThread);
        }

        if (_built)
        {
            throw new InvalidOperationException(SR.WinFormsApplication_AlreadyBuilt);
        }
    }
}
