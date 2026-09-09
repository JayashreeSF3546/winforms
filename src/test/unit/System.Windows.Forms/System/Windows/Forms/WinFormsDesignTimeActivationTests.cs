// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.ComponentModel.Design;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.WinForms;
using Moq;

namespace System.Windows.Forms.Tests;

/// <summary>
///  Verifies that Phase 2 activation never participates in design-time creation and that
///  Designer-created types keep working without any services.
/// </summary>
[Collection(WinFormsUIScopeCollection.Name)]
public class WinFormsDesignTimeActivationTests
{
    private static ServiceProvider CreateServices()
    {
        ServiceCollection services = new();
        services.AddWindowsFormsActivation();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true
        });
    }

    [WinFormsFact]
    public void Activator_AtDesignTime_RefusesActivationAndAssignment()
    {
        using ServiceProvider provider = CreateServices();
        IUIActivator activator = provider.GetApplicationActivator();

        DesignTimeProbe.Reset();
        LicenseManager.CreateWithContext(
            typeof(DesignTimeProbe),
            new DesigntimeLicenseContext(),
            [activator]);

        Assert.True(DesignTimeProbe.WasDesignTime);
        Assert.IsType<InvalidOperationException>(DesignTimeProbe.ActivationException);
        Assert.IsType<InvalidOperationException>(DesignTimeProbe.AssignmentException);
        Assert.Contains(
            "design time",
            DesignTimeProbe.ActivationException!.Message,
            StringComparison.OrdinalIgnoreCase);

        // Nothing may be left behind by a refused design-time request.
        Assert.Empty(activator.Scope.Children);
    }

    [WinFormsFact]
    public void DesignerCreatedControl_IsConstructedParameterlesslyWithoutServices()
    {
        // TypeDescriptor.CreateInstance is the path IDesignerHost.CreateComponent uses.
        object created = TypeDescriptor.CreateInstance(
            provider: null,
            objectType: typeof(DesignerAssignableControl),
            argTypes: null,
            args: null)!;

        using DesignerAssignableControl control = Assert.IsType<DesignerAssignableControl>(created);

        Assert.Equal(0, control.AssignmentCount);
        Assert.Null(control.Services);

        // The control must remain fully usable without ever receiving a service provider.
        control.Text = "no services required";
        control.CreateControl();

        Assert.True(control.IsHandleCreated);
    }

    [WinFormsFact]
    public void ServiceProviderAssignableContract_ExposesNoSerializableMembers()
    {
        Type contract = typeof(IServiceProviderAssignable);

        Assert.Empty(contract.GetProperties());
        Assert.Empty(contract.GetEvents());
        Assert.Empty(contract.GetFields());

        MethodInfo method = Assert.Single(contract.GetMethods());

        Assert.Equal(nameof(IServiceProviderAssignable.AssignServiceProvider), method.Name);
        Assert.Equal(typeof(void), method.ReturnType);
        Assert.Equal(typeof(IServiceProvider), Assert.Single(method.GetParameters()).ParameterType);
    }

    [WinFormsFact]
    public void RecommendedAssignmentPattern_ExposesNoServiceProviderPropertyToTheDesigner()
    {
        // The recommended pattern stores the provider in a private field. Because the contract is a
        // method, nothing it adds is visible to the property grid or to Designer serialization.
        using RecommendedPatternControl control = new();

        foreach (PropertyDescriptor property in TypeDescriptor.GetProperties(control))
        {
            Assert.NotEqual(typeof(IServiceProvider), property.PropertyType);
            Assert.NotEqual(
                nameof(IServiceProviderAssignable.AssignServiceProvider),
                property.Name);
        }

        // A public read/write IServiceProvider property, on the other hand, would be picked up by
        // the Designer. This is the reason the contract is not a property.
        PropertyDescriptor? exposed = TypeDescriptor
            .GetProperties(typeof(DesignerAssignableControl))
            .Find(nameof(DesignerAssignableControl.Services), ignoreCase: false);

        Assert.NotNull(exposed);
        Assert.Equal(typeof(IServiceProvider), exposed.PropertyType);
    }

    [WinFormsFact]
    public void InheritedForm_BaseFormRemainsDesignerConstructibleAndInheritsAssignment()
    {
        // The Designer instantiates the base type of an inherited form, so the base type must keep
        // a public parameterless constructor.
        object baseInstance = Activator.CreateInstance(typeof(DesignerBaseForm))!;
        ((IDisposable)baseInstance).Dispose();

        using ServiceProvider provider = CreateServices();
        IUIActivator activator = provider.GetApplicationActivator();

        using UIActivation<InheritedForm> activation = activator.CreateForm<InheritedForm>();

        Assert.Equal(1, activation.Instance.AssignmentCount);
        Assert.Same(activation.Scope.Services, activation.Instance.Services);
    }

    [WinFormsFact]
    public void ConstructorInjectedType_IsNotDesignerConstructible()
    {
        // This is the documented reason a Designer-created UserControl, component, or inheritable
        // base form must use assignment instead of constructor injection.
        Assert.Throws<MissingMethodException>(
            () => Activator.CreateInstance(typeof(ConstructorInjectedControl)));
    }

    [WinFormsFact]
    public void ActivatedForm_DesignModeChild_DoesNotReceiveServices()
    {
        using ServiceProvider provider = CreateServices();
        IUIActivator activator = provider.GetApplicationActivator();

        using UIActivation<DesignModeChildForm> activation = activator.CreateForm<DesignModeChildForm>();

        Assert.Equal(1, activation.Instance.AssignmentCount);
        Assert.Equal(0, activation.Instance.DesignModeChild.AssignmentCount);
        Assert.Equal(1, activation.Instance.RuntimeChild.AssignmentCount);
    }

    [WinFormsFact]
    public void AssignServices_RuntimeSitedComponent_ReceivesServices()
    {
        using ServiceProvider provider = CreateServices();
        IUIActivator activator = provider.GetApplicationActivator();

        using Container container = new();
        DesignerAssignableComponent component = new();
        container.Add(component, "timer1");

        // A runtime Container sites its components, but Site.DesignMode is false.
        Assert.NotNull(component.Site);
        Assert.False(component.Site!.DesignMode);

        int assigned = activator.AssignServices(container);

        Assert.Equal(1, assigned);
        Assert.Equal(1, component.AssignmentCount);
    }

    [WinFormsFact]
    public void NonBuilderApplication_CreatingAndDisposingFormsCreatesNoScopes()
    {
        int activeBefore = UIScopeDiagnostics.ActiveScopeCount;
        long activationsBefore = UIScopeDiagnostics.TotalActivations;
        long assignmentsBefore = UIScopeDiagnostics.TotalServiceAssignments;

        for (int i = 0; i < 5; i++)
        {
            using DesignerAssignableControl control = new();
            using Form form = new();
            form.Controls.Add(control);
            form.CreateControl();
        }

        Assert.Equal(activeBefore, UIScopeDiagnostics.ActiveScopeCount);
        Assert.Equal(activationsBefore, UIScopeDiagnostics.TotalActivations);
        Assert.Equal(assignmentsBefore, UIScopeDiagnostics.TotalServiceAssignments);
    }

    [WinFormsFact]
    public void ActivatedForm_WithoutTheContract_IsActivatedWithoutAssignment()
    {
        using ServiceProvider provider = CreateServices();
        IUIActivator activator = provider.GetApplicationActivator();
        long assignmentsBefore = UIScopeDiagnostics.TotalServiceAssignments;

        using UIActivation<Form> activation = activator.CreateForm<Form>();

        Assert.NotNull(activation.Instance);
        Assert.Equal(assignmentsBefore, UIScopeDiagnostics.TotalServiceAssignments);
    }

    /// <summary>
    ///  Runs activation requests while the component model reports design-time usage.
    /// </summary>
    private sealed class DesignTimeProbe
    {
        public DesignTimeProbe(IUIActivator activator)
        {
            WasDesignTime = LicenseManager.UsageMode == LicenseUsageMode.Designtime;
            ActivationException = Record.Exception(() => activator.CreateForm<Form>());
            AssignmentException = Record.Exception(
                () => activator.AssignServices(new DesignerAssignableComponent()));
        }

        public static bool WasDesignTime { get; private set; }

        public static Exception? ActivationException { get; private set; }

        public static Exception? AssignmentException { get; private set; }

        public static void Reset()
        {
            WasDesignTime = false;
            ActivationException = null;
            AssignmentException = null;
        }
    }

    /// <summary>
    ///  A Designer-created control: public parameterless constructor plus assignment.
    /// </summary>
    private class DesignerAssignableControl : UserControl, IServiceProviderAssignable
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
    ///  A Designer-created component: public parameterless constructor plus assignment.
    /// </summary>
    private sealed class DesignerAssignableComponent : Component, IServiceProviderAssignable
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
    ///  Implements the contract the recommended way: the provider is kept in a private field.
    /// </summary>
    private sealed class RecommendedPatternControl : UserControl, IServiceProviderAssignable
    {
        private IServiceProvider? _services;

        public bool HasServices => _services is not null;

        public void AssignServiceProvider(IServiceProvider serviceProvider)
            => _services = serviceProvider;
    }

    /// <summary>
    ///  A base form that supports inherited Designer scenarios.
    /// </summary>
    private class DesignerBaseForm : Form, IServiceProviderAssignable
    {
        public IServiceProvider? Services { get; private set; }

        public int AssignmentCount { get; private set; }

        public virtual void AssignServiceProvider(IServiceProvider serviceProvider)
        {
            Services = serviceProvider;
            AssignmentCount++;
        }
    }

    /// <summary>
    ///  An inherited form that reuses the base assignment implementation.
    /// </summary>
    private sealed class InheritedForm : DesignerBaseForm
    {
    }

    /// <summary>
    ///  A control that can only be created through constructor injection.
    /// </summary>
    private sealed class ConstructorInjectedControl : UserControl
    {
        public ConstructorInjectedControl(IServiceProvider services) => Services = services;

        public IServiceProvider Services { get; }
    }

    /// <summary>
    ///  Hosts one child that reports design mode and one ordinary runtime child.
    /// </summary>
    private sealed class DesignModeChildForm : Form, IServiceProviderAssignable
    {
        public DesignModeChildForm()
        {
            Mock<ISite> site = new(MockBehavior.Loose);
            site.Setup(s => s.DesignMode).Returns(true);
            site.Setup(s => s.Container).Returns((IContainer?)null);
            site.Setup(s => s.Name).Returns("designModeChild");

            DesignModeChild = new DesignerAssignableControl
            {
                Site = site.Object
            };

            RuntimeChild = new DesignerAssignableControl();
            Controls.Add(DesignModeChild);
            Controls.Add(RuntimeChild);
        }

        public DesignerAssignableControl DesignModeChild { get; }

        public DesignerAssignableControl RuntimeChild { get; }

        public IServiceProvider? Services { get; private set; }

        public int AssignmentCount { get; private set; }

        public void AssignServiceProvider(IServiceProvider serviceProvider)
        {
            Services = serviceProvider;
            AssignmentCount++;
        }
    }
}
