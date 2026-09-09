// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.WinForms;
using Microsoft.DotNet.RemoteExecutor;

namespace System.Windows.Forms.Tests;

/// <summary>
///  Verifies that the Phase 1 host wires Phase 2 activation without changing its own contract.
/// </summary>
[Collection(WinFormsUIScopeCollection.Name)]
public class WinFormsApplicationBuilderActivationTests
{
    [WinFormsFact]
    public void Build_RegistersActivationServices()
    {
        WinFormsApplicationBuilder builder = new();
        using WinFormsApplication application = builder.Build();

        UIScope applicationScope = application.Services.GetApplicationScope();

        Assert.Equal(UIScopeKind.Application, applicationScope.Kind);
        Assert.Null(applicationScope.Parent);
        Assert.Same(applicationScope, application.Services.GetApplicationActivator().Scope);
    }

    [WinFormsFact]
    public void AddWindowsFormsLifetime_RegistersActivationServices()
    {
        HostApplicationBuilder hostBuilder = Host.CreateEmptyApplicationBuilder(settings: null);
        hostBuilder.Services.AddLogging();
        hostBuilder.Services.AddWindowsFormsLifetime(options => options.EnableVisualStyles = false);

        using IHost host = hostBuilder.Build();

        Assert.Equal(
            UIScopeKind.Application,
            host.Services.GetApplicationScope().Kind);
    }

    [WinFormsFact]
    public void UseWindowsFormsLifetime_RegistersActivationServices()
    {
        HostApplicationBuilder hostBuilder = Host.CreateEmptyApplicationBuilder(settings: null);
        hostBuilder.Services.AddLogging();
        hostBuilder.UseWindowsFormsLifetime<ActivationTestForm>();

        using IHost host = hostBuilder.Build();

        Assert.NotNull(host.Services.GetApplicationActivator());
    }

    [WinFormsFact]
    public void Dispose_Application_DisposesTheApplicationScope()
    {
        WinFormsApplicationBuilder builder = new();
        WinFormsApplication application = builder.Build();
        UIScope applicationScope = application.Services.GetApplicationScope();

        application.Dispose();

        Assert.True(applicationScope.IsDisposed);
    }

    [Fact]
    public void Run_StartupForm_IsActivatedInAFormScopeAndDisposedWithTheMessageLoop()
    {
        RemoteExecutor.Invoke(RunAndAssertStartupFormScope).Dispose();
    }

    [Fact]
    public void Run_ConstructorInjectedStartupForm_ResolvesServicesOnTheUiThread()
    {
        RemoteExecutor.Invoke(RunAndAssertConstructorInjectedStartupForm).Dispose();
    }

    [Fact]
    public void Run_CustomApplicationContext_ReceivesAssignedServices()
    {
        RemoteExecutor.Invoke(RunAndAssertApplicationContextAssignment).Dispose();
    }

    private static void RunAndAssertStartupFormScope()
    {
        UIScopeDiagnostics.Reset();

        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder();
        builder.Services.AddSingleton<StartupProbe>();
        builder.UseStartupForm<ScopedStartupForm>();

        using WinFormsApplication application = builder.Build();
        UIScope applicationScope = application.Services.GetApplicationScope();

        int formScopesWhileRunning = 0;
        bool formAssigned = false;
        bool childAssigned = false;
        bool servicesResolvable = false;
        bool startupScopeDisposedWhenStopped = false;
        UIScope? startupScope = null;

        application.Lifetime.ApplicationStopped += (_, _) =>
            startupScopeDisposedWhenStopped = startupScope?.IsDisposed == true;

        application.Lifetime.ApplicationStarted += (_, _) =>
        {
            application.Lifetime.SynchronizationContext!.Post(
                _ =>
                {
                    formScopesWhileRunning = applicationScope.Children.Count;
                    startupScope = applicationScope.Children[0];

                    ScopedStartupForm form = (ScopedStartupForm)Application.OpenForms[0]!;
                    formAssigned = form.Services is not null;
                    childAssigned = form.Child.Services is not null;
                    servicesResolvable = form.Services!.GetService<StartupProbe>() is not null;

                    form.Close();
                },
                null);
        };

        application.Run();

        // The startup form is activated into exactly one child scope of the application scope, and
        // both the form and its Designer-created child receive the same scoped provider.
        Assert.Equal(1, formScopesWhileRunning);
        Assert.True(formAssigned);
        Assert.True(childAssigned);
        Assert.True(servicesResolvable);
        Assert.Equal(UIScopeKind.Form, startupScope!.Kind);

        // The startup activation is released when the message loop returns, which is before the
        // host reports that the application has stopped.
        Assert.True(startupScopeDisposedWhenStopped);

        // IHost.RunAsync disposes the host, and disposing the container disposes the application
        // scope, so no scope survives the run.
        Assert.True(applicationScope.IsDisposed);
        Assert.Equal(0, UIScopeDiagnostics.ActiveScopeCount);
    }

