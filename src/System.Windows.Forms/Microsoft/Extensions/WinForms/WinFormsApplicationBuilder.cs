// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Selects the startup UI for a Windows Forms application.
/// </summary>
/// <remarks>
///  <para>
///   Configure the application before creating startup controls. Building does not activate the UI.
///   The builder must be configured and built on the thread that will own the application.
///  </para>
/// </remarks>
public sealed class WinFormsApplicationBuilder
{
    private readonly Thread _ownerThread = Thread.CurrentThread;
    private Form? _startupForm;
    private Func<ApplicationContext>? _contextFactory;
    private Func<IServiceProvider, Form>? _serviceFormFactory;
    private IHostBuilder? _hostBuilder;
    private bool _built;

    internal WinFormsApplicationBuilder()
    {
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
    ///  Builds an application without creating or showing startup UI.
    /// </summary>
    /// <returns>The application, which owns any supplied startup form.</returns>
    /// <exception cref="ObjectDisposedException">The selected startup form is disposed.</exception>
    /// <exception cref="InvalidOperationException">
    ///  No startup UI is selected, the builder has been built, or the builder or startup form belongs to another thread.
    /// </exception>
    public WinFormsApplication Build()
        => Build(host: null);

    internal WinFormsApplication Build(IHost? host)
    {
        VerifyCanConfigure();
        if (_startupForm is null && _contextFactory is null && _serviceFormFactory is null)
        {
            throw new InvalidOperationException(SR.WinFormsApplication_MissingStartup);
        }

        if (_serviceFormFactory is not null && _hostBuilder is null && host is null)
        {
            throw new InvalidOperationException(SR.WinFormsApplication_ServiceActivationRequiresHost);
        }

        if (_startupForm is not null)
        {
            WinFormsApplicationOptions.ValidateStartupForm(_startupForm);
        }

        WinFormsApplicationOptions options = new(_ownerThread, _startupForm, _contextFactory, _serviceFormFactory);
        WinFormsApplication application = new(options, host);
        if (_hostBuilder is not null)
        {
            // Generic Host build is single-use, including when its build operation fails.
            _built = true;
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
        _startupForm = null;
        _contextFactory = null;
        _serviceFormFactory = null;
        _hostBuilder = null;

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
        if (_startupForm is not null || _contextFactory is not null || _serviceFormFactory is not null)
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
