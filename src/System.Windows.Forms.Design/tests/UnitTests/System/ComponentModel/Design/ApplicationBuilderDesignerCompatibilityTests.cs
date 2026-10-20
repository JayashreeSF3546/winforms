// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.ComponentModel.Design.Tests;

/// <summary>
///  Verifies that runtime service-aware constructors remain compatible with Designer construction.
/// </summary>
public sealed class ApplicationBuilderDesignerCompatibilityTests
{
    [WinFormsFact]
    public void DesignSurface_LoadServiceAwareForm_UsesParameterlessConstructors()
    {
        using DesignSurface surface = new(typeof(ServiceAwareForm));

        ServiceAwareForm form = Assert.IsType<ServiceAwareForm>(surface.ComponentContainer.Components[0]);
        ServiceAwareUserControl childControl = Assert.IsType<ServiceAwareUserControl>(Assert.Single(form.Controls));

        Assert.True(form.ParameterlessConstructorUsed);
        Assert.False(form.ServiceConstructorUsed);
        Assert.True(childControl.ParameterlessConstructorUsed);
        Assert.False(childControl.ServiceConstructorUsed);
    }

    [WinFormsFact]
    public void DesignSurface_LoadDerivedServiceAwareForm_UsesParameterlessConstructors()
    {
        using DesignSurface surface = new(typeof(DerivedServiceAwareForm));

        DerivedServiceAwareForm form = Assert.IsType<DerivedServiceAwareForm>(surface.ComponentContainer.Components[0]);

        Assert.True(form.DerivedParameterlessConstructorUsed);
        Assert.False(form.DerivedServiceConstructorUsed);
        Assert.True(form.ParameterlessConstructorUsed);
        Assert.False(form.ServiceConstructorUsed);
        Assert.IsType<ServiceAwareUserControl>(Assert.Single(form.Controls));
    }

    [WinFormsFact]
    public void DesignerHost_CreateRenameAndRemoveServiceAwareComponent_UsesParameterlessConstructor()
    {
        using DesignSurface surface = new(typeof(ServiceAwareForm));
        IDesignerHost host = Assert.IsAssignableFrom<IDesignerHost>(surface.GetService(typeof(IDesignerHost)));

        ServiceAwareComponent component = Assert.IsType<ServiceAwareComponent>(
            host.CreateComponent(typeof(ServiceAwareComponent)));

        Assert.True(component.ParameterlessConstructorUsed);
        Assert.False(component.ServiceConstructorUsed);

        component.Site!.Name = "renamedComponent";
        Assert.Same(component, host.Container.Components["renamedComponent"]);

        host.Container.Remove(component);
        Assert.Null(component.Site);
    }

    /// <summary>
    ///  Represents a dependency only available to runtime activation.
    /// </summary>
    private sealed class RuntimeDependency
    {
    }

    /// <summary>
    ///  A Form with separate Designer-safe and runtime service-aware constructors.
    /// </summary>
    private class ServiceAwareForm : Form
    {
        public ServiceAwareForm()
        {
            ParameterlessConstructorUsed = true;
            Controls.Add(new ServiceAwareUserControl());
        }

        public ServiceAwareForm(RuntimeDependency dependency)
            : this()
        {
            ArgumentNullException.ThrowIfNull(dependency);
            ServiceConstructorUsed = true;
        }

        public bool ParameterlessConstructorUsed { get; }

        public bool ServiceConstructorUsed { get; }
    }

    /// <summary>
    ///  A derived Form with separate Designer-safe and runtime service-aware constructors.
    /// </summary>
    private sealed class DerivedServiceAwareForm : ServiceAwareForm
    {
        public DerivedServiceAwareForm()
        {
            DerivedParameterlessConstructorUsed = true;
        }

        public DerivedServiceAwareForm(RuntimeDependency dependency)
            : base(dependency)
        {
            DerivedServiceConstructorUsed = true;
        }

        public bool DerivedParameterlessConstructorUsed { get; }

        public bool DerivedServiceConstructorUsed { get; }
    }

    /// <summary>
    ///  A UserControl with separate Designer-safe and runtime service-aware constructors.
    /// </summary>
    private sealed class ServiceAwareUserControl : UserControl
    {
        public ServiceAwareUserControl()
        {
            ParameterlessConstructorUsed = true;
        }

        public ServiceAwareUserControl(RuntimeDependency dependency)
            : this()
        {
            ArgumentNullException.ThrowIfNull(dependency);
            ServiceConstructorUsed = true;
        }

        public bool ParameterlessConstructorUsed { get; }

        public bool ServiceConstructorUsed { get; }
    }

    /// <summary>
    ///  A component with separate Designer-safe and runtime service-aware constructors.
    /// </summary>
    private sealed class ServiceAwareComponent : Component
    {
        public ServiceAwareComponent()
        {
            ParameterlessConstructorUsed = true;
        }

        public ServiceAwareComponent(RuntimeDependency dependency)
            : this()
        {
            ArgumentNullException.ThrowIfNull(dependency);
            ServiceConstructorUsed = true;
        }

        public bool ParameterlessConstructorUsed { get; }

        public bool ServiceConstructorUsed { get; }
    }
}
