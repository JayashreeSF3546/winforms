// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.WinForms;
using Moq;

namespace System.Windows.Forms.Tests;

/// <summary>
///  Verifies the Phase 2 Designer-safe activation contract: assignment, activation precedence,
///  UI scope ownership, disposal ordering, rollback, and cancellation.
/// </summary>
[Collection(WinFormsUIScopeCollection.Name)]
public class WinFormsUIActivationTests
{
    private static ServiceProvider CreateServices(Action<IServiceCollection>? configure = null)
    {
        ServiceCollection services = new();
        services.AddWindowsFormsActivation();
        configure?.Invoke(services);

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true
        });
    }

    [WinFormsFact]
    public void AddWindowsFormsActivation_NullServices_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            "services",
            () => ((IServiceCollection)null!).AddWindowsFormsActivation());
    }

    [WinFormsFact]
    public void AddWindowsFormsActivation_IsIdempotent()
    {
        ServiceCollection services = new();
        services.AddWindowsFormsActivation();
        services.AddWindowsFormsActivation();

        int activatorRegistrations = 0;
        foreach (ServiceDescriptor descriptor in services)
        {
            if (descriptor.ServiceType == typeof(IUIActivator))
            {
                activatorRegistrations++;
            }
        }

        Assert.Equal(1, activatorRegistrations);
    }

    [WinFormsFact]
    public void GetApplicationActivator_ExposesTheApplicationScope()
    {
        using ServiceProvider provider = CreateServices();
        IUIActivator activator = provider.GetApplicationActivator();

        Assert.Equal(UIScopeKind.Application, activator.Scope.Kind);
        Assert.Null(activator.Scope.Parent);

        // The application scope is a real dependency-injection scope, so scoped services can be
        // resolved from it even when the container validates scopes.
        Assert.NotSame(provider, activator.Scope.Services);
        Assert.Same(activator.Scope, provider.GetApplicationScope());
    }

    [WinFormsFact]
    public void GetApplicationScope_WithoutActivationServices_ThrowsInvalidOperationException()
    {
        ServiceCollection services = new();
        using ServiceProvider provider = services.BuildServiceProvider();

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(provider.GetApplicationScope);

        Assert.Contains("AddWindowsFormsActivation", exception.Message, StringComparison.Ordinal);
    }

    [WinFormsFact]
    public void ApplicationScope_IsDisposedWhenTheRootProviderIsDisposed()
    {
        ServiceProvider provider = CreateServices();
        UIScope applicationScope = provider.GetApplicationScope();

        Assert.False(applicationScope.IsDisposed);

        provider.Dispose();

        Assert.True(applicationScope.IsDisposed);
    }

    [WinFormsFact]
    public void CreateForm_ParameterlessForm_RequiresNoServices()
    {
        using ServiceProvider provider = CreateServices();
        IUIActivator activator = provider.GetApplicationActivator();

        using UIActivation<PlainForm> activation = activator.CreateForm<PlainForm>();

        Assert.NotNull(activation.Instance);
        Assert.Equal(UIScopeKind.Form, activation.Scope.Kind);
        Assert.Same(activator.Scope, activation.Scope.Parent);
    }

    [WinFormsFact]
    public void CreateForm_ConstructorInjection_ResolvesRegisteredServices()
    {
        using ServiceProvider provider = CreateServices(
            services => services.AddSingleton<ICounterService, CounterService>());
        IUIActivator activator = provider.GetApplicationActivator();

        using UIActivation<InjectedForm> activation = activator.CreateForm<InjectedForm>();

        Assert.NotNull(activation.Instance.Counter);
        Assert.Same(
            provider.GetRequiredService<ICounterService>(),
            activation.Instance.Counter);
    }

    [WinFormsFact]
    public void CreateForm_AssignsScopeServiceProviderToTheForm()
    {
        using ServiceProvider provider = CreateServices();
        IUIActivator activator = provider.GetApplicationActivator();

        using UIActivation<AssignableForm> activation = activator.CreateForm<AssignableForm>();

        Assert.Equal(1, activation.Instance.AssignmentCount);
        Assert.Same(activation.Scope.Services, activation.Instance.Services);
    }

    [WinFormsFact]
    public void CreateForm_AssignsToDesignerCreatedChildrenRecursively()
    {
        using ServiceProvider provider = CreateServices();
        IUIActivator activator = provider.GetApplicationActivator();

        using UIActivation<DesignerLikeForm> activation = activator.CreateForm<DesignerLikeForm>();
        DesignerLikeForm form = activation.Instance;

        Assert.Same(activation.Scope.Services, form.Services);
        Assert.Same(activation.Scope.Services, form.NestedControl.Services);
        Assert.Same(activation.Scope.Services, form.DeeplyNestedControl.Services);
        Assert.Equal(1, form.NestedControl.AssignmentCount);
        Assert.Equal(1, form.DeeplyNestedControl.AssignmentCount);
    }

    [WinFormsFact]
    public void AssignServices_Container_AssignsDesignerCreatedComponents()
    {
        using ServiceProvider provider = CreateServices();
        IUIActivator activator = provider.GetApplicationActivator();

        using UIActivation<ComponentOwnerForm> activation = activator.CreateForm<ComponentOwnerForm>();

        // The form forwards its Designer components container from AssignServiceProvider.
        Assert.Same(activation.Scope.Services, activation.Instance.Component.Services);
        Assert.Equal(1, activation.Instance.Component.AssignmentCount);
    }

    [WinFormsFact]
    public void AssignServices_SameInstanceTwiceInOneWalk_AssignsOnce()
    {
        using ServiceProvider provider = CreateServices();
        IUIActivator activator = provider.GetApplicationActivator();
        using AssignableControl control = new();

        int assigned = activator.AssignServices(control);

        Assert.Equal(1, assigned);
        Assert.Equal(1, control.AssignmentCount);
    }

    [WinFormsFact]
    public void AssignServices_DesignModeComponent_IsSkipped()
    {
        using ServiceProvider provider = CreateServices();
        IUIActivator activator = provider.GetApplicationActivator();

        Mock<ISite> site = new(MockBehavior.Loose);
        site.Setup(s => s.DesignMode).Returns(true);
        site.Setup(s => s.Container).Returns((IContainer?)null);
        site.Setup(s => s.Component).Returns(() => null!);
        site.Setup(s => s.Name).Returns("designModeControl");

        using AssignableControl control = new()
        {
            Site = site.Object
        };

        int assigned = activator.AssignServices(control);

        Assert.Equal(0, assigned);
        Assert.Equal(0, control.AssignmentCount);
        Assert.Null(control.Services);
    }

    [WinFormsFact]
    public void AssignServices_NullTarget_ThrowsArgumentNullException()
    {
        using ServiceProvider provider = CreateServices();
        IUIActivator activator = provider.GetApplicationActivator();

        Assert.Throws<ArgumentNullException>("target", () => activator.AssignServices(null!));
    }

    [WinFormsFact]
    public void CreateForm_IsolatedScope_DoesNotShareScopedServicesWithTheParent()
    {
        using ServiceProvider provider = CreateServices(
            services => services.AddScoped<ICounterService, CounterService>());
        IUIActivator activator = provider.GetApplicationActivator();

        using UIActivation<PlainForm> first = activator.CreateForm<PlainForm>();
        using UIActivation<PlainForm> second = activator.CreateForm<PlainForm>();

        var firstService = first.Scope.Services.GetRequiredService<ICounterService>();
        var secondService = second.Scope.Services.GetRequiredService<ICounterService>();

        Assert.NotSame(firstService, secondService);
        Assert.NotSame(activator.Scope.Services.GetRequiredService<ICounterService>(), firstService);
    }

    [WinFormsFact]
    public void CreateControl_SharedScope_ReusesTheParentScopedServices()
    {
        using ServiceProvider provider = CreateServices(
            services => services.AddScoped<ICounterService, CounterService>());
        IUIActivator activator = provider.GetApplicationActivator();

        using UIActivation<PlainForm> form = activator.CreateForm<PlainForm>();
        var formActivator = form.Scope.Services.GetRequiredService<IUIActivator>();

        using UIActivation<AssignableControl> shared =
            formActivator.CreateControl<AssignableControl>(scopeMode: UIServiceScopeMode.Shared);

        Assert.Same(form.Scope.Services, shared.Scope.Services);
        Assert.Same(
            form.Scope.Services.GetRequiredService<ICounterService>(),
            shared.Scope.Services.GetRequiredService<ICounterService>());
        Assert.Equal(UIScopeKind.Control, shared.Scope.Kind);
    }

    [WinFormsFact]
    public void CreateForm_NestedActivatorResolvedFromScope_ParentsTheChildScope()
    {
        using ServiceProvider provider = CreateServices();
        IUIActivator activator = provider.GetApplicationActivator();

        using UIActivation<PlainForm> parent = activator.CreateForm<PlainForm>();
        var parentActivator = parent.Scope.Services.GetRequiredService<IUIActivator>();

        Assert.Same(parent.Scope, parentActivator.Scope);

        using UIActivation<PlainForm> child = parentActivator.CreateForm<PlainForm>();

        Assert.Same(parent.Scope, child.Scope.Parent);
        Assert.Contains(child.Scope, parent.Scope.Children);
    }

    [WinFormsFact]
    public void Dispose_ParentScope_DisposesChildScopesAndOwnedInstances()
    {
        using ServiceProvider provider = CreateServices();
        IUIActivator activator = provider.GetApplicationActivator();

        UIActivation<PlainForm> parent = activator.CreateForm<PlainForm>();
        var parentActivator = parent.Scope.Services.GetRequiredService<IUIActivator>();
        UIActivation<PlainForm> child = parentActivator.CreateForm<PlainForm>();

        parent.Dispose();

        Assert.True(parent.Scope.IsDisposed);
        Assert.True(child.Scope.IsDisposed);
        Assert.True(parent.Instance.IsDisposed);
        Assert.True(child.Instance.IsDisposed);
        Assert.Empty(activator.Scope.Children);
    }

    [WinFormsFact]
    public void Dispose_Scope_DisposesChildrenThenOwnerThenScopedServices()
    {
        List<string> order = [];
        using ServiceProvider provider = CreateServices(
            services => services.AddScoped(_ => new DisposableProbe("service", order)));
        IUIActivator activator = provider.GetApplicationActivator();

        UIActivation<TrackingForm> parent = activator.Create(
            _ => new TrackingForm("owner", order),
            UIScopeKind.Form);

        // Force the scoped service to be created so that it participates in disposal.
        parent.Scope.Services.GetRequiredService<DisposableProbe>();

        var parentActivator = parent.Scope.Services.GetRequiredService<IUIActivator>();
        parentActivator.Create(_ => new TrackingForm("child", order), UIScopeKind.Form);

        parent.Dispose();

        Assert.Equal(["child", "owner", "service"], order);
    }

    [WinFormsFact]
    public void Dispose_Activation_IsIdempotent()
    {
        using ServiceProvider provider = CreateServices();
        IUIActivator activator = provider.GetApplicationActivator();

        UIActivation<PlainForm> activation = activator.CreateForm<PlainForm>();
        int disposedRaised = 0;
        activation.Scope.Disposed += (_, _) => disposedRaised++;

        activation.Dispose();
        activation.Dispose();
        activation.Scope.Dispose();

        Assert.Equal(1, disposedRaised);
        Assert.True(activation.IsDisposed);
    }

    [WinFormsFact]
    public void CreateForm_RegisteredInContainer_IsResolvedAndNotDisposedByTheUIScope()
    {
        using ServiceProvider provider = CreateServices(
            services => services.AddSingleton<PlainForm>());
        IUIActivator activator = provider.GetApplicationActivator();

        UIActivation<PlainForm> activation = activator.CreateForm<PlainForm>();

        Assert.Same(provider.GetRequiredService<PlainForm>(), activation.Instance);

        activation.Dispose();

        // The container owns the singleton, so the UI scope must not dispose it.
        Assert.False(activation.Instance.IsDisposed);
        provider.GetRequiredService<PlainForm>().Dispose();
    }

    [WinFormsFact]
    public void CreateForm_MissingService_ThrowsAndLeavesNoScope()
    {
        using ServiceProvider provider = CreateServices();
        IUIActivator activator = provider.GetApplicationActivator();
        int scopesBefore = activator.Scope.Children.Count;

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() => activator.CreateForm<InjectedForm>());

        Assert.Contains(nameof(ICounterService), exception.Message, StringComparison.Ordinal);
        Assert.Equal(scopesBefore, activator.Scope.Children.Count);
    }

    [WinFormsFact]
    public void CreateForm_ConstructorThrows_RollsBackTheScope()
    {
        using ServiceProvider provider = CreateServices();
        IUIActivator activator = provider.GetApplicationActivator();

        Assert.Throws<ArithmeticException>(() => activator.CreateForm<ThrowingForm>());

        Assert.Empty(activator.Scope.Children);
    }

    [WinFormsFact]
    public void CreateForm_AssignmentThrows_DisposesInstanceAndScope()
    {
        using ServiceProvider provider = CreateServices();
        IUIActivator activator = provider.GetApplicationActivator();

        Assert.Throws<InvalidTimeZoneException>(() => activator.CreateForm<ThrowingAssignableForm>());

        Assert.Empty(activator.Scope.Children);
        Assert.True(ThrowingAssignableForm.LastInstance!.IsDisposed);
    }

    [WinFormsFact]
    public void CreateForm_CanceledToken_ThrowsWithoutCreatingAScope()
    {
        using ServiceProvider provider = CreateServices();
        IUIActivator activator = provider.GetApplicationActivator();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => activator.CreateForm<PlainForm>(cancellationToken: cancellation.Token));

        Assert.Empty(activator.Scope.Children);
    }

    [WinFormsFact]
    public void CreateForm_CanceledDuringConstruction_DisposesInstanceAndScope()
    {
        using CancellationTokenSource cancellation = new();
        using ServiceProvider provider = CreateServices();
        IUIActivator activator = provider.GetApplicationActivator();

        PlainForm? created = null;

        Assert.Throws<OperationCanceledException>(() => activator.Create(
            _ =>
            {
                created = new PlainForm();
                cancellation.Cancel();
                return created;
            },
            UIScopeKind.Form,
            cancellationToken: cancellation.Token));

        Assert.NotNull(created);
        Assert.True(created.IsDisposed);
        Assert.Empty(activator.Scope.Children);
    }

    [WinFormsFact]
    public void CreateComponent_CreatesComponentScopeAndAssignsServices()
    {
        using ServiceProvider provider = CreateServices();
        IUIActivator activator = provider.GetApplicationActivator();

        using UIActivation<AssignableComponent> activation =
            activator.CreateComponent<AssignableComponent>();

        Assert.Equal(UIScopeKind.Component, activation.Scope.Kind);
        Assert.Same(activation.Scope.Services, activation.Instance.Services);

        activation.Dispose();

        Assert.True(activation.Instance.IsDisposed);
    }

    [WinFormsFact]
    public void Create_NullFactory_ThrowsArgumentNullException()
    {
        using ServiceProvider provider = CreateServices();
        IUIActivator activator = provider.GetApplicationActivator();

        Assert.Throws<ArgumentNullException>(
            "factory",
            () => activator.Create<PlainForm>(null!, UIScopeKind.Form));
    }

    [WinFormsFact]
    public void Create_FactoryReturnsNull_ThrowsInvalidOperationException()
    {
        using ServiceProvider provider = CreateServices();
        IUIActivator activator = provider.GetApplicationActivator();

        Assert.Throws<InvalidOperationException>(
            () => activator.Create<PlainForm>(_ => null!, UIScopeKind.Form));

        Assert.Empty(activator.Scope.Children);
    }

    [WinFormsFact]
    public void Show_ModelessForm_DisposesTheScopeWhenTheFormIsDisposed()
    {
        using ServiceProvider provider = CreateServices();
        IUIActivator activator = provider.GetApplicationActivator();

        UIActivation<PlainForm> activation = activator.Show<PlainForm>();
        UIScope scope = activation.Scope;

        Assert.True(activation.Instance.Visible);
        Assert.Single(activator.Scope.Children);

        activation.Instance.Close();

        Assert.True(scope.IsDisposed);
        Assert.Empty(activator.Scope.Children);
    }

    [WinFormsFact]
    public void ShowDialog_CreatesDialogScopeAndDisposesItAfterTheDialogCloses()
    {
        using ServiceProvider provider = CreateServices();
        IUIActivator activator = provider.GetApplicationActivator();
        UIScope? dialogScope = null;

        DialogResult result = activator.ShowDialog<AssignableForm>(
            owner: null,
            initialize: form =>
            {
                dialogScope = form.Services!.GetRequiredService<IUIActivator>().Scope;
                form.Shown += static (sender, _) => ((Form)sender!).DialogResult = DialogResult.OK;
            });

        Assert.Equal(DialogResult.OK, result);
        Assert.NotNull(dialogScope);
        Assert.Equal(UIScopeKind.Dialog, dialogScope.Kind);
        Assert.True(dialogScope.IsDisposed);
        Assert.Empty(activator.Scope.Children);
    }

    [WinFormsFact]
    public void CreateForm_DialogHandle_KeepsResultsReadableUntilDisposed()
    {
        using ServiceProvider provider = CreateServices();
        IUIActivator activator = provider.GetApplicationActivator();

        using UIActivation<ResultForm> activation =
            activator.CreateForm<ResultForm>(UIScopeKind.Dialog);
        activation.Instance.Shown += static (sender, _) =>
        {
            ResultForm form = (ResultForm)sender!;
            form.Result = "accepted";
            form.DialogResult = DialogResult.OK;
        };

        DialogResult result = activation.Instance.ShowDialog();

        Assert.Equal(DialogResult.OK, result);
        Assert.Equal("accepted", activation.Instance.Result);
        Assert.False(activation.Scope.IsDisposed);
    }

    [WinFormsFact]
    public void CreateChildScope_NullName_UsesTheScopeKind()
    {
        using ServiceProvider provider = CreateServices();
        IUIActivator activator = provider.GetApplicationActivator();

        using UIScope scope = activator.Scope.CreateChildScope(UIScopeKind.Navigation);

        Assert.Equal(UIScopeKind.Navigation, scope.Kind);
        Assert.Equal(nameof(UIScopeKind.Navigation), scope.Name);
        Assert.False(scope.IsDisposed);
    }

    [WinFormsFact]
    public void Scope_UsedAfterDisposal_ThrowsObjectDisposedException()
    {
        using ServiceProvider provider = CreateServices();
        IUIActivator activator = provider.GetApplicationActivator();

        UIScope scope = activator.Scope.CreateChildScope(UIScopeKind.Form);
        scope.Dispose();

        Assert.Throws<ObjectDisposedException>(() => scope.CreateChildScope(UIScopeKind.Form));
        Assert.Throws<ObjectDisposedException>(() => scope.AssignServices(new AssignableComponent()));
    }

    [WinFormsFact]
    public void Diagnostics_TrackScopeAndActivationCounts()
    {
        UIScopeDiagnostics.Reset();
        using ServiceProvider provider = CreateServices();
        IUIActivator activator = provider.GetApplicationActivator();

        long activationsBefore = UIScopeDiagnostics.TotalActivations;
        int activeBefore = UIScopeDiagnostics.ActiveScopeCount;

        UIActivation<PlainForm> activation = activator.CreateForm<PlainForm>();

        Assert.Equal(activationsBefore + 1, UIScopeDiagnostics.TotalActivations);
        Assert.Equal(activeBefore + 1, UIScopeDiagnostics.ActiveScopeCount);

        activation.Dispose();

        Assert.Equal(activeBefore, UIScopeDiagnostics.ActiveScopeCount);

        long failuresBefore = UIScopeDiagnostics.TotalActivationFailures;
        Assert.Throws<ArithmeticException>(() => activator.CreateForm<ThrowingForm>());
        Assert.Equal(failuresBefore + 1, UIScopeDiagnostics.TotalActivationFailures);
    }

    [WinFormsFact]
    public void Navigator_NavigateSwapsViewsAndDisposesThePreviousScope()
    {
        using ServiceProvider provider = CreateServices();
        IUIActivator activator = provider.GetApplicationActivator();
        using Panel region = new();
        using UINavigator navigator = new(activator, region);

        AssignableControl first = navigator.Navigate<AssignableControl>();
        UIScope firstScope = navigator.CurrentScope!;

        Assert.Same(first, navigator.CurrentView);
        Assert.Same(region, first.Parent);
        Assert.Equal(UIScopeKind.Navigation, firstScope.Kind);
        Assert.Same(firstScope.Services, first.Services);

        AssignableControl second = navigator.Navigate<AssignableControl>();

        Assert.Same(second, navigator.CurrentView);
        Assert.True(firstScope.IsDisposed);
        Assert.True(first.IsDisposed);
        Assert.Single(region.Controls);
    }

    [WinFormsFact]
    public void Navigator_ActivationFailure_KeepsThePreviousView()
    {
        using ServiceProvider provider = CreateServices();
        IUIActivator activator = provider.GetApplicationActivator();
        using Panel region = new();
        using UINavigator navigator = new(activator, region);

        AssignableControl first = navigator.Navigate<AssignableControl>();

        Assert.Throws<ArithmeticException>(() => navigator.Navigate<ThrowingControl>());

        Assert.Same(first, navigator.CurrentView);
        Assert.False(first.IsDisposed);
        Assert.Single(region.Controls);
        Assert.False(navigator.CurrentScope!.IsDisposed);
    }

    [WinFormsFact]
    public void Navigator_Dispose_DisposesTheCurrentViewAndScope()
    {
        using ServiceProvider provider = CreateServices();
        IUIActivator activator = provider.GetApplicationActivator();
        using Panel region = new();
        UINavigator navigator = new(activator, region);

        AssignableControl view = navigator.Navigate<AssignableControl>();
        UIScope scope = navigator.CurrentScope!;

        navigator.Dispose();

        Assert.True(view.IsDisposed);
        Assert.True(scope.IsDisposed);
        Assert.Null(navigator.CurrentView);
        Assert.Empty(region.Controls);
    }

    [WinFormsFact]
    public void Navigator_NullArguments_ThrowArgumentNullException()
    {
        using ServiceProvider provider = CreateServices();
        IUIActivator activator = provider.GetApplicationActivator();
        using Panel region = new();

        Assert.Throws<ArgumentNullException>("activator", () => new UINavigator(null!, region));
        Assert.Throws<ArgumentNullException>("region", () => new UINavigator(activator, null!));
    }

    private interface ICounterService
    {
        int Value { get; set; }
    }

    /// <summary>
    ///  Minimal service used to observe scoped versus singleton resolution.
    /// </summary>
    private sealed class CounterService : ICounterService
    {
        public int Value { get; set; }
    }

    /// <summary>
    ///  Records disposal order using a shared log.
    /// </summary>
    private sealed class DisposableProbe(string name, List<string> log) : IDisposable
    {
        public void Dispose() => log.Add(name);
    }

    /// <summary>
    ///  A form with only a parameterless constructor, which is what the Designer requires.
    /// </summary>
    private class PlainForm : Form
    {
    }

    /// <summary>
    ///  Records disposal order using a shared log.
    /// </summary>
    private sealed class TrackingForm(string name, List<string> log) : Form
    {
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                log.Add(name);
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    ///  A form that requires constructor injection and therefore cannot be created by the Designer.
    /// </summary>
    private sealed class InjectedForm(ICounterService counter) : Form
    {
        public ICounterService Counter { get; } = counter;
    }

    /// <summary>
    ///  A form that keeps a parameterless constructor and receives services through assignment.
    /// </summary>
    private class AssignableForm : Form, IServiceProviderAssignable
    {
        public IServiceProvider? Services { get; private set; }

        public int AssignmentCount { get; private set; }

        public void AssignServiceProvider(IServiceProvider serviceProvider)
        {
            Services = serviceProvider;
            AssignmentCount++;
        }
    }

    /// <summary>
    ///  Returns a value that must remain readable after a modal dialog closes.
    /// </summary>
    private sealed class ResultForm : Form
    {
        public string? Result { get; set; }
    }

    /// <summary>
    ///  A control that keeps a parameterless constructor and receives services through assignment.
    /// </summary>
    private class AssignableControl : UserControl, IServiceProviderAssignable
    {
        public IServiceProvider? Services { get; private set; }

        public int AssignmentCount { get; private set; }

        public void AssignServiceProvider(IServiceProvider serviceProvider)
        {
            Services = serviceProvider;
            AssignmentCount++;
        }
    }

    /// <summary>
    ///  A component that keeps a parameterless constructor and receives services through assignment.
    /// </summary>
    private sealed class AssignableComponent : Component, IServiceProviderAssignable
    {
        public IServiceProvider? Services { get; private set; }

        public int AssignmentCount { get; private set; }

        public bool IsDisposed { get; private set; }

        public void AssignServiceProvider(IServiceProvider serviceProvider)
        {
            Services = serviceProvider;
            AssignmentCount++;
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    /// <summary>
    ///  Mirrors what <c>InitializeComponent</c> produces: children constructed by the owner.
    /// </summary>
    private sealed class DesignerLikeForm : AssignableForm
    {
        public DesignerLikeForm()
        {
            NestedControl = new AssignableControl();
            DeeplyNestedControl = new AssignableControl();
            NestedControl.Controls.Add(DeeplyNestedControl);
            Controls.Add(NestedControl);
        }

        public AssignableControl NestedControl { get; }

        public AssignableControl DeeplyNestedControl { get; }
    }

    /// <summary>
    ///  Mirrors a Designer form that owns a components container.
    /// </summary>
    private sealed class ComponentOwnerForm : Form, IServiceProviderAssignable
    {
        private readonly IContainer _components = new Container();

        public ComponentOwnerForm()
        {
            Component = new AssignableComponent();
            _components.Add(Component);
        }

        public AssignableComponent Component { get; }

        public void AssignServiceProvider(IServiceProvider serviceProvider)
        {
            serviceProvider.GetRequiredService<IUIActivator>().AssignServices(_components);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _components.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    ///  Fails during construction so that activation rollback can be observed.
    /// </summary>
    private sealed class ThrowingForm : Form
    {
        public ThrowingForm() => throw new ArithmeticException("Construction probe.");
    }

    /// <summary>
    ///  Fails during construction so that navigation rollback can be observed.
    /// </summary>
    private sealed class ThrowingControl : UserControl
    {
        public ThrowingControl() => throw new ArithmeticException("Construction probe.");
    }

    /// <summary>
    ///  Fails during assignment so that post-construction rollback can be observed.
    /// </summary>
    private sealed class ThrowingAssignableForm : Form, IServiceProviderAssignable
    {
        public ThrowingAssignableForm() => LastInstance = this;

        public static ThrowingAssignableForm? LastInstance { get; private set; }

        public void AssignServiceProvider(IServiceProvider serviceProvider)
            => throw new InvalidTimeZoneException("Assignment probe.");
    }
}
