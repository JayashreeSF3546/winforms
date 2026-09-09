// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel.Design.Serialization;
using Microsoft.Extensions.WinForms;

namespace System.ComponentModel.Design.Tests;

/// <summary>
///  Designer-surface coverage for the Phase 2 activation contract.
/// </summary>
/// <remarks>
///  <para>
///   These tests use an in-process <see cref="DesignSurface"/>, which is the same designer host,
///   component creation, siting, and CodeDOM serialization pipeline the out-of-process Designer
///   drives. They verify that a type implementing <see cref="IServiceProviderAssignable"/> is
///   created with its parameterless constructor, never receives a service provider at design time,
///   and round trips through serialization - the mechanism behind undo/redo and reload - without
///   emitting any service state.
///  </para>
///  <para>
///   Drag/drop, property-window editing, and inherited-form authoring are driven by the closed
///   source Designer client and remain manual cases in the Phase 2 report.
///  </para>
/// </remarks>
public class DesignerServiceAssignmentTests
{
    [WinFormsFact]
    public void DesignSurface_CreateComponent_UsesParameterlessConstructorAndAssignsNoServices()
    {
        using DesignSurface surface = new(typeof(RootDesignerComponent));
        IDesignerHost host = GetHost(surface);

        AssignableDesignerComponent component =
            (AssignableDesignerComponent)host.CreateComponent(typeof(AssignableDesignerComponent), "probe1");

        Assert.NotNull(component.Site);
        Assert.True(component.Site!.DesignMode);
        Assert.Equal("probe1", component.Site.Name);

        // Design-time creation must never invoke the assignment contract.
        Assert.Equal(0, component.AssignmentCount);
        Assert.Null(component.Services);
    }

    [WinFormsFact]
    public void DesignSurface_CreateComponent_ConstructorInjectedType_Fails()
    {
        using DesignSurface surface = new(typeof(RootDesignerComponent));
        IDesignerHost host = GetHost(surface);

        // A type that only offers a service-injected constructor cannot be dropped on a surface.
        // This is why Designer-created types use assignment instead of constructor injection.
        Exception? exception = Record.Exception(
            () => host.CreateComponent(typeof(ConstructorInjectedComponent), "probe1"));

        Assert.NotNull(exception);
    }

    [WinFormsFact]
    public void DesignSurface_RootComponent_IsCreatedWithoutServices()
    {
        using DesignSurface surface = new(typeof(AssignableRootComponent));
        IDesignerHost host = GetHost(surface);

        AssignableRootComponent root = Assert.IsType<AssignableRootComponent>(host.RootComponent);

        Assert.Equal(0, root.AssignmentCount);
        Assert.NotNull(root.Site);
        Assert.True(root.Site!.DesignMode);
    }

    [WinFormsFact]
    public void DesignSurface_RenameAndDestroy_DoNotAssignServices()
    {
        using DesignSurface surface = new(typeof(RootDesignerComponent));
        IDesignerHost host = GetHost(surface);

        AssignableDesignerComponent component =
            (AssignableDesignerComponent)host.CreateComponent(typeof(AssignableDesignerComponent), "probe1");

        component.Site!.Name = "renamed";

        Assert.Equal("renamed", component.Site.Name);
        Assert.Equal(0, component.AssignmentCount);

        host.DestroyComponent(component);

        Assert.DoesNotContain(component, host.Container.Components.Cast<IComponent>());
        Assert.Equal(0, component.AssignmentCount);
    }

    [WinFormsFact]
    public void DesignSurface_SerializationRoundTrip_PreservesPropertiesAndAssignsNoServices()
    {
        using DesignSurface surface = new(typeof(RootDesignerComponent));
        IDesignerHost host = GetHost(surface);

        AssignableDesignerComponent component =
            (AssignableDesignerComponent)host.CreateComponent(typeof(AssignableDesignerComponent), "probe1");
        component.Caption = "round trip";

        CodeDomComponentSerializationService serialization = new((IServiceProvider)host);
        SerializationStore store = serialization.CreateStore();
        serialization.Serialize(store, component);
        store.Close();

        host.DestroyComponent(component);
        serialization.DeserializeTo(store, host.Container, validateRecycledTypes: false, applyDefaults: false);

        AssignableDesignerComponent restored = host.Container.Components
            .Cast<IComponent>()
            .OfType<AssignableDesignerComponent>()
            .Single();

        // The serialized state contains the Designer-visible property and nothing that the
        // assignment contract contributes.
        Assert.Equal("round trip", restored.Caption);
        Assert.Equal(0, restored.AssignmentCount);
        Assert.Null(restored.Services);
    }

