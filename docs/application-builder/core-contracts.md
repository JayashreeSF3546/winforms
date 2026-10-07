# WinForms Application Builder core contracts

**Status:** Contract prototype for issue [#14942](https://github.com/dotnet/winforms/issues/14942)

**Architecture decisions:** [lifetime architecture](lifetime-architecture.md)

**DI and scope research:** [Designer-safe DI and UI scopes](di-scopes-architecture.md)

**Service infrastructure architecture:**
[service boundaries and defaults](service-infrastructure-architecture.md)

**DI contract prototype:** [runtime factory contract](di-contract-prototype.md)

**Parent proposal:** [#14082](https://github.com/dotnet/winforms/issues/14082)

## Contract boundary

The builder places `WinFormsApplicationBuilder`, `WinFormsApplication`,
`WinFormsApplicationLifetime`, and the internal `WinFormsApplicationOptions`
in the `Microsoft.Extensions.WinForms` namespace in `System.Windows.Forms.dll`.
This follows the proposal's single-assembly option. `WinFormsApplicationBuilder`
implements `IHostApplicationBuilder` and delegates configuration, service
registration, logging, metrics, and host creation to
`Microsoft.Extensions.Hosting`. Runtime coordination continues to own the
WinForms message loop and coordinate the built host's lifetime.

The builder supports selecting a form by type or instance, or selecting a
default or supplied `ApplicationContext`. The last startup-selection call wins.
Generic form selection stores a factory; it does not instantiate a control
during builder creation, `Build`, or option copying. `Build` snapshots the
builder's options so subsequent builder changes do not alter an already-built
application.

The options type is internal. Applications continue to own their generated
`ApplicationConfiguration.Initialize()` call; the builder does not attempt to
reference application-specific generated code.

## Configuration and options (#14957)

The parameterless `CreateBuilder()` preserves the hostless path: it does not
create a host unless a host-building surface such as `Configuration`,
`Services`, or `Environment` is accessed. `CreateBuilder(args)` and
`CreateBuilder(HostApplicationBuilderSettings)` explicitly opt into a
builder-owned Generic Host. The default content root is
`AppContext.BaseDirectory`; the settings overload can choose another content
root or environment. Standard Generic Host defaults provide optional base and
environment-specific JSON files, Development-only User Secrets when an
application User Secrets ID exists, environment variables, command-line
arguments, and reload support. Providers appended to `Configuration` have
higher precedence than the defaults.

Options bind and validate through the standard Microsoft.Extensions.Options
extensions. `ValidateOnStart()` runs when the host starts, before
`Application.Run` enters the WinForms message loop; the original validation
exception is propagated to the caller. A supplied, already-built host via
`UseHost` is an alternative composition mode: host configuration and service
registration cannot be combined with it, and the builder reports that
conflict rather than silently ignoring either host.

```csharp
WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder(args);
builder.Services.AddOptions<WindowOptions>()
    .Bind(builder.Configuration.GetSection("Window"))
    .ValidateOnStart();
builder.UseStartupForm<MainForm>();

using WinFormsApplication application = builder.Build();
application.Run();
```

## Lifetime contract

The application exposes one lifetime object with `ApplicationStarted`,
`ApplicationStopping`, and `ApplicationStopped` events. Internal notification
methods are one-shot and retain the required ordering; a stop after failed
startup does not synthesize an `ApplicationStarted` notification. Event-handler
exceptions propagate to the runtime coordinator, which must preserve cleanup
and failure semantics when it is implemented.

## Deferred to issue #14943

The runtime added by #14943 runs on the calling UI thread, installs the
WinForms synchronization context before creating a deferred startup form, and
uses the existing `Application.Run(ApplicationContext)` message loop. A
configured `IHost` is started before the WinForms started notification and is
stopped before an intercepted thread exit is allowed to unwind the loop. The
application owns and disposes a host passed to `UseHost`. Host-originated
stopping notifications are marshalled to the UI thread while the coordinator
is active.

Application-context exit deferral is an internal hook used to keep the message
pump responsive while asynchronous host stop callbacks finish. Ordinary
WinForms contexts without a configured host retain their existing synchronous
exit behavior. A host stop is terminal: after the host has begun stopping, an
application shutdown request is not canceled by a form-close veto.

The lifetime bridge introduced by #14943 continues to accept an already-built
`IHost`; #14957 also lets the application builder create and own the host
through `IHostApplicationBuilder`. Existing C# and Visual Basic examples using
an externally built host remain in [samples](samples/README.md). A standalone
lifecycle and resource benchmark harness is documented in
[benchmarks](benchmarks/README.md).

## Scope-aware Form activation

Issue #14950 adds a service-aware startup Form factory and
`WinFormsApplication.CreateForm` / `ShowDialog` activation methods. Each
activated Form uses a host-created service scope when a Generic Host is
configured; without a host, the factory receives an empty service provider.
The Form's `Disposed` event ends its scope, so a canceled close keeps scoped
services alive. Modal activation disposes the dialog and scope before
returning, including when display or disposal throws. When the application
message loop exits, it disposes any activated modeless Forms that remain open.

UserControls and components remain Designer-constructed. A Form factory can
explicitly assign dependencies to these descendants; the framework does not
inject an ambient provider or create descendant scopes. Automatic Designer
integration remains assigned to #14951. The new API signatures are unshipped
and require API review before release.

## Alternatives considered

- **A separate hosting assembly:** deferred. The proposal recommends the
  single-assembly option for the core types, and this prototype has no
  independent package dependency that would justify a second assembly.
- **Implementing a message loop or a host adapter here:** rejected. WinForms
  already owns message-loop behavior, and implementing runtime coordination
  here would overlap #14943.
- **Adding dependency-injection or application-configuration APIs now:**
  rejected. Those APIs and application-specific initialization semantics are
  outside the minimal contract prototype.
