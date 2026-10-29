# WinForms Application Builder: designer-safe DI and UI scopes

Status: research and architecture recommendation for
[dotnet/winforms#14948](https://github.com/dotnet/winforms/issues/14948).
An internal contract prototype for dotnet/winforms#14949 is described below.
The subsequent dotnet/winforms#14950 section records explicit scope-aware activation.
This document does not approve or implement a public DI contract.

Parent story: [dotnet/winforms#14947](https://github.com/dotnet/winforms/issues/14947).
Proposal: [dotnet/winforms#14082](https://github.com/dotnet/winforms/issues/14082).
Dependency: the [host/lifetime implementation](winforms-application-builder-lifetime.md)
from [dotnet/winforms#14940](https://github.com/dotnet/winforms/issues/14940).

## Scope and recommendation

Use explicit runtime activation factories as the composition boundary. Factories supply typed
dependencies through constructors, or explicitly initialize designer-created instances after their
constructors and `InitializeComponent` have completed. Controls and components receive the services
they use, not a stored `IServiceProvider`. Runtime assignment is opt-in; existing components and
applications do not acquire a DI requirement.

Retain service-free construction for designable types, existing generated code, `ISite` services,
and designer transactions. Do not initialize a host, build an application service provider, or
resolve runtime services in the Designer. Do not introduce global, static, thread-local, or ambient
service lookup. Injecting a provider into every control would still encourage a service locator,
even if that provider were scoped.

Represent UI lifetime nesting explicitly, independently of the DI container. Modeless forms own
distinct scopes; modal invocations own distinct scopes bounded by their owners. UserControls and
components borrow their form's scope. An application-level owner tracks every active UI lifetime,
not just `ApplicationContext.MainForm`.

This is the recommended direction for the next prototype, not a declaration that all lifecycle
seams are solved. The most-derived disposal boundary, async-only service disposal, and actual
out-of-process Designer behavior are explicit prototype gates below.

## Research baseline and evidence

The local source was inspected at `70d2aacd8`, which includes the host/lifetime stream.
The checkout centrally selects `Microsoft.Extensions.Hosting` 10.0.10 in
[Directory.Packages.props](../../Directory.Packages.props).
The external DI implementation references below are pinned to the corresponding runtime tag.

| ID | Inspected source | Finding |
|---|---|---|
| R1 | [WinFormsApplicationBuilder.cs](../../src/System.Windows.Forms/Microsoft/Extensions/WinForms/WinFormsApplicationBuilder.cs), lines 69-74, 93-102, 119-125, 137-181 | Generic startup currently requires `new()`; instance ownership transfers on successful build; the context factory is deferred. Build does not activate UI. Constructor DI must not silently change these existing paths. |
| R2 | [WinFormsApplication.cs](../../src/System.Windows.Forms/Microsoft/Extensions/WinForms/WinFormsApplication.cs), lines 155-180, 203-235, 286-323 | Host startup precedes UI activation; the UI synchronization context is installed before activation. UI cleanup completes before the host barrier is released. Scope cleanup must join that boundary, not run from a later host-stopped callback. |
| R3 | [ToolboxItem.cs](../../src/System.Windows.Forms.Design/src/System/Drawing/Design/ToolboxItem.cs), lines 255-305 | Toolbox creation delegates to the designer host and calls `InitializeNewComponent`; initialization failure destroys newly created components. Runtime registration must not participate in this transaction. |
| R4 | [DesignerHost.cs](../../src/System.Windows.Forms.Design/src/System/ComponentModel/Design/DesignerHost.cs), lines 921-1019 | The design surface constructs the component before the host performs normal siting. Creation uses a design-time license context. Delete removes and disposes within the designer transaction. `DesignMode` cannot be assumed available at the start of every constructor. |
| R5 | [DesignSurface.cs](../../src/System.Windows.Forms.Design/src/System/ComponentModel/Design/DesignSurface.cs), lines 271-297 | The implementation first tries a public parameterless constructor, then an `IContainer` constructor for components, then reflection fallback. A nearby summary has a different ordering; the implementation is the evidence. There is no general constructor-service resolution contract here. |
| R6 | [DesignerSerializationManager.cs](../../src/System.Windows.Forms.Design/src/System/ComponentModel/Design/Serialization/DesignerSerializationManager.cs), lines 219-370 | Deserialization can recycle named instances, delegate component creation to `IDesignerHost`, or invoke `TypeDescriptor`. Creation is not a single universal reflection hook that can safely be replaced by DI. |
| R7 | [CodeDomSerializerBase.cs](../../src/System.Windows.Forms.Design/src/System/ComponentModel/Design/Serialization/CodeDomSerializerBase.cs), lines 1873-1930, 2142-2151 | Creation can use an existing expression, `InstanceDescriptor`, resources, or a public parameterless constructor. Hidden serialization metadata is checked separately from property browsing. Do not assume a service property is safe merely because it is not browsable. |
| R8 | [DesignerHost.Site.cs](../../src/System.Windows.Forms.Design/src/System/ComponentModel/Design/DesignerHost.Site.cs), lines 197, 220-256; [CodeDomDesignerLoader.cs](../../src/System.Windows.Forms.Design/src/System/ComponentModel/Design/Serialization/CodeDomDesignerLoader.cs), lines 786-869 | Designer sites expose design mode; renaming raises change notifications and updates generated declarations. Loading and saving deserialize/serialize through the designer pipeline. Scope identity must not depend on a generated field name. |
| R9 | [BasicDesignerLoader.cs](../../src/System.Windows.Forms.Design/src/System/ComponentModel/Design/Serialization/BasicDesignerLoader.cs), lines 720-773; [DesignerHost.cs](../../src/System.Windows.Forms.Design/src/System/ComponentModel/Design/DesignerHost.cs), lines 685-775 | Reload can flush, unload, and reload the document; host unload disposes components and designers. Any runtime-assignment state must stay out of these recreated graphs and serialization sessions. |
| R10 | [UndoEngine.UndoUnit.AddRemoveUndoEvent.cs](../../src/System.Windows.Forms.Design/src/System/ComponentModel/Design/UndoEngine.UndoUnit.AddRemoveUndoEvent.cs), lines 26-35, 70-99; [UndoEngine.UndoUnit.RenameUndoEvent.cs](../../src/System.Windows.Forms.Design/src/System/ComponentModel/Design/UndoEngine.UndoUnit.RenameUndoEvent.cs), lines 27-36 | Undo/redo serializes and recreates components, destroys them through the host, or changes the site name. Neither an object instance nor its name is a durable runtime ownership record across designer operations. |
| R11 | [InheritanceService.cs](../../src/System.Windows.Forms.Design/src/System/ComponentModel/Design/InheritanceService.cs), lines 51-210; [ComponentCodeDomSerializer.cs](../../src/System.Windows.Forms.Design/src/System/ComponentModel/Design/Serialization/ComponentCodeDomSerializer.cs), lines 116-137 | Inherited components are discovered from initialized base fields and have inherited/read-only metadata. The serializer avoids changing privately inherited components. Derived hookup must not reconstruct or reassign inaccessible base-owned objects. |
| R12 | [Form.cs](../../src/System.Windows.Forms/System/Windows/Forms/Form.cs), lines 3244-3261, 3515-3578, 5727-5884, 6737-6891 | Modeless closing, owned-form disposal, and modal execution are different paths. Modal execution hides the dialog and destroys its handle but does not generally dispose the form. A canceled close is not the end of a scope. |
| R13 | [Control.cs](../../src/System.Windows.Forms/System/Windows/Forms/Control.cs), lines 2613-2625, 4994-5094; [UserControl.cs](../../src/System.Windows.Forms/System/Windows/Forms/UserControl.cs), lines 29-35 | Ancestor design-mode detection requires a site/parent; it is not a constructor-time runtime switch. Control disposal disposes children before calling the component base. It cannot prove that an arbitrary most-derived `Dispose` override has finished. |
| R14 | [ApplicationContext.cs](../../src/System.Windows.Forms/System/Windows/Forms/ApplicationContext.cs), lines 50-61, 93-111, 143-151 | Replacing `MainForm` changes handle-destruction subscriptions, not a DI ownership tree. Context disposal disposes its current main form; custom contexts may manage more UI. Handle recreation must not terminate a form scope. |

These are source findings, not new runtime or Designer execution results.
Existing `DesignerSerializationManagerTests`, `DesignerHostTests`, `InheritanceServiceTests`,
and `ControlCodeDomSerializerTests` provide useful extension points. They do not establish that
a new assignment contract works, or that Visual Studio's out-of-process Designer supports it.

### External constraints

- [.NET DI guidelines](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/guidelines):
  the built-in container does not provide automatic property injection or hierarchical child
  containers. Do not resolve disposable scoped/transient dependencies from the root provider or
  dispose injected container-owned dependencies individually.
- [ServiceProviderEngineScope, v10.0.10](https://github.com/dotnet/runtime/blob/v10.0.10/src/libraries/Microsoft.Extensions.DependencyInjection/src/ServiceLookup/ServiceProviderEngineScope.cs):
  `CreateScope` delegates to the root provider. Each scope has its own scoped-instance cache.
  Synchronous disposal rejects async-only disposables, and a throwing service disposer interrupts
  that scope's disposal loop. A coordinator cannot promise to repair the container's internal
  cleanup by calling `Dispose` again.
- [ActivatorUtilities, v10.0.10](https://github.com/dotnet/runtime/blob/v10.0.10/src/libraries/Microsoft.Extensions.DependencyInjection.Abstractions/src/ActivatorUtilities.cs):
  runtime activation can reuse constructor selection and factory infrastructure, but multiple
  constructors and service availability affect selection. A design-time parameterless constructor
  must not become a silent fallback when a required runtime service is missing.
- [Out-of-process Designer architecture](https://devblogs.microsoft.com/dotnet/custom-controls-for-winforms-out-of-process-designer/):
  actual controls execute in `DesignToolsServer`; Visual Studio uses proxies. The application's
  live service provider and scopes cannot be treated as cross-process Designer objects.
- [Local OOP serialization documentation](../designer/modernization-of-code-behind-in-OOP-designer/modernization-of-code-behind-in-oop-designer.md):
  the Visual Studio pipeline uses Roslyn for source processing. Local CodeDOM/DesignSurface tests
  are necessary evidence, not a replacement for Designer-team review and real C#/VB round trips.

## Alternatives evaluated

| Approach | Benefits | Limitations and decision |
|---|---|---|
| Marker interface alone | Small opt-in surface; avoids changing existing types. | Does not specify assignment, readiness, lifetime, or cleanup. Insufficient as the bridge. |
| `IServiceProviderAssignable` | Can attach services after parameterless construction and serve existing generated children. | A stored provider encourages service lookup throughout UI code; it does not define scope ownership or revoke captured services safely. Do not select as the default. If prototyped for comparison, keep lookup inside composition code and evaluate whether the interface is necessary at all. |
| Automatic property injection | Can reach instances created by `InitializeComponent` without new constructors. | Built-in DI does not do this. Reflection scanning, setter ordering, required/optional rules, inherited properties, and serialized state introduce complexity. Reject implicit scanning and convention-based injection. |
| Explicit typed property/method assignment | Preserves design-time construction; dependencies remain visible and testable; no general-purpose provider on controls. | Requires an explicit composition step and readiness validation before use. Recommend typed assignment callbacks/methods; properties, when needed, must be nonserialized and have clear single-assignment rules. |
| Constructor injection everywhere | Strong dependency requirements and immutable fields. | Designer/toolbox creation and generated child constructors do not supply runtime services. Support in runtime factories, not as the only creation model for designable types. |
| Explicit activation factories | Centralize service resolution, thread checks, ownership, errors, and assignment to an already-created graph. | Require lifecycle integration and an explicit path for nested controls/components. Recommend as the primary runtime boundary. A typed factory must not simply expose arbitrary service resolution to UI code. |
| Generated Designer hookup | Could discover nested members and emit strongly typed assignment. | Requires C#/VB parsing/generation, inheritance rules, rename/delete/undo/reload support, and OOP team agreement. Not the prerequisite for the first runtime prototype; investigate only in the owning designer issue. Never hand-edit generated `InitializeComponent`. |
| Replace `TypeDescriptor`, `ISite`, or all activation globally | Appears to intercept many creation paths. | Conflates designer services with application services and affects non-builder applications. Reject process-wide providers, site replacement, and hidden ambient activation hooks. |

Factories plus typed assignment provide the best initial balance: ordinary design-time construction,
no mandatory provider lookup in UI code, explicit failure/ownership paths, and no new serialization
or reflection traversal on every control creation. This is a qualitative assessment; no DI
performance improvement or regression budget is claimed without prototype measurements.

## Service activation decisions

1. **Explicit opt-in.** Preserve existing `UseStartupForm<TForm>()`, supplied-instance, and custom
   context behavior. A future scoped-factory path must be distinct; merely selecting `UseHost`
   must not reinterpret every `new Form()` or every control in a legacy application.
2. **Runtime constructors.** A factory can resolve required typed dependencies and construct a form,
   UserControl, component, or context. Designable types retain a service-free construction path
   through their inheritance chain. Constructor-only runtime types are possible but are not
   promised toolbox/design-surface support.
3. **Designer-created graphs.** `InitializeComponent` remains responsible for creating its existing
   objects. A factory explicitly initializes opted-in children and components after that method
   returns, without replacing them, changing `Site`, or scanning all writable properties.
4. **Readiness.** Finish assignment and validation before the factory publishes the graph or the
   coordinator shows it. Constructors, property getters used by the Designer, and `EndInit` must
   not require runtime assignment that has not happened yet. Defer service-dependent work until
   runtime readiness; do not invoke overridable assignment callbacks from base constructors.
5. **Required services.** An explicitly selected runtime path fails with the original activation
   error if a required dependency is absent. Do not retry parameterless creation, install a no-op
   service, or use a global/root fallback. Explicit constructor selection must avoid
   `ActivatorUtilities` selecting the design-only constructor because a service is unavailable.
6. **Optional services.** Optionality is declared in the typed factory/assignment contract, with
   an intentional null/default branch. Nullable annotations alone do not request optional DI.
   Missing optional services are different from required-service resolution failures.
7. **Assignment identity.** Initialization is once per instance/declaring inheritance layer.
   Conflicting reassignment, activation after disposal, and cross-owner reuse fail explicitly.
   Renaming a component does not reassign it. Existing instances keep their current caller-owned
   resources unless an explicit new contract transfers ownership.
8. **Service-free operation.** Design-time creation and non-builder applications remain ordinary
   service-free paths. Missing runtime services must never be inferred to mean "design mode."
   Design-time preview data, if offered, is separately configured and must not construct the host.

No public interface name, provider-taking overload, member-discovery attribute, or generated-code
format is committed by these decisions. The prototype must compare the minimum factory/callback
surface with an assignment interface before proposing API entries.

## Scope ownership model

| Resource | Owner and service scope | End condition |
|---|---|---|
| Root provider / singletons | Existing host owns its provider and container-created singletons. UI infrastructure borrows it only at composition boundaries. No second provider is built. Caller-supplied service instances retain their documented external ownership. | Host disposal, after UI and UI-owned scopes finish. |
| Application UI scope | The built application's lifetime coordinator owns an optional, explicit scope for its DI-enabled context/application collaborators. No scope is needed for service-free applications. This is distinct from the host root. | After tracked UI lifetimes and their scopes are cleaned up, before host disposal and the UI-completion barrier is released. |
| Custom `ApplicationContext` | Runtime factory creates the context on the owning STA using application-scope dependencies. Context/UI ownership remains with the existing runner. The context does not create another root provider. | Existing context teardown plus application-scope cleanup. Switching `MainForm` does not implicitly discard or merge other form scopes. |
| Modeless form, including startup form | One explicit form-lifetime record owns one form service scope and the factory-created form. Independent top-level forms have distinct scopes. Explicitly owned child windows are tracked as lifetime children. | Actual form disposal or committed owner/application cleanup; not a vetoed close, `Hide`, or handle recreation. |
| Modal dialog | One invocation record owns a fresh dialog and distinct scope. Its logical parent is the explicit owner lifetime; unowned dialogs belong to the application lifetime. | On return or failure of the modal invocation: dispose the dialog, then its scope, in `finally`. Do not wait for `FormClosed` to dispose the scope. |
| UserControl / nested controls | Borrow the containing form's scope through explicit typed assignment. Parent control collection/factory owns the UI object; it does not own the borrowed provider/services. No automatic independent scope. | UI disposal releases its own resources/subscriptions; the containing form later disposes the shared scope. |
| Nonvisual component | Borrow the explicitly identified form/context scope. Generated `components` containers or an explicit caller own the component itself, never a new service root. A runtime factory for an unattached component requires an explicit lifetime owner. | Component disposal releases its resources/subscriptions, not the shared services. |

### Logical nesting is not container nesting

Creating a scope from another scope with Microsoft DI creates an independent scope, not a child
container inheriting the parent's scoped instances. "Nested modal scope" means a **UI lifetime
relationship** here. Two dialogs resolve different scoped instances, even when they share an owner.
The coordinator must explicitly track descendants and terminate them before their owner.

If a dialog needs its owner's current unit of work, pass that specific typed collaborator as a
borrowed dependency and retain the owner for the invocation. Do not implement provider chaining,
copy registrations, or imply that modal services automatically share all parent-scoped state.
The borrowing dialog must not dispose that owner-owned collaborator.

Reparenting a control does not magically rebind its dependencies. Moving an assigned control to
another active form scope must fail or use an explicit detach/recreate protocol agreed in the
prototype; do not silently retain a disposed old scope or dispose the new owner's services.
Unregistered `new`/`Show`/`ShowDialog` calls retain existing WinForms behavior; only explicit scoped
activation obtains these ownership guarantees.

## Disposal, modal behavior, and failures

The target cleanup sequence is:

1. Commit stopping and reject new child activation.
2. Unwind modal work; close/dispose descendant UI on its owning STA.
3. Dispose controls, components, and their runtime subscriptions while their dependencies are live.
4. Dispose each completed child scope, then the owning form scope.
5. Dispose remaining application UI/context resources and the application scope.
6. Signal UI cleanup completion to the existing host barrier; complete host shutdown/disposal.

This is required integration work for the scope implementation, not behavior added by this
research. R2's current cleanup has no scope owner yet. Scope completion must precede its
`_uiStopped` signal; subscribing only to `ApplicationStopped` would be too late.

Important boundaries:

- A canceled `FormClosing` keeps the scope alive. `FormClosed` precedes all disposal work and
  is not a general scope-cleanup boundary. `HandleDestroyed` also occurs during recreation.
- `ShowDialog` generally returns a hidden form whose native handle is destroyed, not a disposed
  object (R12). A scoped modal wrapper must dispose both dialog and scope after return or failure.
  Reusing that completed dialog with old scoped dependencies is not supported by this model;
  another invocation creates another dialog.
- Do not register a factory-created form/UserControl/component as an ordinary container-owned
  disposable as well as a WinForms-owned object. Factories own the UI result; the scope owns
  resolved dependencies. Existing explicitly registered container-owned UI requires a separately
  specified ownership path or rejection, not accidental double disposal.
- A `Disposed` event callback is not proof that all code in a most-derived override has returned.
  The prototype must compare an explicit activation/ownership wrapper with the lifecycle hook
  needed for ordinary close and direct `Dispose`. If an opt-in base-last disposal convention is
  necessary, document and test it; do not claim arbitrary inherited overrides are already safe.
- Detach lifetime handlers and runtime event subscriptions and release per-instance records.
  Use the instance/ownership record, not `Name`, as identity. Do not keep a static registry.
- On constructor failure, the constructor remains responsible for resources allocated before it
  throws. The coordinator disposes its successfully created scope. On later assignment/show
  failure, it also attempts cleanup of every successfully returned UI object it owns.
- Preserve the activation error and surface independent cleanup errors using the existing
  lifetime error-aggregation pattern. Continue attempts for independent child lifetimes/scopes.
  Do not claim every service was disposed if a container-owned disposer throws; the selected
  container can stop its own disposal loop. Fault tests must expose that limitation.

### Async-only dependencies are a prototype gate

`IServiceScope.Dispose` is not a valid general cleanup path for async-only services. Blocking on
an arbitrary `DisposeAsync` from a UI event can deadlock; moving all disposal to `Task.Run` would
also violate thread affinity for controls or UI-affine synchronous dependencies.

The next prototype must explicitly choose and validate either a documented synchronous-only scope
contract that rejects unsupported dependencies, or an asynchronous service-cleanup boundary that
the application/owner tracks before signaling UI completion. It must not silently skip async
disposal, swallow the container's exception, or release an owner before child cleanup completes.
UI objects stay STA-owned; any supported async-only service must not depend on a dispatcher that
has already terminated. No async scope API or unsupported-service detection mechanism is approved
by this research.

## Designer and inheritance contract

| Operation | Required behavior for the proposed opt-in model |
|---|---|
| Creation / toolbox drag-drop | Use normal service-free Designer construction, siting, and `InitializeNewComponent`. No application host or runtime scope is constructed. Existing `IContainer` constructors keep their semantics. |
| Runtime creation | Enter the explicit factory on the owner STA, create/select the proper scope, construct the UI graph, assign typed dependencies, validate readiness, then publish/show it. |
| Save / serialization | Do not serialize providers, scopes, runtime services, readiness state, registrations, or ownership records into code/resources. Prefer methods/private state; any exposed service property must have both appropriate browsing and `DesignerSerializationVisibility.Hidden` metadata. |
| Load / deserialization | Existing CodeDOM/TypeDescriptor/DesignerHost paths continue; service-free instances are sufficient. Runtime-only assignment is not replayed from saved data. |
| Rename | Existing site/change-service and code-generation operations remain authoritative. No runtime scope creation, service reassignment, or name-keyed lifetime record. |
| Delete | Existing designer removal/disposal/transaction behavior remains. A design-time instance owns no runtime service scope to tear down. |
| Reload / unload | Dispose/recreate normal design-time graphs; do not preserve application providers, runtime-assignment flags, or old object references between sessions. |
| Undo / redo | Normal serialization/restoration and removal remain sufficient; restored instances do not cause service resolution or require provider state from the removed object. |
| Base/derived forms and controls | Every designable base must support service-free creation. Base and derived `InitializeComponent` retain their existing construction order and ownership; private/read-only inherited members are not rewritten. Runtime assignment occurs after construction, once per participating layer. |

An inherited form whose base requires constructor services cannot be made designable merely by
adding a parameterless constructor to the derived form. A constructor-DI runtime overload can
delegate layout initialization to a service-free constructor, but required dependencies must be
validated on the explicit runtime path. Do not call a virtual injection method from a constructor,
initialize the same base twice, or resolve services inside designer-visible property getters.

Design-time absence is intentional, not evidence that a required runtime dependency is optional.
`Site.DesignMode`, ancestor site checks, and license usage describe parts of the Designer context;
none replaces the explicit separation of service-free construction and runtime composition.

### Out-of-process agreement still required

The local evidence does not establish a supported extension point inside Visual Studio's private
Designer server/code-generation pipeline. Before choosing generated hookup or changing designer
contracts, the Designer team must agree:

- where and when opt-in metadata is inspected in the server, including inherited/nested objects;
- how design-only construction remains independent of application service registration;
- whether assignment requires generated code at all, and how C#/VB ownership/round trips work;
- which runtime-only state is hidden from the property browser, serialization, and undo stores;
- how drag/drop, rename, delete, reload, undo/redo, and inheritance are exercised in real OOP tests;
- supported SDK/Visual Studio/target-architecture combinations and actionable design-time errors.

No application provider, service instance, or scope is serialized through client/server proxies.
No Designer-team agreement, successful Visual Studio exercise, or new Designer compatibility
guarantee is claimed here.

## Prototype checks and child-issue boundaries

The following are acceptance inputs for later issues, not tests or features added in this issue.
Each child remains independently implemented and reviewed.

| Owning issue | Required follow-up |
|---|---|
| [dotnet/winforms#14949](https://github.com/dotnet/winforms/issues/14949) | Compare a minimal explicit factory/typed callback against a provider-assignment prototype. Prove service-free design construction, constructor selection without missing-service fallback, assignment timing, base/derived behavior, and the most-derived disposal seam. Decide/document async-only support before finalizing APIs. |
| [dotnet/winforms#14950](https://github.com/dotnet/winforms/issues/14950) | Implement the application/form/modal ownership records, parent-bounded cleanup, borrowed child participation, thread checks, and the pre-UI-completion cleanup seam. Prove dialog/form/scope ownership has no competing disposers. |
| [dotnet/winforms#14951](https://github.com/dotnet/winforms/issues/14951) | Agree OOP contracts with the Designer team and validate actual C#/VB drag/drop, save/load, rename, delete, reload, undo/redo, inherited forms/controls, and non-builder compatibility. Add generation only if the validated approach requires it. |
| [dotnet/winforms#14952](https://github.com/dotnet/winforms/issues/14952) | Add deterministic activation/readiness/missing-service tests; same-form shared dependencies versus distinct form/dialog scopes; child-before-owner disposal; close veto/hide/handle recreation; context main-form replacement; direct/unshown/repeated disposal; constructor/assignment/show/scope/disposal failures; and actual designer serialization exclusions. Tests accompanying earlier functional changes still belong with those changes. |
| [dotnet/winforms#14953](https://github.com/dotnet/winforms/issues/14953) | Add C#/VB examples of runtime constructor injection, explicit typed assignment to designer children/components, modeless/modal lifetimes, context navigation, cancellation, and disposal observation. Explain owner-state borrowing versus independent modal scoped instances. |
| [dotnet/winforms#14954](https://github.com/dotnet/winforms/issues/14954) | Execute thousands of create/show/close/dispose cycles; track scope/service disposal, weak references, memory, USER/GDI/native handles, event subscriptions, and owner registries on both loop backends. Distinguish legacy baselines from DI-attributable growth. |

Compatibility coverage must include an application with no builder/DI and an existing builder
application with no scoped-activation opt-in. Parameterless constructors are still legitimate
runtime constructors there; the missing-service failure rule applies to the explicitly selected
DI path, not to all WinForms objects.

The unresolved resource growth and hosted retention recorded by
[dotnet/winforms#14946](https://github.com/dotnet/winforms/issues/14946) remain separate baseline
findings. This document neither fixes them nor treats their presence as proof of a DI leak.
Likewise, it does not close the host/lifetime stream's pending API, CI, or environment acceptance.

## Internal contract prototype (dotnet/winforms#14949)

The prototype implements the factory/callback alternative without adding public API or selecting
`IServiceProviderAssignable`. It is available to the runtime and friend test assemblies, not to
application code. It does not change `Form`, `UserControl`, `Component`, or Designer generation.

### Implemented boundary

- Internal `WinFormsApplicationBuilder.UseStartupFormFactory(Func<IServiceProvider, Form>)` selects
  an explicitly host-backed startup path. Build requires a host selection but does not invoke the
  factory. The factory runs on the caller-owned STA after host startup and WinForms synchronization
  context installation. Disposing an unrun application does not invoke it.
- Internal `WinFormsServiceActivation.Create` accepts a borrowed provider, an explicit constructor
  factory, and an optional assignment callback. Constructor choice belongs to the factory; it
  never retries the parameterless constructor after failed required-service resolution.
- `Create` rejects null results and validates an existing control handle's thread ownership,
  control disposal, and design-time state before accepting the returned object. A handleless
  control/nonvisual component's construction thread cannot be established from the component
  model; creating fresh instances on the activation STA is a caller obligation, not inferred
  cross-thread safety. If its assignment callback subsequently fails, it
  attempts disposal of that owned UI result. Independent assignment/disposal failures are both
  surfaced. A constructor/factory that throws before returning an object still owns its partial
  allocations; the helper cannot recover an object it never received.
- Internal `Assign` initializes an existing component through an explicit callback. Ownership
  stays with the caller/parent container, including when assignment fails. The callback receives
  the provider at the composition boundary; the target receives typed dependencies, not a provider.
  A parent factory can assign its existing `InitializeComponent` children without recreating them.
- Design-time activation is rejected using the current license usage mode before invoking a
  factory, and using target/ancestor designer sites before invoking an assignment callback.
  These guards supplement the explicit runtime path; they are not a universal design-mode detector.
- Required versus optional resolution is explicit in the callback. Typed receivers enforce their
  own single-assignment/readiness rules, including a separate assigned flag when null is a valid
  optional value. `IComponent` has no universal disposed/readiness contract: the helper can check
  `Control.IsDisposed`, but nonvisual receivers must enforce their own disposed-state checks.
- Existing generic/instance/context selections remain service-free. No existing public overload
  changes meaning, no service provider is put on a control/site, and no global instance table,
  property scanning, automatic graph traversal, or new service container is introduced.

### Why not the other contract shapes?

| Candidate | Prototype conclusion |
|---|---|
| Factory plus explicit typed assignment | Selected internally: construction, optionality, failure cleanup, and ownership are visible at the composition root. Designer-created child objects are preserved. |
| Provider-assignment interface | Adds a public implementation obligation without solving ownership/readiness; storing it on a control also exposes general service lookup. The prototype needs no such interface. Reconsider only with a demonstrated scenario that cannot use a typed callback. |
| Universal constructor/property injection | Rejected: the former changes design-time creation requirements; the latter needs reflection, conventions, or generated member discovery. The prototype deliberately makes neither change. |
| Generated hookup | Not required for explicit assignment. Source generation and OOP contract changes remain subject to Designer-team agreement in dotnet/winforms#14951. |

### Disposal and async boundary

The helper owns only a successfully accepted factory result until it returns that result to the
caller. It does not dispose borrowed services, create/dispose scopes, or attach a scope-disposal
handler to `Disposed`. After successful activation, existing runner/control/container ownership
disposes UI; the supplying host/provider still owns its dependencies. Therefore dependencies remain
available to most-derived UI cleanup, including code after `base.Dispose`, until provider disposal.

This resolves the prototype's service-assignment ownership boundary, not the general form-scope
problem. The tests exercise that ordering for startup UI; a future per-form scope must find a safe
boundary for direct `Dispose` and inherited overrides before adopting the same guarantee.

Async-only dependencies can be borrowed like other dependencies: this prototype performs no service
disposal and makes no synchronous scope-disposal promise. A caller-owned provider retains its
normal disposal obligations. UI scopes, async service-cleanup coordination, modal/modeless tracking,
navigation, and parent-child cancellation remain dotnet/winforms#14950 work. No async public API is
approved by this step.

### Validation and limitations

Focused runtime checks cover deferred activation, real host startup, STA/synchronization context,
required/optional dependencies, existing child/control/component assignment, service ownership,
factory/assignment failures, independent cleanup errors, and invalid activation targets.
Both message-loop backends are exercised for the hosted success/failure paths.

Local Designer checks use the real `DesignSurface`, toolbox, site rename/delete, undo/redo,
reload, and component serialization infrastructure. They verify service-free nested/inherited
control construction, rejected runtime assignment to designer-owned instances, and omission of
runtime-only service properties while ordinary layout state round-trips. These checks live in the
runtime unit-test assembly, which already has internal runtime access, and use public Designer
infrastructure; no new friend-assembly grant is required.

Local validation (Debug, x64):

```powershell
.\build.cmd -projects src\test\unit\System.Windows.Forms\System.Windows.Forms.Tests.csproj -verbosity minimal
```

Result: passed, zero warnings and zero errors.

```powershell
$env:DOTNET_ROOT=Join-Path $PWD '.dotnet'
& .\artifacts\bin\System.Windows.Forms.Tests\Debug\net11.0-windows7.0\System.Windows.Forms.Tests.exe `
  --filter-class 'System.Windows.Forms.Tests.WinFormsServiceActivationTests' `
  --filter-class 'System.Windows.Forms.Tests.WinFormsServiceActivationDesignerTests' `
  --filter-class 'System.Windows.Forms.Tests.WinFormsApplicationBuilderTests' `
  --filter-class 'System.Windows.Forms.Tests.WinFormsApplicationLifetimeTests' `
  --filter-class 'System.Windows.Forms.Tests.WinFormsApplicationRuntimeTests' `
  --filter-class 'System.Windows.Forms.Tests.WinFormsApplicationHostingTests' `
  --no-progress --report-trx
```

Result: 129 passed, zero failed, zero skipped. This includes the new runtime and local Designer
checks alongside the existing builder, lifetime, runtime, and hosting regression tests.
Targeted product/test `dotnet format whitespace --no-restore --verify-no-changes` checks also passed.

These are not actual Visual Studio OOP tests or generated C#/VB round-trip certification.
Designer-team agreement, drag/drop interaction in Visual Studio, and real C#/VB save/load,
rename/delete/reload/undo/redo remain dotnet/winforms#14951 acceptance work. Comprehensive scope
tests, samples, and leak testing remain with their assigned later issues. Remote CI must be
verified separately; local build/test results do not imply CI success.

## Scope-aware activation (dotnet/winforms#14950)

This step extends the internal prototype, not the public API surface. New internal builder
selections `UseScopedStartupForm` and `UseScopedApplicationContext` explicitly opt into synchronous
UI scope ownership. They require a host and remain deferred through Build. Existing startup,
context, and unscoped service factories keep their previous behavior, including the unscoped
prototype's post-base-disposal service availability.

### Owners and activation

`WinFormsUiScope` borrows the host provider and owns one application service scope plus explicit
form lifetime records. A context factory receives the application-scope provider and coordinator
only at the composition boundary. Typed navigation callbacks can capture the coordinator without
putting a general service provider on controls. Context replacement of `MainForm` neither merges
nor loses independently tracked form scopes, including unshown or hidden forms.
An initial non-null `MainForm` must come from this coordinator's `CreateForm`; an invalid foreign
context/form is rejected without taking its ownership. Its factory retains cleanup responsibility.

`CreateForm` creates a fresh DI scope and invokes the explicit constructor factory. Its
`WinFormsFormScope` owns the returned form and that scope. An optional owner record defines logical
nesting, independently of the flat DI container. Factories must return fresh, hidden, top-level,
caller-STA-owned forms, never container-owned UI or a previously registered form. A handleless
form's construction thread still cannot be inferred. No service-free constructor fallback occurs.

The lifetime's `Assign` callback borrows the form provider for already-created UserControls and
components. It rejects controls attached to another form or previously assigned by another lifetime.
A control's sparse property store holds only an opaque ownership token, not a provider or lifetime
record; this also prevents explicit reassignment after unmanaged reparenting without retaining
disposed scopes. Construction and container/parent
ownership of those objects remain explicit, as in the previous prototype; typed receivers enforce
readiness and once-only assignment. No additional child-control or component scope is created.
Unmanaged reparenting, native owner changes, and raw `new`/`Show`/`ShowDialog` calls do not establish
or migrate coordinator ownership. Cross-form borrowing requires an explicitly passed typed
collaborator; it must not be inferred from parent containers.

### Selected disposal boundary

The selected scoped-form contract is **base-last disposal**: every derived form override must finish
service-dependent cleanup before calling `base.Dispose(disposing)`, and must reach that base call.
The internal, per-instance `Form.RuntimeDispose` hook runs only for opted-in forms and wraps the
existing Form disposal body. The hook uses the existing sparse property store, not an extra
instance field on every legacy form. It does not subscribe to `FormClosed`, `HandleDestroyed`, or `Disposed`.
The original disposal body is unchanged for all other forms.

This choice supports ordinary modeless `Close` and direct `Form.Dispose`, including unshown forms.
A canceled close, hide, or handle recreation does not dispose a scope. The hook disposes tracked
descendants, attempts normal form/control/component teardown, and then disposes services on the
owning STA. Derived parent cleanup before the hook can precede child teardown; dependencies remain
alive during that cleanup. Child service scopes complete before the parent service scope.
The hook is detached and ownership records are removed when their lifetime completes.

This is not a guarantee for arbitrary code after a derived form's base call. A base Form method
cannot observe completion of an arbitrary most-derived override. Deferring to a posted callback
would depend on dispatcher lifetime and could run early under reentrant message pumping; using
only an external wrapper would not cover normal window-close/direct-dispose paths. These
alternatives were rejected for this synchronous opt-in stage. The convention must be revisited
before approving public scoped-activation APIs; existing unscoped forms are not opted in silently.

If a derived override throws before reaching base disposal, coordinator-driven cleanup still
attempts descendants and scope cleanup, and reports the UI error. It cannot repair arbitrary
partial user cleanup or promise that the UI object became fully disposed. Likewise, a container
disposer can stop that container's internal cleanup; independent form scopes are still attempted.

### Modal nesting and shutdown

`WinFormsFormScope.ShowDialog` owns one invocation and always attempts form/scope cleanup after
the invocation returns or fails. A new invocation requires a new lifetime. Reentering an already
active modal invocation is rejected without tearing it down.

If an owner is disposed programmatically while a nested modal call is active, descendant UI is
disposed to unwind the modal loop, but its service scope stays live until the call returns.
Disposed ancestor scopes are retained until those descendant invocations unwind, then released
child-first. Thus a dialog's logical scope cannot outlive its owner scope, even when native owner
UI has already gone away. No blocking wait or timer is introduced into disposal.

Application stopping rejects new activation. After the runner has unwound its loops and disposed
the context, coordinator cleanup also covers hidden/unshown and independent forms. Application
scope cleanup precedes the existing UI-completion signal and host disposal. The host still owns
root services; the coordinator never disposes the root provider.

### Synchronous service-cleanup boundary

Owned scopes support synchronous `IDisposable` dependencies. Async-only disposable services are
unsupported in these scopes: the container's synchronous-disposal exception is surfaced. There
is no blocking `DisposeAsync`, hidden thread-pool disposal of UI-affine services, or silent skip.
The container may mark a scope disposed before throwing and cannot be repaired by retrying it;
the application must correct the unsupported registration. Borrowing async-only dependencies
from an externally owned provider in the unscoped prototype remains a separate supported scenario.

A future asynchronous lifetime contract must specify dispatcher affinity and parent/child
completion before it can replace this boundary. This step does not introduce that public contract.

### Validation and remaining work

Focused checks accompany this implementation: distinct scoped identities and borrowed children,
direct/unshown disposal, descendant cleanup, close veto/hide/handle recreation, modal return and
owner-triggered unwind, context navigation, cancellation, stopping guards, scope/factory/show/
disposal failures, and explicit async-only rejection. Hosted modal/navigation/cancellation paths
exercise both message-loop backends.

Local validation (Debug, x64):

```powershell
.\build.cmd -projects src\test\unit\System.Windows.Forms\System.Windows.Forms.Tests.csproj -verbosity minimal
```

Result: passed with zero warnings and zero errors.

```powershell
$env:DOTNET_ROOT=Join-Path $PWD '.dotnet'
& .\artifacts\bin\System.Windows.Forms.Tests\Debug\net11.0-windows7.0\System.Windows.Forms.Tests.exe `
  --filter-class 'System.Windows.Forms.Tests.WinFormsUiScopeTests' `
  --filter-class 'System.Windows.Forms.Tests.WinFormsServiceActivationTests' `
  --filter-class 'System.Windows.Forms.Tests.WinFormsServiceActivationDesignerTests' `
  --filter-class 'System.Windows.Forms.Tests.WinFormsApplicationBuilderTests' `
  --filter-class 'System.Windows.Forms.Tests.WinFormsApplicationLifetimeTests' `
  --filter-class 'System.Windows.Forms.Tests.WinFormsApplicationRuntimeTests' `
  --filter-class 'System.Windows.Forms.Tests.WinFormsApplicationHostingTests' `
  --no-progress --report-trx
```

Result: 150 passed, zero failed, zero skipped (21 new scope cases and 129 existing regression cases).

```powershell
$env:DOTNET_ROOT=Join-Path $PWD '.dotnet'
& .\artifacts\bin\System.Windows.Forms.Tests\Debug\net11.0-windows7.0\System.Windows.Forms.Tests.exe `
  --filter-class 'System.Windows.Forms.Tests.FormTests' `
  --filter-method '*Dispose*' --filter-method '*Close*' --no-progress --report-trx
```

Result: both matching existing Form regression cases passed, zero failed or skipped.
Targeted product/test `dotnet format whitespace --no-restore --verify-no-changes` and
`git diff --check` also passed. No full-suite, architecture-matrix, leak, or Visual Studio
validation is claimed by these targeted runs.

Remote CI, actual OOP Designer agreement/integration (dotnet/winforms#14951), broader scope tests
(dotnet/winforms#14952), C#/VB samples (dotnet/winforms#14953), and dedicated leak runs
(dotnet/winforms#14954) remain separate acceptance work. No success for these is implied here.

## Research completion

This issue supplies a source-backed comparison, the recommended activation model, application/
form/dialog/control/component/context ownership rules, disposal and failure constraints, designer
operation requirements, inheritance rules, and explicit prototype/OOP approval gates.

The original dotnet/winforms#14948 change was documentation-only and claimed no build/test/CI
execution. The subsequent internal prototype above belongs exclusively to dotnet/winforms#14949.
The separate dotnet/winforms#14950 section records the subsequent scope implementation.
Designer integration remains with its owning child issue.
