# WinForms Application Builder DI contract prototype

**Status:** Design prototype from [#14949](https://github.com/dotnet/winforms/issues/14949), implemented as an unshipped runtime prototype in [#14950](https://github.com/dotnet/winforms/issues/14950); proposed signatures still require API review

**Research basis:** [DI and scope architecture](di-scopes-architecture.md)

## Recommendation

Use a runtime activation factory as the only framework-owned service-resolution
boundary for Forms. The activation coordinator supplies the provider belonging
to that activation; the factory returns the fully constructed Form. The Form
does not implement `IServiceProvider`, and the framework does not assign a
general-purpose provider to every component.

The public API shape to prototype is:

```csharp
namespace Microsoft.Extensions.WinForms;

public sealed class WinFormsApplicationBuilder
{
    public WinFormsApplicationBuilder UseStartupForm<TForm>(
        Func<IServiceProvider, TForm> factory)
        where TForm : Form;
}
```

This is a candidate signature, not an approved or shipped API. The current
prototype tracks the overload in `PublicAPI.Unshipped.txt`. The factory is
called only by the runtime activation path. Its provider is the activation
scope's service provider; scope creation and disposal are owned by the
coordinator, not by the Form or factory. A factory exception fails activation
and must not trigger a silent parameterless-constructor fallback.

## Construction and Designer contract

The Form keeps a parameterless constructor for Designer and legacy use. Runtime
code opts into service-aware construction through the factory:

```csharp
public partial class MainForm : Form
{
    public MainForm()
    {
        InitializeComponent();
    }

    public MainForm(ICustomerService customerService)
        : this()
    {
        CustomerService = customerService;
    }

    private ICustomerService? CustomerService { get; }
}

builder.UseStartupForm(
    services => ActivatorUtilities.CreateInstance<MainForm>(services));
```

Designer-generated construction remains unchanged: the Designer can call the
parameterless constructor and execute `InitializeComponent` without a host,
service registration, or runtime scope. The existing startup-Form instance
overload also remains unchanged and does not claim to retroactively perform
constructor injection.

The Form's `InitializeComponent` still constructs child UserControls and
components in the normal way. This prototype does not inject into those
descendants. Their owning Form can compose them with explicit, typed
dependencies after initialization:

```csharp
public MainForm(ICustomerService customerService)
    : this()
{
    _customerControl.CustomerService = customerService;
}
```

This keeps service requirements visible in application code without giving a
control an arbitrary provider. It is not automatic component injection. Any
framework-managed descendant assignment would require a separate
Designer-approved contract. #14951 validated the current runtime-only boundary:
no Designer-generated service hookup is needed, and the runtime activation
factory does not alter drag/drop, serialization, rename, delete, undo/redo,
reload, or inheritance behavior.

## Contract boundaries

- **Application host:** owns registrations and the root provider. It passes the
  provider for a particular activation to the runtime coordinator.
- **Activation coordinator:** creates and owns the activation scope, invokes
  the factory, and retains the scope until the Form's owning lifetime ends.
  Scope behavior is implemented in #14950.
- **Factory:** synchronously constructs one Form from the supplied provider.
  It does not own or dispose the provider/scope.
- **Form:** receives required services as ordinary constructor parameters and
  stores them as fields/properties. It is not a service locator and does not
  own its activation scope.
- **Designer:** continues constructing the Form through its normal
  parameterless path and does not load the application host or scope.
- **Existing callers:** continue using `new Form()`, `UseStartupForm<TForm>()`,
  or the existing instance overload without change.
- **Designer validation:** service-aware Form, UserControl, and component
  constructors remain separate from the Designer's parameterless construction
  path. Focused regression coverage is in
  `System.Windows.Forms.Design.Tests.ApplicationBuilderDesignerCompatibilityTests`.

The same factory boundary is exposed through `WinFormsApplication.CreateForm`
for modeless Forms and `WinFormsApplication.ShowDialog` for modal dialogs.
Modeless scopes remain alive through canceled closes and are cleaned up when
the Form is disposed or the application loop ends. Modal helpers dispose the
dialog and scope after the modal operation, including exceptional exits. A
startup Form supplied as an instance, by a parameterless factory, or as the
`MainForm` of a supplied context also receives an activation scope. The
application context remains the application lifetime owner and does not itself
acquire a Form scope.

## Alternatives reviewed

| Contract | Decision |
|---|---|
| Constructor injection through an activation factory | Recommended for runtime Form roots. It uses normal constructor semantics, supports required dependencies and derived Forms, keeps the provider at the framework boundary, and leaves parameterless Designer construction available. |
| `IServiceProviderAssignable : IServiceProvider` | Rejected as proposed. It makes arbitrary service lookup part of every implementing component and blurs assignment with service location. Removing `: IServiceProvider` would narrow the declared interface but would not prevent the same locator behavior. |
| General property injection | Not selected for the root Form contract. Reflection-based property discovery can hide required dependencies and ordering. A future property-assignment contract would need explicit opt-in, required/optional rules, and a post-`InitializeComponent` point that also handles nonvisual components. |
| Designer-generated service hookup | Not selected for this prototype. It changes serialized/generated source and needs explicit C# and VB Designer-team validation for round trips, rename/delete, undo/redo, reload, inheritance, and out-of-process loading. #14951 owns that investigation. |
| Static or ambient provider | Rejected. It obscures scope ownership, permits design-time code to depend on runtime state, and violates the no-global-service-locator constraint. |

## Prototype validation plan

Before finalizing the public API:

1. Verify that a parameterless Designer construction path and a factory-created
   DI path both initialize the same Form exactly once.
2. Verify factory failures propagate and dispose the activation scope, and that
   any Form returned before a later activation failure is disposed with it.
3. Verify the factory receives the Form activation scope, not the root provider,
   and that separate Form/dialog activations do not share scoped services.
4. Verify a factory is not invoked by builder configuration, `Build`, or
   design-time construction.
5. Review factory overload naming, inference, exception behavior, and nullable
   annotations with the API review team before considering the unshipped
   signatures final.
6. Agree with the Designer team on a separate descendant-assignment mechanism
   before claiming DI support for Designer-created UserControls/components.

The current source does not expose a DI registration surface on
`WinFormsApplicationBuilder`; the caller continues to configure and own the
Generic Host. Scope identity, disposal, failure cleanup, and the explicit
descendant-composition boundary are covered by #14950 tests. API and Designer
review remain necessary before these candidate signatures can be considered
final.

## Follow-up ownership

- #14950: implemented scope-aware Form activation, modal/modeless lifetime,
  and cleanup. The public factory signatures remain pending API review.
- #14951: validated that the provisional root-factory contract needs no
  Designer production-code or generated-code hook. A future contract that
  automatically assigns services to descendants would require a separate
  Designer review and round-trip validation.
- #14952: add the factory, activation failure, scope identity, disposal, and
  design-time compatibility tests.