    [WinFormsFact]
    public void DesignSurface_ComponentWithoutTheContract_RoundTripsUnchanged()
    {
        using DesignSurface surface = new(typeof(RootDesignerComponent));
        IDesignerHost host = GetHost(surface);

        PlainDesignerComponent component =
            (PlainDesignerComponent)host.CreateComponent(typeof(PlainDesignerComponent), "plain1");
        component.Caption = "unchanged";

        CodeDomComponentSerializationService serialization = new((IServiceProvider)host);
        SerializationStore store = serialization.CreateStore();
        serialization.Serialize(store, component);
        store.Close();

        host.DestroyComponent(component);
        serialization.DeserializeTo(store, host.Container, validateRecycledTypes: false, applyDefaults: false);

        PlainDesignerComponent restored = host.Container.Components
            .Cast<IComponent>()
            .OfType<PlainDesignerComponent>()
            .Single();

        Assert.Equal("unchanged", restored.Caption);
    }

    [WinFormsFact]
    public void DesignSurface_DesignerCreatedUserControl_IsCreatedParameterlesslyWithoutServices()
    {
        // TypeDescriptor.CreateInstance is the creation path DesignerHost.CreateComponent uses for
        // controls dropped on a surface.
        using AssignableUserControl control = (AssignableUserControl)TypeDescriptor.CreateInstance(
            provider: null,
            objectType: typeof(AssignableUserControl),
            argTypes: null,
            args: null)!;

        Assert.Equal(0, control.AssignmentCount);
        Assert.Null(control.Services);

        control.CreateControl();

        Assert.True(control.IsHandleCreated);
    }

    private static IDesignerHost GetHost(DesignSurface surface)
        => (IDesignerHost)surface.GetService(typeof(IDesignerHost))!;

    /// <summary>
    ///  A root designer that supports a non-visual design surface.
    /// </summary>
    private class TestRootDesigner : ComponentDesigner, IRootDesigner
    {
        public ViewTechnology[] SupportedTechnologies => [ViewTechnology.Default + 1];

        public object GetView(ViewTechnology technology) => new();
    }

    /// <summary>
    ///  The design surface root used by these tests.
    /// </summary>
    [Designer(typeof(TestRootDesigner), typeof(IRootDesigner))]
    private class RootDesignerComponent : Component
    {
    }

    /// <summary>
    ///  A design surface root that also implements the assignment contract.
    /// </summary>
    [Designer(typeof(TestRootDesigner), typeof(IRootDesigner))]
    private class AssignableRootComponent : Component, IServiceProviderAssignable
    {
        public int AssignmentCount { get; private set; }

        public void AssignServiceProvider(IServiceProvider serviceProvider) => AssignmentCount++;
    }

    /// <summary>
    ///  A Designer-created component that implements the assignment contract.
    /// </summary>
    [Designer(typeof(ComponentDesigner))]
    private class AssignableDesignerComponent : Component, IServiceProviderAssignable
    {
        public string? Caption { get; set; }

        public IServiceProvider? Services { get; private set; }

        public int AssignmentCount { get; private set; }

        public void AssignServiceProvider(IServiceProvider serviceProvider)
        {
            Services = serviceProvider;
            AssignmentCount++;
        }
    }

    /// <summary>
    ///  A Designer-created component that does not use the assignment contract.
    /// </summary>
    [Designer(typeof(ComponentDesigner))]
    private class PlainDesignerComponent : Component
    {
        public string? Caption { get; set; }
    }

    /// <summary>
    ///  A component that can only be created through constructor injection.
    /// </summary>
    [Designer(typeof(ComponentDesigner))]
    private class ConstructorInjectedComponent : Component
    {
        public ConstructorInjectedComponent(IServiceProvider services) => Services = services;

        public IServiceProvider Services { get; }
    }

    /// <summary>
    ///  A Designer-created user control that implements the assignment contract.
    /// </summary>
    private class AssignableUserControl : UserControl, IServiceProviderAssignable
    {
        public IServiceProvider? Services { get; private set; }

        public int AssignmentCount { get; private set; }

        public void AssignServiceProvider(IServiceProvider serviceProvider)
        {
            Services = serviceProvider;
            AssignmentCount++;
        }
    }
}
