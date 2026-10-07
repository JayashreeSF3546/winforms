# WinForms Application Builder DI contract prototype

**Status:** Design prototype for issue [#14949](https://github.com/dotnet/winforms/issues/14949); proposed signatures require API and Designer review

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

This is a candidate signature, not an approved or shipped API. The factory is
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
framework-managed descendant assignment requires a separate Designer-approved
contract and is deferred to #14951. The runtime activation factory does not
require Designer-generated code and therefore does not alter drag/drop,
serialization, rename, delete, undo/redo, reload, or inheritance behavior.

## Contract boundaries

- **Application host:** owns registrations and the root provider. It passes the
  provider for a particular activation to the runtime coordinator.
- **Activation coordinator:** creates and owns the activation scope, invokes
  the factory, and retains the scope until the Form's owning lifetime ends.
  Scope behavior itself is implemented in #14950.
- **Factory:** synchronously constructs one Form from the supplied provider.
  It does not own or dispose the provider/scope.
- **Form:** receives required services as ordinary constructor parameters and
  stores them as fields/properties. It is not a service locator and does not
  own its activation scope.
- **Designer:** continues constructing the Form through its normal
  parameterless path and does not load the application host or scope.
- **Existing callers:** continue using `new Form()`, `UseStartupForm<TForm>()`,
  or the existing instance overload without change.

The same factory boundary can be reused for modeless Forms and modal dialogs,
but their scope lifetimes and cleanup are not implemented by this prototype.
The application context remains the application lifetime owner and does not
itself acquire a Form scope.

## Alternatives reviewed

| Contract | Decision |
|---|---|
| Constructor injection through an activation factory | Recommended for runtime Form roots. It uses normal constructor semantics, supports required dependencies and derived Forms, keeps the provider at the framework boundary, and leaves parameterless Designer construction available. |
| `IServiceProviderAssignable : IServiceProvider` | Rejected as proposed. It makes arbitrary service lookup part of every implementing component and blurs assignment with service location. Removing `: IServiceProvider` would narrow the declared interface but would not prevent the same locator behavior. |
| General property injection | Not selected for the root Form contract. Reflection-based property discovery can hide required dependencies and ordering. A future property-assignment contract would need explicit opt-in, required/optional rules, and a post-`InitializeComponent` point that also handles nonvisual components. |
| Designer-generated service hookup | Not selected for this prototype. It changes serialized/generated source and needs explicit C# and VB Designer-team validation for round trips, rename/delete, undo/redo, reload, inheritance, and out-of-process loading. #14951 owns that investigation. |
| Static or ambient provider | Rejected. It obscures scope ownership, permits design-time code to depend on runtime state, and violates the no-global-service-locator constraint. |

## Prototype validation plan

Before finalizing or adding a public API:

1. Verify that a parameterless Designer construction path and a factory-created
   DI path both initialize the same Form exactly once.
2. Verify required-service resolution failures propagate and the activation
   coordinator disposes a partially created Form and scope.
3. Verify the factory receives the Form activation scope, not the root provider,
   and that separate Form/dialog activations do not share scoped services.
4. Verify a factory is not invoked by builder configuration, `Build`, or
   design-time construction.
5. Review factory overload naming, inference, exception behavior, and nullable
   annotations with the API review team before adding it to the public API
   baseline.
6. Agree with the Designer team on a separate descendant-assignment mechanism
   before claiming DI support for Designer-created UserControls/components.

The current source has no DI registration surface on
`WinFormsApplicationBuilder`; this document intentionally does not add a
runtime API or claim that the prototype validation plan has already passed.
The factory signature remains a design proposal until the tests and API/Designer
reviews above are completed.

## Follow-up ownership

- #14950: implement scope-aware Form activation, modal/modeless lifetime, and
  cleanup after the factory contract is approved.
- #14951: determine whether nested UserControls and components need
  Designer-generated hookup or another explicit assignment protocol.
- #14952: add the factory, activation failure, scope identity, disposal, and
  design-time compatibility tests.
