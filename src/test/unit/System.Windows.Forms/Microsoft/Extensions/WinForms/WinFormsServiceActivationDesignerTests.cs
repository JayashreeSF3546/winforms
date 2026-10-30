// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CodeDom;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.ComponentModel.Design.Serialization;
using System.Drawing;
using System.Drawing.Design;
using System.Reflection;
using System.Resources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.WinForms;

namespace System.Windows.Forms.Tests;

/// <summary>
///  Checks the prototype boundary against the local Designer infrastructure, not the Visual Studio OOP server.
/// </summary>
[Collection("Sequential")]
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
        Assert.IsType<InvalidOperationException>(component.ScopeFailure);
        Assert.False(component.FactoryCalled);
        Assert.Equal(0, component.ProviderCalls);
        Assert.True(component.Site!.DesignMode);
        Assert.Throws<InvalidOperationException>(() =>
            WinFormsServiceActivation.Assign(component, component.Provider, (_, source) => source.GetService(typeof(ServiceToken))));
        Assert.Equal(0, component.ProviderCalls);
    }

    [WinFormsTheory]
    [InlineData("CSharp", false)]
    [InlineData("VisualBasic", false)]
    [InlineData("CSharp", true)]
    [InlineData("VisualBasic", true)]
    public void CodeDomLoader_SaveReloadRenameAndUndoPreserveServiceFreeCode(string language, bool inherited)
    {
        using DesignSurfaceManager manager = new();
        using DesignSurface surface = manager.CreateDesignSurface();
        var services = (IServiceContainer)surface.GetService(typeof(IServiceContainer))!;
        services.AddService(typeof(DesignSurfaceManager), manager);
        MenuCommandService commands = new(surface);
        services.AddService(typeof(IMenuCommandService), commands);
        LocalCodeDomLoader loader = new(language, inherited ? typeof(InheritedPrototypeForm) : typeof(PrototypeForm));
        surface.BeginLoad(loader);
        Assert.True(surface.IsLoaded);
        Assert.Empty(surface.LoadErrors);
        manager.ActiveDesignSurface = surface;
        var host = (IDesignerHost)surface.GetService(typeof(IDesignerHost))!;
        Form root = Assert.IsAssignableFrom<Form>(host.RootComponent);
        Assert.Null(root.RuntimeDispose);
        PrototypeControl? inheritedControl = (root as InheritedPrototypeForm)?.InheritedControl;
        if (inheritedControl is not null)
        {
            var inheritance = (IInheritanceService)surface.GetService(typeof(IInheritanceService))!;
            Assert.Equal(InheritanceLevel.Inherited, inheritance.GetInheritanceAttribute(inheritedControl).InheritanceLevel);
            Assert.Null(inheritedControl.Service);
        }

        ToolboxItem toolbox = new(typeof(PrototypeControl));
        PrototypeControl control = Assert.IsType<PrototypeControl>(Assert.Single(toolbox.CreateComponents(host)!));
        root.Controls.Add(control);
        control.Size = new(240, 120);
        control.Site!.Name = "originalControl";
        using (SingleUndoEngine undo = new(surface))
        {
            using (DesignerTransaction transaction = host.CreateTransaction("Rename control"))
            {
                control.Site.Name = "renamedControl";
                transaction.Commit();
            }

            undo.Toggle();
            Assert.Same(control, host.Container.Components["originalControl"]);
            undo.Toggle();
            Assert.Same(control, host.Container.Components["renamedControl"]);
        }

        surface.Flush();
        string code = loader.LastCode;
        Assert.Contains("InitializeComponent", code);
        Assert.Contains("renamedControl", code);
        Assert.DoesNotContain("originalControl", code);
        Assert.DoesNotContain(nameof(WinFormsUiScope), code);
        Assert.DoesNotContain(nameof(ServiceToken), code);
        Assert.DoesNotContain(nameof(PrototypeControl.InitializeServices), code);
        Assert.DoesNotContain(nameof(Form.RuntimeDispose), code);
        Assert.DoesNotContain("GetService", code);
        CodeMemberMethod initialize = Assert.Single(loader.Document.Namespaces[0].Types[0].Members
            .OfType<CodeMemberMethod>(), method => method.Name == "InitializeComponent");
        CodeObjectCreateExpression creation = Assert.IsType<CodeObjectCreateExpression>(
            Assert.Single(initialize.Statements.OfType<CodeAssignStatement>(),
                statement => statement.Left is CodeFieldReferenceExpression { FieldName: "renamedControl" }).Right);
        Assert.Empty(creation.Parameters);

        loader.ReloadDocument();
        Assert.Empty(surface.LoadErrors);
        Assert.True(root.IsDisposed);
        Assert.True(control.IsDisposed);
        Form reloaded = Assert.IsAssignableFrom<Form>(host.RootComponent);
        PrototypeControl restored = Assert.IsType<PrototypeControl>(host.Container.Components["renamedControl"]);
        Assert.Equal(new Size(240, 120), restored.Size);
        Assert.Same(reloaded, restored.Parent);
        Assert.Null(restored.Service);
        Assert.Null(reloaded.RuntimeDispose);
        if (inheritedControl is not null)
        {
            PrototypeControl restoredInherited = Assert.IsType<InheritedPrototypeForm>(reloaded).InheritedControl;
            Assert.NotSame(inheritedControl, restoredInherited);
            Assert.Null(restoredInherited.Service);
            Assert.Same(reloaded, restoredInherited.Parent);
        }

        using (SingleUndoEngine undo = new(surface))
        {
            var selection = (ISelectionService)surface.GetService(typeof(ISelectionService))!;
            selection.SetSelectedComponents(new object[] { restored }, SelectionTypes.Replace);
            Assert.True(commands.GlobalInvoke(StandardCommands.Delete));
            Assert.True(restored.IsDisposed);
            undo.Toggle();
            PrototypeControl undone = Assert.IsType<PrototypeControl>(host.Container.Components["renamedControl"]);
            Assert.Null(undone.Service);
            Assert.Same(reloaded, undone.Parent);
            undo.Toggle();
            Assert.True(undone.IsDisposed);
        }

        surface.Flush();
        Assert.DoesNotContain("renamedControl", loader.LastCode);
        Assert.Empty(surface.LoadErrors);
    }

    [WinFormsTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void RuntimeSerialization_DoesNotPersistScopeHooksOrChildOwnership(bool serializeForm)
    {
        using ServiceProvider provider = new ServiceCollection()
            .AddScoped(_ => new ServiceToken())
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using WinFormsUiScope application = new(provider);
        WinFormsFormScope lifetime = application.CreateForm(_ => new Form());
        PrototypeControl child = new();
        lifetime.Form.Controls.Add(child);
        lifetime.Assign(child, static (target, services) => target.InitializeServices(services.GetRequiredService<ServiceToken>()));
        Control original = serializeForm ? lifetime.Form : child;
        original.Size = new(240, 120);
        using Container container = new();
        container.Add(original, "runtimeControl");
        CodeDomComponentSerializationService serializer = new();
        using SerializationStore store = serializer.CreateStore();
        serializer.Serialize(store, original);
        store.Close();
        Assert.Empty(store.Errors);

        using Control restored = Assert.IsAssignableFrom<Control>(Assert.Single(serializer.Deserialize(store).Cast<object>()));
        Assert.Equal(original.GetType(), restored.GetType());
        Assert.Equal(new Size(240, 120), restored.Size);
        Assert.Null(TypeDescriptor.GetProperties(restored)[nameof(Form.RuntimeDispose)]);
        if (serializeForm)
        {
            Assert.NotNull(lifetime.Form.RuntimeDispose);
            Assert.Null(Assert.IsType<Form>(restored).RuntimeDispose);
        }
        else
        {
            PrototypeControl restoredChild = Assert.IsType<PrototypeControl>(restored);
            Assert.Null(restoredChild.Service);
            WinFormsFormScope newOwner = application.CreateForm(_ => new Form());
            newOwner.Form.Controls.Add(restoredChild);
            newOwner.Assign(restoredChild, static (target, services) => target.InitializeServices(services.GetRequiredService<ServiceToken>()));
            Assert.NotNull(restoredChild.Service);
            Assert.NotSame(child.Service, restoredChild.Service);
        }

        Assert.NotNull(child.Service);
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

        public PrototypeControl(ServiceToken service) : this()
        {
            InitializeServices(service);
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
    public class PrototypeForm : Form
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
    ///  Models an inherited design with a normally named protected layout field and a runtime-only constructor.
    /// </summary>
    public class InheritedBaseForm : Form
    {
#pragma warning disable CA1051 // Models a protected Designer field for inherited-component discovery.
        protected PrototypeControl InheritedLayoutControl;
#pragma warning restore CA1051

        public InheritedBaseForm()
        {
            InheritedLayoutControl = new PrototypeControl();
            Controls.Add(InheritedLayoutControl);
        }

        public InheritedBaseForm(ServiceToken service) : this()
        {
            InheritedLayoutControl.InitializeServices(service);
        }
    }

    /// <summary>
    ///  Retains service-free creation throughout the inherited form chain.
    /// </summary>
    public class InheritedPrototypeForm : InheritedBaseForm
    {
        internal PrototypeControl InheritedControl => InheritedLayoutControl;
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
            ScopeFailure = Record.Exception(() => new WinFormsUiScope(_provider));
        }

        internal Exception? Failure { get; }

        internal Exception? ScopeFailure { get; }

        internal bool FactoryCalled { get; private set; }

        internal IServiceProvider Provider => _provider;

        internal int ProviderCalls => _provider.Calls;
    }

    /// <summary>
    ///  Retains one real designer undo unit for undo/redo.
    /// </summary>
    private sealed class SingleUndoEngine(IServiceProvider provider) : UndoEngine(provider)
    {
        private UndoUnit? _unit;

        protected override void AddUndoUnit(UndoUnit unit) => _unit = unit;

        internal void Toggle()
        {
            Assert.NotNull(_unit);
            _unit.Undo();
        }
    }

    /// <summary>
    ///  Exercises the real loader with an in-memory CodeDOM document and language-specific code generation.
    /// </summary>
    /// <remarks>
    ///  <para>
    ///   Parse supplies an existing tree. This tests local save/reload, not C#/VB text parsing or the OOP server.
    ///  </para>
    /// </remarks>
    private sealed class LocalCodeDomLoader : CodeDomDesignerLoader
    {
        private readonly CodeDomProvider _provider;
        private readonly ITypeResolutionService _types = new LocalTypeResolutionService();

        internal LocalCodeDomLoader(string language, Type baseType)
        {
            _provider = CodeDomProvider.CreateProvider(language);
            CodeNamespace space = new("DesignerContractTests");
            CodeTypeDeclaration declaration = new("DesignedForm");
            declaration.BaseTypes.Add(baseType);
            declaration.Members.Add(new CodeMemberMethod { Name = "InitializeComponent", Attributes = MemberAttributes.Private });
            space.Types.Add(declaration);
            Document.Namespaces.Add(space);
        }

        internal CodeCompileUnit Document { get; private set; } = new();

        internal string LastCode { get; private set; } = string.Empty;

        protected override CodeDomProvider CodeDomProvider => _provider;

        protected override ITypeResolutionService TypeResolutionService => _types;

        protected override CodeCompileUnit Parse() => Document;

        internal void ReloadDocument()
        {
            LoaderHost.Activate();
            Reload(ReloadOptions.Force);
            Assert.True(ReloadPending);
            Application.RaiseIdle(EventArgs.Empty);
            Assert.False(ReloadPending);
        }

        protected override void Write(CodeCompileUnit unit)
        {
            Document = unit;
            using StringWriter writer = new();
            _provider.GenerateCodeFromCompileUnit(unit, writer, new CodeGeneratorOptions());
            LastCode = writer.ToString();
        }

        public override void Dispose()
        {
            base.Dispose();
            _provider.Dispose();
        }
    }

    /// <summary>
    ///  Resolves the pre-referenced document types and validates toolbox assembly-reference requests.
    /// </summary>
    private sealed class LocalTypeResolutionService : AssemblyNamesTypeResolutionService, ITypeResolutionService
    {
        internal LocalTypeResolutionService() : base(
        [
            typeof(WinFormsServiceActivationDesignerTests).Assembly.GetName(),
            typeof(Form).Assembly.GetName(),
            typeof(Size).Assembly.GetName(),
            typeof(Component).Assembly.GetName(),
            typeof(CodeDomSerializer).Assembly.GetName(),
            typeof(object).Assembly.GetName()
        ])
        {
        }

        void ITypeResolutionService.ReferenceAssembly(AssemblyName name)
            => Assert.NotNull(GetAssembly(name, throwOnError: true));
    }
}
