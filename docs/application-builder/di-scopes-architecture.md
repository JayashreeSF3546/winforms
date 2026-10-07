# WinForms Application Builder DI and UI scopes

**Status:** Research recommendation for [#14948](https://github.com/dotnet/winforms/issues/14948), with the runtime scope prototype implemented for [#14950](https://github.com/dotnet/winforms/issues/14950). The added public signatures are tracked as unshipped and remain subject to API review.

**Parent feature:** [#14947](https://github.com/dotnet/winforms/issues/14947)

**Lifetime foundation:** [core contracts](core-contracts.md) and [lifetime architecture](lifetime-architecture.md)

## Decision summary

Keep design-time construction independent from the runtime application's service
container. Use an explicit runtime activation factory for Form roots; the
activation coordinator supplies that activation's provider, and constructor
injection is appropriate at the root. Preserve the parameterless constructors
used by Designer-generated code and existing applications.

Designer-created child controls and nonvisual components are constructed inside
`InitializeComponent`, not by the application factory. The root factory does
not inject into those descendants. Do not give components a global or ambient
provider, or expose arbitrary lookup as the default assignment contract. A
separate Designer-approved descendant-assignment mechanism remains open for
#14951. The root factory candidate is documented in
[the DI contract prototype](di-contract-prototype.md); it is not an approved API.

The Generic Host owns the root provider for the application lifetime. Create
activation scopes only for Form and dialog roots. UserControls and components
receive explicitly selected dependencies from the owning Form's activation
factory; they do not create scopes or receive an ambient provider. Scope
ownership is explicit and independent of native window ownership.

## Findings from current WinForms behavior

### Designer creation and serialization

- `DesignSurface.CreateInstance` tries a parameterless constructor first, then
  an `IContainer` constructor for components, and finally a parameterless
  `Activator.CreateInstance` fallback. This is a design-time creation path, not
  an application DI activation hook
  (`src\System.Windows.Forms.Design\src\System\ComponentModel\Design\DesignSurface.cs:271-295`).
- `DesignerHost.CreateComponent` delegates construction to the design surface
  and then sites/adds the instance to its container. Components are removed and
  disposed through the host's component lifecycle
  (`src\System.Windows.Forms.Design\src\System\ComponentModel\Design\DesignerHost.cs:921-961,987-1008`).
- The CodeDOM type serializer constructs the root through the serialization
  manager, then processes the generated initialization statements. Those
  statements construct nested controls and components as part of the user's
  `InitializeComponent` implementation
  (`src\System.Windows.Forms.Design\src\System\ComponentModel\Design\Serialization\TypeCodeDomSerializer.cs:81`).
- The out-of-process Designer runs separately from the application runtime.
  It must be able to load and construct user controls without the application
  host or its runtime scopes
  (`docs\designer\readme.md:1-17`).

Consequently, runtime DI cannot replace all design-time constructors without
also changing the generated-code/designer contract. Requiring a runtime service
in a control's parameterless constructor would break design loading and could
also break existing applications.

### Designer edits, undo, reload, and inheritance

- The designer host reports component rename and removal operations. The
  CodeDOM loader listens to those notifications and updates or removes
  declarations; the basic loader tracks add/remove/change/rename operations
  and performs reloads
  (`src\System.Windows.Forms.Design\src\System\ComponentModel\Design\DesignerHost.Site.cs:254-256`,
  `src\System.Windows.Forms.Design\src\System\ComponentModel\Design\Serialization\CodeDomDesignerLoader.cs:713-773`,
  `src\System.Windows.Forms.Design\src\System\ComponentModel\Design\Serialization\BasicDesignerLoader.cs:673-679,807-834`).
- `UndoEngine` observes component and transaction changes to build undo units
  (`src\System.Windows.Forms.Design\src\System\ComponentModel\Design\UndoEngine.cs:39-57`).
- `InheritanceService` discovers inherited components and marks private or
  assembly-visible inherited members read-only
  (`src\System.Windows.Forms.Design\src\System\ComponentModel\Design\InheritanceService.cs:53-61,152-179`).

Service assignment must not mutate the serialized component graph, rely on
stable designer field names, or create an additional scope when a document is
reloaded or an inherited member is rediscovered. Generated hookup code would
need explicit Designer-team review and undo/redo, rename, delete, reload, and
inheritance coverage; it is not a safe assumption for this research issue.

### Runtime ownership and disposal

- Disposing a `Control` disposes its child controls. `Form.Dispose` also disposes
  owned Forms. These are existing UI ownership relationships, not DI scopes
  (`src\System.Windows.Forms\System\Windows\Forms\Control.cs:4994-5063`,
  `src\System.Windows.Forms\System\Windows\Forms\Form.cs:3515-3574`).
- `Form.Close` initiates normal close processing and disposal for a closed
  Form. A canceled close must leave the Form and its scope alive
  (`src\System.Windows.Forms\System\Windows\Forms\Form.cs:3244-3256`).
- `Form.ShowDialog` runs the modal loop, hides the dialog and destroys its
  handle before returning; it does not dispose the Form. A modal activation
  wrapper must therefore dispose the dialog and its scope in a `finally` path
  (`src\System.Windows.Forms\System\Windows\Forms\Form.cs:5727-5859`).

The owner must dispose controls/components before disposing their scope so
their `Dispose` implementations can still use scoped dependencies. Close
cancellation is not scope disposal. A modal wrapper owns cleanup even when
activation, display, or disposal fails.

## Proposed activation and scope model

The activation methods added for #14950 are prototype public APIs recorded in
`PublicAPI.Unshipped.txt`; they still require API review before release.

| Object/lifetime | Activation and service access | Scope owner and end |
|---|---|---|
| Application | The Generic Host owns the root provider and application lifetime. A custom `ApplicationContext` is part of that lifetime, not a second DI root. | The application disposes activated Forms and their scopes before disposing the host. The host disposes its root provider when the application is disposed. |
| Startup or modeless Form | Create a Form activation scope and invoke the service-aware factory. `CreateForm` returns the modeless Form without showing it. | The Form's scope is disposed when its `Disposed` event is raised, after child controls and container-owned components are torn down. A canceled close keeps both alive. Any activated Forms remaining when the application loop exits are disposed by the application. |
| Modal dialog | `ShowDialog` creates a distinct activation scope, invokes the factory, shows the dialog, and disposes the Form and scope after the modal loop, including failure paths. | The modal invocation owns cleanup. The caller's Form ownership does not transfer DI scope ownership. |
| UserControl | Keep Designer construction parameterless. The root Form factory may explicitly assign declared dependencies after normal control construction. | It participates through dependencies from the owning Form's activation. It receives no automatic provider and creates no scope. |
| Component | Continue to support `IContainer`/Designer construction. The root Form factory may explicitly assign declared dependencies to components it owns. | Its owner disposes it before the Form's `Disposed` event and Form scope. It receives no automatic provider and creates no scope. |
| Inherited Form/control | Activate the concrete runtime derived Form once. Base and derived controls/components share that Form's scope. | The derived Form owns the single scope; inherited members do not create additional scopes. |

### Constructor, optional, and fallback behavior

- Use a service-aware factory at runtime activation roots (startup/modeless
  Forms and modal dialogs). Applications may use constructor injection inside
  the factory, resolving from that activation's scope rather than a static or
  ambient provider.
- Keep the existing parameterless Designer construction path. Constructor
  injection does not make a DI dependency mandatory for a type's design-time
  constructor.
- Treat required constructor dependencies as activation requirements: if a
  required service cannot be resolved, fail activation and clean up the scope.
  Do not silently retry with a parameterless constructor after an activation
  failure; that would hide missing registrations and change the requested
  runtime object.
- Represent optional dependencies through normal optional constructor
  parameters or an explicitly reviewed opt-in assignment contract. Do not
  silently skip an unresolved required dependency.
- Preserve existing forms and components that are not registered with or
  activated by the builder. They continue to use normal WinForms construction.

### Scope nesting and ownership details

The Microsoft DI scope model does not promise hierarchical child scopes that
inherit scoped instances from a parent scope. “Nested” here describes logical
ownership and deterministic cleanup, not parent-scope service inheritance.
Each new Form/dialog activation scope is created by the application activation
coordinator. Dependencies that must be shared from a caller's Form should be
passed explicitly; they must not be obtained through a parent or global
service locator.

An application context owns the application lifetime, not a separate service
scope. Its initial `MainForm`, when present, receives a Form scope. Forms
created later by custom context code should use `CreateForm` or `ShowDialog`;
direct `new Form()` calls remain outside builder activation. Modeless windows
each own their activation scopes, even when one window is the native owner of
another. With no Generic Host, the factory receives an empty provider; when a
host is supplied, its provider must expose `IServiceScopeFactory`.

## Alternatives evaluated

| Approach | Assessment |
|---|---|
| Constructor injection for every control/component | Not compatible with current Designer construction and generated `new` statements for nested controls. Keep it for factory-created scope roots. |
| General property injection | Can preserve parameterless construction, but implicit reflection-based injection hides required dependencies and ordering. Only consider an explicit, opt-in assignment for designer-created descendants; define missing-service and initialization ordering in #14949. |
| `IServiceProviderAssignable : IServiceProvider` | The proposal's sketch is explicit per instance, but exposes arbitrary service lookup to each component and is still a service-locator surface. Do not adopt that shape without API review; prefer assignment of declared dependencies through a narrow contract. |
| Scope-aware activation factories | Best fit for Forms and dialogs: factory creates the scope, activates the concrete type, and can own failure/disposal cleanup. Insufficient by itself for children constructed by designer-generated `InitializeComponent`. |
| Generated Designer hookup code | Could provide a well-defined point to assign dependencies to nested controls/components, but changes generated source and must preserve C#/VB serialization, rename/delete, undo/redo, reload, inheritance, and out-of-process Designer behavior. Defer evaluation/implementation to #14949 and #14951 with Designer-team agreement. |
| Static or ambient provider / service locator | Rejected. Hides ownership, makes design-time behavior process-dependent, and violates the issue's no-global-provider requirement. |

## Open questions and dependencies

1. #14949 must prototype the minimum explicit service-assignment contract for
   Designer-created descendants and prove failure behavior, optional services,
   and initialization ordering. The current proposal's raw-provider interface
   is not accepted as the final shape.
2. The Designer team must agree on whether the runtime can assign services to
   generated controls/components without changing Designer output, or whether
   a generated hookup is required. #14951 owns Designer integration and
   round-trip validation.
3. #14950 implements the Form/dialog activation scopes and disposal contract.
   Automatic dependency assignment for Designer-created descendants remains
   deferred to #14951.
4. Dynamic reparenting of a UserControl across Forms with different scopes
   needs an explicit policy before assignment semantics are finalized. The
   control must not silently retain a stale scope or create a new one.

## Sources reviewed

- `D:\WinForms\API builder\Builder_Requirement.docx`
- `D:\WinForms\API builder\BuilderAPI_ModernizationGoals.docx`
- `D:\WinForms\API builder\BuilderAPI_Proposal.docx`
- `D:\WinForms\API builder\BuilderAPI_Rationale.docx`
- [DI contract prototype](di-contract-prototype.md)
- WinForms designer construction, serialization, inheritance, reload, undo,
  Form close/modal, and Control disposal code cited above.
- Issues [#14947](https://github.com/dotnet/winforms/issues/14947) and
  [#14948](https://github.com/dotnet/winforms/issues/14948).
