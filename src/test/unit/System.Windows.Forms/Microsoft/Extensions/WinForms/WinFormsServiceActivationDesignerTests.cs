// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.ComponentModel.Design;
using System.ComponentModel.Design.Serialization;
using System.Drawing;
using System.Drawing.Design;
using Microsoft.Extensions.WinForms;

namespace System.Windows.Forms.Tests;

/// <summary>
///  Checks the prototype boundary against the local Designer infrastructure, not the Visual Studio OOP server.
/// </summary>
public class WinFormsServiceActivationDesignerTests
{
    [WinFormsFact]
    public void DesignSurface_ToolboxRenameDeleteAndReloadNeedNoRuntimeServices()
    {
        using DesignSurface surface = new(typeof(PrototypeForm));
        Assert.True(surface.IsLoaded);
        Assert.Empty(surface.LoadErrors);
        var host = (IDesignerHost)surface.GetService(typeof(IDesignerHost))!;
        PrototypeForm root = Assert.IsType<PrototypeForm>(host.RootComponent);
        Assert.Null(root.Child.Service);
        RejectingProvider provider = new();
        bool assigned = false;
        Assert.Throws<InvalidOperationException>(() => WinFormsServiceActivation.Assign(root.Child, provider,
            (_, _) => assigned = true));

        ToolboxItem toolbox = new(typeof(PrototypeControl));
        PrototypeControl control = Assert.IsType<PrototypeControl>(Assert.Single(toolbox.CreateComponents(host)!));
        root.Controls.Add(control);
        Assert.Null(control.Service);
        Assert.True(control.Site!.DesignMode);
        Assert.Throws<InvalidOperationException>(() => WinFormsServiceActivation.Assign(control, provider,
            (_, _) => assigned = true));
        control.Site.Name = "renamedControl";
        Assert.Same(control, host.Container.Components["renamedControl"]);
        host.DestroyComponent(control);
        Assert.True(control.IsDisposed);
        Assert.Null(host.Container.Components["renamedControl"]);
        Assert.False(assigned);
        Assert.Equal(0, provider.Calls);

        ((IDesignerLoaderHost)host).Reload();
        Assert.True(root.IsDisposed);
        PrototypeForm reloaded = Assert.IsType<PrototypeForm>(host.RootComponent);
        Assert.NotSame(root, reloaded);
        Assert.Null(reloaded.Child.Service);
        Assert.Empty(surface.LoadErrors);
    }

    [WinFormsFact]
    public void DesignSurface_DeleteUndoRedoRestoresServiceFreeComponents()
    {
        using DesignSurface surface = new(typeof(PrototypeForm));
        var host = (IDesignerHost)surface.GetService(typeof(IDesignerHost))!;
        var services = (IServiceContainer)surface.GetService(typeof(IServiceContainer))!;
        services.AddService(typeof(ComponentSerializationService), new CodeDomComponentSerializationService(surface));
        PrototypeControl original = (PrototypeControl)host.CreateComponent(typeof(PrototypeControl), "child");
        using SingleUndoEngine undo = new(surface);

        host.DestroyComponent(original);
        Assert.True(original.IsDisposed);
        undo.Toggle();
        PrototypeControl restored = Assert.IsType<PrototypeControl>(host.Container.Components["child"]);
        Assert.NotSame(original, restored);
        Assert.Null(restored.Service);
        Assert.True(restored.Site!.DesignMode);
        undo.Toggle();
        Assert.True(restored.IsDisposed);
        Assert.Null(host.Container.Components["child"]);
    }