    private static void RunAndAssertConstructorInjectedStartupForm()
    {
        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder();
        builder.Services.AddSingleton<StartupProbe>();
        builder.UseStartupForm<InjectedStartupForm>();

        using WinFormsApplication application = builder.Build();
        bool resolved = false;

        application.Lifetime.ApplicationStarted += (_, _) =>
            application.Lifetime.SynchronizationContext!.Post(
                _ =>
                {
                    InjectedStartupForm form = (InjectedStartupForm)Application.OpenForms[0]!;
                    resolved = form.Probe is not null;
                    form.Close();
                },
                null);

        application.Run();

        Assert.True(resolved);
    }

    private static void RunAndAssertApplicationContextAssignment()
    {
        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder();
        builder.Services.AddSingleton<StartupProbe>();
        builder.UseApplicationContext<AssignableApplicationContext>();

        using WinFormsApplication application = builder.Build();

        application.Lifetime.ApplicationStarted += (_, _) =>
            application.Lifetime.SynchronizationContext!.Post(
                static _ => Application.ExitThread(),
                null);

        application.Run();

        Assert.True(AssignableApplicationContext.WasAssigned);
    }

    /// <summary>
    ///  A service used to prove that the activated form can resolve application services.
    /// </summary>
    private sealed class StartupProbe
    {
    }

    /// <summary>
    ///  A plain form used only to configure the lifetime extensions.
    /// </summary>
    private sealed class ActivationTestForm : Form
    {
    }

    /// <summary>
    ///  A startup form that receives services through assignment and owns a Designer-like child.
    /// </summary>
    private sealed class ScopedStartupForm : Form, IServiceProviderAssignable
    {
        public ScopedStartupForm()
        {
            Child = new ScopedChildControl();
            Controls.Add(Child);
        }

        public ScopedChildControl Child { get; }

        public IServiceProvider? Services { get; private set; }

        public void AssignServiceProvider(IServiceProvider serviceProvider)
            => Services = serviceProvider;
    }

    /// <summary>
    ///  A child control created by the form's constructor, as <c>InitializeComponent</c> would.
    /// </summary>
    private sealed class ScopedChildControl : UserControl, IServiceProviderAssignable
    {
        public IServiceProvider? Services { get; private set; }

        public void AssignServiceProvider(IServiceProvider serviceProvider)
            => Services = serviceProvider;
    }

    /// <summary>
    ///  A startup form that uses constructor injection.
    /// </summary>
    private sealed class InjectedStartupForm(StartupProbe probe) : Form
    {
        public StartupProbe Probe { get; } = probe;
    }

    /// <summary>
    ///  A custom application context that receives services through assignment.
    /// </summary>
    private sealed class AssignableApplicationContext : ApplicationContext, IServiceProviderAssignable
    {
        public static bool WasAssigned { get; private set; }

        public void AssignServiceProvider(IServiceProvider serviceProvider)
            => WasAssigned = serviceProvider.GetService<StartupProbe>() is not null;
    }
}