    [WinFormsTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Serialization_DropsRuntimeAssignmentAndPreservesOrdinaryState(bool derived)
    {
        using Container container = new();
        ServiceToken token = new();
        using PrototypeControl control = WinFormsServiceActivation.Create(
            new RejectingProvider(),
            _ => derived ? new DerivedPrototypeControl() : new PrototypeControl(),
            (created, _) => created.InitializeServices(token));
        control.Size = new(240, 120);
        container.Add(control, "control");
        CodeDomComponentSerializationService serializer = new();
        using SerializationStore store = serializer.CreateStore();
        serializer.Serialize(store, control);
        store.Close();
        Assert.Empty(store.Errors);

        using PrototypeControl restored = Assert.IsAssignableFrom<PrototypeControl>(
            Assert.Single(serializer.Deserialize(store).Cast<object>()));
        Assert.Equal(control.GetType(), restored.GetType());
        Assert.Equal(new Size(240, 120), restored.Size);
        Assert.Null(restored.Service);
        Assert.Same(token, control.Service);
        Assert.Throws<InvalidOperationException>(() => control.InitializeServices(new ServiceToken()));
    }

    [WinFormsFact]
    public void DesignerCreation_RejectsRuntimeActivationBeforeFactoryOrProviderAccess()
    {
        using DesignSurface surface = new(typeof(PrototypeForm));
        var host = (IDesignerHost)surface.GetService(typeof(IDesignerHost))!;
        LicenseProbe component = (LicenseProbe)host.CreateComponent(typeof(LicenseProbe), "component");

        Assert.IsType<InvalidOperationException>(component.Failure);
        Assert.False(component.FactoryCalled);
        Assert.Equal(0, component.ProviderCalls);
        Assert.True(component.Site!.DesignMode);
        Assert.Throws<InvalidOperationException>(() =>
            WinFormsServiceActivation.Assign(component, component.Provider, (_, source) => source.GetService(typeof(ServiceToken))));
        Assert.Equal(0, component.ProviderCalls);
    }

    /// <summary>
    ///  Supplies a value that would fail serialization if runtime service state were persisted.
    /// </summary>
    public sealed class ServiceToken
    {
        internal ServiceToken()
        {
        }
    }

    /// <summary>
    ///  Keeps ordinary layout construction independent of a typed runtime assignment.
    /// </summary>
    public class PrototypeControl : UserControl
    {
        public PrototypeControl()
        {
            Controls.Add(new Button { Text = "Layout" });
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ServiceToken? Service { get; set; }

        internal void InitializeServices(ServiceToken service)
        {
            ArgumentNullException.ThrowIfNull(service);
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (Service is not null)
            {
                throw new InvalidOperationException("Services have already been assigned.");
            }

            Service = service;
        }
    }

    /// <summary>
    ///  Preserves the base control's service-free constructor and inherited initialization.
    /// </summary>
    public sealed class DerivedPrototypeControl : PrototypeControl
    {
    }

    /// <summary>
    ///  Constructs a nested inherited control before the Designer sites the root.
    /// </summary>
    public sealed class PrototypeForm : Form
    {
        public PrototypeForm()
        {
            Controls.Add(Child);
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public DerivedPrototypeControl Child { get; } = new();
    }

    /// <summary>
    ///  Detects accidental application-service access from a design-time operation.
    /// </summary>
    private sealed class RejectingProvider : IServiceProvider
    {
        internal int Calls { get; private set; }

        public object? GetService(Type serviceType)
        {
            Calls++;
            throw new InvalidOperationException("Runtime services must not be resolved by the Designer.");
        }
    }

    /// <summary>
    ///  Verifies the license-context guard while a component is still being constructed.
    /// </summary>
    public sealed class LicenseProbe : Component
    {
        private readonly RejectingProvider _provider = new();

        public LicenseProbe()
        {
            Failure = Record.Exception(() => WinFormsServiceActivation.Create(_provider, _ =>
            {
                FactoryCalled = true;
                return new Component();
            }));
        }

        internal Exception? Failure { get; }

        internal bool FactoryCalled { get; private set; }

        internal IServiceProvider Provider => _provider;

        internal int ProviderCalls => _provider.Calls;
    }

    /// <summary>
    ///  Retains one real designer undo unit for a delete/restore/delete cycle.
    /// </summary>
    private sealed class SingleUndoEngine(IServiceProvider provider) : UndoEngine(provider)
    {
        private UndoUnit? _unit;

        protected override void AddUndoUnit(UndoUnit unit) => _unit = unit;

        internal void Toggle() => _unit!.Undo();
    }
}
