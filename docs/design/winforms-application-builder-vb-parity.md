# WinForms Application Builder: Visual Basic capability and compatibility inventory

Status: source-backed requirements baseline for
[dotnet/winforms#14973](https://github.com/dotnet/winforms/issues/14973), pending VB/WinForms stakeholder review.
This document inventories behavior and proposes parity requirements; it does not approve APIs or implement features.

Parent: [dotnet/winforms#14972](https://github.com/dotnet/winforms/issues/14972).
Proposal: [dotnet/winforms#14082](https://github.com/dotnet/winforms/issues/14082).
Foundations: [host/lifetime](winforms-application-builder-lifetime.md),
[DI/UI scopes](winforms-application-builder-di-scopes.md), and
[services](winforms-application-builder-services.md).

## What is being proposed?

VB already provides useful application-level behavior: startup/shutdown events, single-instance activation,
splash screens, settings-on-exit, and network notifications. The parent proposal makes those capabilities
available through a shared, opt-in Application Builder model that both VB and C# can use.
It does **not** remove, obsolete, or require rewriting the existing VB Application Framework.

This first issue answers **what exists, what must be preserved, and where the gaps are**.
The next issue owns the actual shared contracts and migration-adapter design.

```text
Existing VB framework + source + tests + documentation + known issue reports
                                  |
                                  v
                  Inventory behavior and compatibility requirements (#14973)
                                  |
                                  v
             Later, after review: shared contracts -> implementations -> validation

Existing VB application ---------------------> continues using the existing framework
VB or C# application choosing the shared model -> opts into shared capabilities incrementally
```

## Evidence, baseline, and scope

Research baseline: local commit `167789d31aa008ed2bd96e47180fe6f4ba7eeb9c` on
`Application_Builder_14972_VB_Parity`; public upstream issue bodies and cited documentation read on
2026-11-05. Relative source links describe **this checkout**, not every released .NET version.
The builder and companion-package APIs on this branch remain prototypes pending API/package review.

Evidence labels used below:

- **S — source:** verified by reading the linked implementation. This is static analysis, not a runtime observation.
- **D — documented contract:** Microsoft Learn's published behavior. Older VB documentation can describe
  .NET Framework-era experiences; differences from this checkout are called out rather than silently reconciled.
- **K — known report:** an upstream issue/comment, with reported version and status at the research date.
  An open report is not proof of reproduction here; closed does not establish coverage of every variant.
- **R — requirement/recommendation:** proposed compatibility target or constraint for owner/API review,
  not an implemented contract.
- **U — unresolved:** requires owner review, a generated-artifact inspection, or executable/environment validation.

The inventory covers `ApplicationBase`, `ConsoleApplicationBase`, `WindowsFormsApplicationBase`, their event
arguments, single-instance helpers, splash implementation, network availability, application/runtime information,
principal/culture/logging conveniences, settings generation, and application templates.
Adjacent `My` services are identified to avoid treating every VB convenience as a new hosting requirement.

**Compatibility requirement (R):** preserve existing VB application source and binary compatibility, including
public/protected members, virtual overrides, event signatures, generated partial-class patterns, and existing
settings stores. Do not remove/obsolete the framework or silently redirect its `Run` into a different lifecycle.
Migration is opt-in. This documentation change does not alter any of those artifacts.

## Capability and compatibility matrix

Classification describes the **proposed shared-model target**, not completed parity:

- **Full:** preserve the user-visible capability in both languages; exact shared API awaits #14974.
- **Partial:** retain the capability with an explicit compatibility boundary or intentional difference requiring review.
- **Migration-only:** preserve the existing VB convenience and support an opt-in mapping, not a shared language feature.
- **Non-goal:** no new implementation in this stream; existing functionality remains available.

| Capability | Current behavior and dependencies | Compatibility requirement / disposition (R) | Target; owner |
|---|---|---|---|
| Application defaults | S: `OnInitialize` raises `ApplyApplicationDefaults`; applies font, synchronization context, DPI, visual styles, color, and form-reveal policy before normal startup. Event values supersede earlier property values. [Initialization][vb-init], [event arguments][defaults]. | Preserve configuration-before-handles and existing overrides. Shared configuration belongs to existing WinForms mechanisms, not a second VB initialization engine. Default values and precedence need explicit mapping. | Partial; #14974, #14975 |
| Startup | S/D: normal `Startup` precedes default main-form creation; `StartupEventArgs.CommandLine` contains arguments. `OnInitialize` and `OnStartup` are overridable gates. [Lifecycle][vb-model], [startup][vb-startup], [overview][doc-model]. | Provide a deterministic UI-thread startup phase before deferred startup UI; do not relabel the builder's post-activation `ApplicationStarted` event as VB `Startup`. | Full; #14974, #14975 |
| Startup cancellation | S: `OnInitialize = False` or `OnStartup = False` skips `OnRun` and `OnShutdown`; base `OnStartup` resets `Cancel = False`, invokes handlers, returns `Not Cancel`. [Lifecycle][vb-model], [startup][vb-startup]. | Preserve the pre-main-form cancellation capability. Document outcome/cleanup differences between VB cancellation and runner/host token cancellation; an already-created splash still needs cleanup in the shared model. | Full capability, failure-path differences reviewed; #14974, #14975, #14977 |
| Command line | S: `Run(String())` supplies `InternalCommandLine`; otherwise `ConsoleApplicationBase` caches environment arguments excluding executable element zero. Read-only collection, not an options parser. [Console base][console], [Run][vb-run]. | Preserve argument order, empty strings, Unicode, and executable exclusion. Preserve original arguments separately from optional Generic Host key/value configuration. | Full; #14974, #14975 |
| Main form / root context | S: default `OnRun` calls `OnCreateMainForm` if needed and throws `NoStartupFormException` if still absent, then runs the private `WinFormsAppContext`. Main form and splash cannot be the same object. [Run hook][vb-onrun], [properties][vb-properties]. | Reuse the builder's deferred form/context selection; support custom-context/tray migration without requiring a visible main form. Do not assume every override uses base `OnRun`. | Full capability; #14974, #14975 |
| Shutdown policy | S: default enum value is `AfterMainFormCloses`; `AfterAllFormsClose` reassigns the context main form from `Application.OpenForms` until none remain. [Context][vb-context], [enum][shutdown-mode]. | Preserve both policies through the existing context/lifetime foundation. Global `OpenForms` can contain forms from other threads; it is not a new cross-thread ownership contract. | Full capability; #14974, #14975 |
| Shutdown event / cleanup | S/D: after normal `OnRun` return, `OnShutdown` raises `Shutdown(EventArgs)`. It is not in an outer `Finally`. `OnRun` disconnects network and restores the prior synchronization context in its pump `Finally`. [Lifecycle][vb-model], [run/shutdown][vb-onrun], [template][template-events]. | Preserve normal shutdown work, but distinguish this legacy event from once-only builder stopping/stopped notifications, including failed startup. No guaranteed notification on process kill/fail-fast. | Partial ordering/failure semantics; #14974, #14975 |
| Shutdown cancellation | S: no cancellable VB `Shutdown` event. A normal form close or `Application.Exit` can be vetoed by `FormClosing`; shutdown notification is too late. [Application.Exit][application-exit]. | Preserve user-close cancellation. Do not invent a legacy shutdown-cancel event or promise that committed builder/host shutdown is vetoable. | Partial; #14974, #14975 |
| UI exceptions | S/D: adding the first VB `UnhandledException` handler subscribes to `Application.ThreadException`; removing the last unsubscribes. Event defaults `ExitApplication = True`; setting false can allow UI dispatch to continue. [Exception hookup][vb-exception-event], [routing][vb-exception], [contract][doc-exception]. | Explicit opt-in UI policy, not behavior-neutral logging. Preserve original exception and distinguish recoverable dispatch policy from fatal failures. Avoid competing VB/shared subscriptions. | Partial; #14974, #14975; reuse services |
| Startup / shutdown exceptions | S: outside debugger, an outer catch routes exceptions from `OnInitialize`, `OnStartup`, `OnRun`, and `OnShutdown`; no retry/resume of the interrupted sequence. Without a handler, rethrow. Handler exceptions escape rather than recursively route. [Lifecycle][vb-model]. | Cover pre-pump failures explicitly, with deterministic cleanup and no assertion that `ExitApplication = False` resumes startup. Shared runner propagation differs from VB suppression. | Partial; #14974, #14975 |
| Background exceptions | S: no general VB subscription to `AppDomain.UnhandledException` or `TaskScheduler.UnobservedTaskException` in this application base. The detached splash/IPC tasks are not made UI exceptions by the outer catch. [Splash][vb-splash], [Run][vb-run]. | Do not promise arbitrary worker recovery or task observation. Reuse Generic Host worker policy and opt-in diagnostics; terminating exceptions remain terminating. | Partial; #14974, #14975 |
| Single-instance identity | S: calling assembly GUID plus major/minor version; without GUID, module version ID. Identity includes no explicit executable path, user SID, session ID, or elevation discriminator. [Identity][vb-identity], [tests][identity-tests]. | Decide logical identity, version/path policy, and isolation independently of IPC access control. Preserve the legacy path for existing applications; no default identity change here. | Partial; #14974, #14976 |
| Next-instance notification / forwarding | S/D: later process forwards a serialized `String()` and skips its normal application-model startup. First process invokes `StartupNextInstance`, then optionally restores/activates main form. [Run][vb-run], [activation][vb-activation], [helpers][pipes], [overview][doc-model]. | Deliver arguments on the owning UI thread; support a live custom context without a main form. Define acceptance/failure semantics before claiming reliable delivery. | Full capability, transport guarantees unresolved; #14974, #14976 |
| User/session/elevation / races | S: named pipes use `CurrentUserOnly`, byte mode, one server instance; sender timeout is 2,500 ms. No explicit session key or acknowledgement protocol. D: on Windows `CurrentUserOnly` checks account and elevation. [Helpers][pipes], [Run][vb-run], [pipe contract][doc-pipes]. | Isolate users and define session/elevation policy; no insecure global fallback. Validate startup/exit races, malformed clients, and relaunch independently of simple server exclusivity. | Partial; #14976, #14978, #14980 |
| Splash creation / suppression | S/D: once per application object; created by `OnCreateSplashScreen` during initialization; exact `/nosplash` or `-nosplash` skips it, without removing arguments. Form cannot also be main form. [Initialization][vb-init], [splash][vb-splash], [overview][doc-model]. | Preserve opt-in display and suppression intent. Do not infer case-insensitive parsing or introduce host switch parsing into raw argument handling. | Full capability; #14974, #14977 |
| Splash minimum display / close | S/D: default 2,000 ms; positive duration starts a timer on splash execution, main-form `Load` waits, then activates main and marshals splash disposal. [Wait][vb-splash-wait], [hide][vb-hide], [splash][vb-splash], [contract][doc-splash]. | Preserve minimum-display intent, not UI blocking or exact timer/paint timing. Define cancellation/failure/early-close ownership and do not leave splash tasks unobserved. | Partial mechanics; #14974, #14977 |
| Application information | S: lazy cached `Info` uses entry assembly, falling back to calling assembly; `AssemblyInfo` exposes assembly name/version/attributes/path, loaded assemblies, stack trace, working set. Missing attributes return empty strings in source. [Base][app-base], [info][assembly-info]. | Provide language-neutral access using the same metadata sources; host application name/content root are not substitutes for all assembly properties. Review unavailable assembly location in special deployments. | Full capability, deployment limits documented; #14974, #14977 |
| Runtime / computer information | S: `My.Computer.Info` is adjacent `Devices.ComputerInfo`, not a lifecycle service. OS description uses `RuntimeInformation`, OS version uses `Environment`, installed UI culture uses `CultureInfo`; memory has Windows interop dependencies. [Computer info][computer-info], [memory][computer-memory]. | Reuse BCL/retained VB services, not duplicate a machine/runtime telemetry system. Define only required shared application-information projections. | Partial/reuse; #14974, #14977 |
| Network availability | S/D: lazy subscription to `NetworkAddressChanged`; rechecks IP-network availability, emits only state changes, posts to captured context, not an Internet reachability guarantee. Principal/context must exist first; listener disconnected at normal pump exit. [VB subscription][vb-network], [network][network], [contract][doc-network]. | Language-neutral, testable, UI-safe notifications with bounded subscription lifetime, no mandatory network dependency at startup. | Full capability; #14974, #14977 |
| Settings-on-exit | D: mutable flag controls implicit save; explicit `My.Settings.Save()` remains available. S: runtime property only stores a Boolean; generated settings hook `My.Application.Shutdown` and call `Save` when true. Runtime default false; shipped VB application template true. [Property][vb-properties], [generator][settings-generator], [template constructor][template-designer], [contract][doc-settings]. | Preserve existing generated settings/provider/store. Opt-in shared shutdown save must reuse the service stream, define completion/error policy, and avoid a second writer. | Partial / VB adapter migration; #14974, #14975, #14979 |
| Culture / environment | S: current-thread culture/UI culture getters/setters; environment lookup throws for a missing variable, unlike nullable BCL lookup. [Base][app-base]. | Preserve existing VB methods; reuse BCL functionality for shared applications. Do not silently make culture changes process-wide or translate missing variables to configuration defaults. | Migration-only; #14974, #14979 |
| Authentication / `My.User` | S: constructor defaults to Windows authentication, sets thread principal where permitted; `ApplicationDefined` leaves it application-managed. `User` reads/writes `Thread.CurrentPrincipal`. [Constructor][vb-constructor], [User][user]. | Preserve existing principal semantics; do not replace them with a new authentication service or infer IPC identity from `Thread.CurrentPrincipal`. | Migration-only; #14974, #14979 |
| Logging | S: `ApplicationBase.Log` lazily constructs VB `Logging.Log`. Shared package has separate opt-in exception logging. [Base][app-base], [logging][exception-logging]. | Keep VB logging behavior; do not silently redirect to `ILogger`, add providers, or transmit exception/argument/settings data. | Migration-only; reuse Stream C |
| UI threading / helpers | S: primary lifecycle runs on the caller; initialization installs WinForms context; network/activation require marshaling. `DoEvents` forwards to `Application.DoEvents`; `OpenForms` is process-wide. [Initialization][vb-init], [activation][vb-activation], [Run][vb-run], [properties][vb-properties]. | Entry point supplies STA; preserve control affinity and avoid synchronous UI/host waits or a second main message loop. `DoEvents` is not a new async-startup mechanism. | Full UI-safety requirement; helper conveniences migration-only |
| `ApplicationEvents.vb` / project generation | S/D: partial `MyApplication` and `Handles Me.*`; `.myapp` and generated constructor/main-form override configure policy. Project selects `Sub Main`, `MyType=WindowsForms`, and `MyApplicationCodeGenerator`. [Events][template-events], [designer][template-designer], [project][template-project], [data][template-myapp]. | Existing source/generated code remains valid. Shared runtime works from both languages without generating VB-specific lifecycle infrastructure. No regeneration or template change in #14973. | Migration-only; #14974, #14979; coordinate Stream D |
| Designer / default form instances | S: template uses `Me.MainForm = Form1`, a VB default-instance convenience; shared scope activation rejects design-time usage. [Template][template-designer], [activation guard][service-activation]. | Keep designer constructors/`InitializeComponent` service-free; no host, IPC, or settings I/O merely from design-time construction. Default instances are not a C# language feature to replicate. | Migration-only; Streams B/D, #14979 |
| Other `My` features / deployment | S: network upload/download/ping and other `My.Computer`, resources, and language conveniences are adjacent APIs, not `WindowsFormsApplicationBase` lifecycle. [Download][network-download], [upload][network-upload], [ping][network-ping]. | Preserve existing APIs; do not rewrite WebClient, compiler-generated `My`, deployment/ClickOnce infrastructure, or add DI/configuration/logging/settings/template infrastructure here. | Non-goal; coordinate existing owners |

## Lifecycle ordering and cancellation requirements

### Verified normal VB path (S)

For base implementations and a generated entry point, not arbitrary overridden methods:

1. Construct `MyApplication`: Windows principal policy, application context, saved prior synchronization context.
   Generated constructor configures visual styles, single instance, settings flag, and shutdown mode.
2. `Run(args)` stores arguments. If single-instance mode is enabled, attempt primary pipe ownership **before**
   `DoApplicationModel`; a secondary only sends arguments and returns, or throws connection failure.
   Constructors and generated entry-point work can already have run: “before initialization” in documentation
   refers to application-model initialization, not zero process/constructor side effects.
3. `DoApplicationModel` creates startup event arguments and calls `OnInitialize`.
   Base initialization raises `ApplyApplicationDefaults`, sets font before creating a WinForms synchronization
   context/native marshaling window, then applies DPI/styles/color/reveal defaults and starts an optional splash.
4. If initialization succeeds, `OnStartup` creates the requested network listener, raises `Startup`, and checks
   cancellation. The main form is normally not yet constructed; user overrides or preassigned forms can differ.
5. If startup succeeds, `OnRun` creates the main form when absent, adds its `Load` splash coordination handler
   in that path, and enters `Application.Run(_appContext)`.
6. During main-form `Load`, splash coordination waits for any remaining minimum duration and hides/disposes
   the splash before allowing the main form to continue becoming visible.
7. Accepted UI close/context exit ends the main pump. Base `OnRun` disconnects network and restores the previous
   synchronization context in `Finally`.
8. Only after normal `OnRun` return, `OnShutdown` raises `Shutdown`; generated settings autosave participates
   as a subscriber if its default instance has been accessed. After `DoApplicationModel` returns, a primary
   single-instance `Run` cancels its listener and disposes its pipe.

Evidence: [constructor][vb-constructor], [lifecycle][vb-model], [initialization][vb-init],
[run hook][vb-onrun], [Run entry][vb-run], [settings generator][settings-generator].
Do not extrapolate a total ordering across all form constructors, handlers, paints, and other UI threads.
In particular, assigning `MainForm` early bypasses the base `OnRun` branch that installs the splash `Load` hook.

The event-argument types reinforce the boundaries: [`StartupEventArgs`][startup-args] derives from
`CancelEventArgs`; [`StartupNextInstanceEventArgs`][next-instance-args] carries arguments and a mutable
foreground flag, not cancellation; [`UnhandledExceptionEventArgs`][exception-args] carries the exception
and exit policy. These existing signatures remain unchanged.

### Cancellation and abnormal paths

| Path | Verified VB outcome (S) | Shared requirement / unresolved distinction (R/U) |
|---|---|---|
| `OnInitialize` returns false | No `OnStartup`, main pump, or `OnShutdown`. Overrides own any custom work. | Clean up acquired resources, including a splash that might already exist. Define cancellation result in #14974. |
| `StartupEventArgs.Cancel = True` | No `OnRun`/`OnShutdown`; initialization, splash launch, and possibly network subscription already happened. No encompassing cleanup `Finally` in `DoApplicationModel`. | Do not copy possible resource-retention behavior as a parity guarantee. Verify cleanup without claiming the current VB path is fixed. |
| Normal close veto | `FormClosing.Cancel` / canceled `Application.Exit` can keep the pump alive; no `Shutdown` just because closing was requested. | Preserve interactive close veto before committed stop. |
| Exception before/during `OnRun` | Outer catch may route/suppress or rethrow; it does not subsequently execute `OnShutdown`. The pump's own `Finally` applies only if that region was entered. | Separate guaranteed teardown notifications from legacy successful-shutdown handlers and settings-save policy. |
| `Shutdown` handler throws | Non-debugger outer catch routes it; later VB multicast subscribers need not run. | Builder lifetime currently notifies remaining subscribers and aggregates failures: not equivalent. Adapters need explicit ordering/error rules. |
| Process termination / fatal exception | No universal shutdown/cleanup guarantee; the application template explicitly excludes abnormal termination. | Do not guarantee settings flush, splash close, or host graceful stop after kill/fail-fast. |

VB `Shutdown` takes `EventArgs`, not `CancelEventArgs`. A late “cancel shutdown” feature is not existing parity.
The current [shared runner][runner] documents `StopAsync` as **committed** shutdown: form-close veto cannot stop it.
`Run(CancellationToken)` requests shutdown and reports cancellation after cleanup for an accepted run;
an already-canceled token rejects the run before activation. A `StopAsync` token cancels only the caller's wait.
These are not interchangeable with VB's startup `Cancel` flag or an interactive close veto.

## Exception boundaries

The [source outer catch][vb-model] checks `Debugger.IsAttached`; attached debugging bypasses that catch.
[Published exception documentation][doc-exception] also describes debugger/compiler behavior. Do not infer
that every Debug configuration disables the source catch when no debugger is attached; executable matrix
validation must cover attached/unattached and Debug/Release separately.

- **UI dispatch (S/D):** when WinForms routes a dispatch exception to `Application.ThreadException`, the VB
  adapter creates event args with `ExitApplication = True`. VB handler selection changes default WinForms
  exception presentation. Without a VB handler, retain the underlying [WinForms policy][thread-exceptions],
  including exception mode/debugger effects; do not promise every UI exception escapes `Application.Run`.
- **Lifecycle outside the pump (S):** the outer catch routes initialization/startup/run/shutdown exceptions.
  Returning “handled” suppresses propagation but does not resume the failed method or run skipped shutdown.
  `ExitApplication = True` calls `Application.Exit`, which is not an unconditional process kill and can
  encounter form-close cancellation.
- **Handler failure (S):** the reentrancy guard prevents rerouting a failure thrown by a VB unhandled-exception
  handler through the outer catch. Do not create recursive logging/error callbacks in the shared model.
- **Network handlers (S):** the VB application event catches individual handler failures and tries
  `OnUnhandledException`; subscription is to the captured UI context. Failure before entering a user
  handler or after the dispatcher has ended is a separate boundary.
- **Workers/tasks (S/R):** a background thread, unobserved task, detached IPC listener, or splash task is not
  automatically covered by the application-model catch. Await/observe owned async work. Use Generic Host's
  `BackgroundServiceExceptionBehavior` for hosted workers, not an invented guarantee of UI-thread delivery.
- **Fatal/terminating failures (R):** no “continue application” guarantee for fail-fast, stack overflow,
  corrupted state, or an `AppDomain` notification whose `IsTerminating` is true.

The existing [exception-logging prototype][exception-logging] installs UI routing at shared
`ApplicationStarted`, removes it at stopping, and offers `LogRunFailure` for explicitly reported failures
escaping `Run`. It does **not** cover pre-activation startup exceptions with a VB-style event. Optional
process-wide exception subscriptions are diagnostics, do not mark tasks observed, and do not promise recovery.
Reuse this boundary with Stream C instead of introducing a second competing exception observer.

## Single-instance identity, activation, and security boundaries

### Current implementation (S)

[Identity][vb-identity] comes from `Assembly.GetCallingAssembly` at `Run`: `GuidAttribute.Value` concatenated
with major/minor version when available, otherwise `ManifestModule.ModuleVersionId`. Neither a caller-supplied
business ID nor the executable directory is part of this algorithm. Copies with identical metadata therefore
have the same calculated key; GUID-less rebuilds can differ. This is source reasoning, not a reproduced
answer to every historical deployment-path report.

[Pipe helpers][pipes] create a local, inbound, byte-mode, asynchronous named pipe, maximum one server instance,
with `CurrentUserOnly`. The client connects to `"."`, sends `DataContractSerializer` XML for `String()`, and
disconnects. The server reads until end-of-stream, deserializes, invokes the callback, then disconnects and
accepts again. Deserialization failures return no arguments; read/callback exceptions can fault the listener.
The read loop accumulates data in memory with no explicit byte/argument bound in this helper.

Pipe creation catches all exceptions and returns false; `Run` interprets false as “secondary.” Thus lack of
ownership is not distinguished from access denial or another creation failure. Sending has a 2.5-second
cancellation deadline and failures become `CantStartSingleInstanceException` without original details.
The server listener is started without awaiting its task; cancellation is after normal
`DoApplicationModel` return, not in an enclosing listener-cleanup `Finally` (pipe disposal is still in `Using`).

[Activation marshalling][vb-activation] uses `MainForm.Invoke` when present, otherwise
`AsyncOperationManager.SynchronizationContext.Send`. It swallows failures occurring before the delegate has
begun, such as a missing/stopped dispatcher. It does not swallow user-handler failures once entered.
`StartupNextInstance` defaults `BringToForeground` to true; handlers run first, then the main form is restored
from minimized and `Activate` is called when permitted. OS foreground restrictions still apply.

There is **no application-level acknowledgement** that the UI consumed arguments. Successful client write is
not proof that activation ran. Serialized connection handling and a single server limit simultaneous ownership;
they do not prove exactly-once handoff through startup, crash, or shutdown races.

### Boundary requirements, not selected APIs (R/U)

| Boundary | Evidence / limitation | Required decision or validation |
|---|---|---|
| Different Windows users | No SID in name. `CurrentUserOnly` restricts access, not name collisions; #11232 reports a second-user failure. | Isolation must allow legitimate independent users without insecure broad ACLs/global fallback. #14976 defines identity/security; #14980 validates real users. |
| Same user, different sessions | No session ID in identity. User-only access is not a per-session scope declaration. | Proposed safe desktop default: user/session-local coordination, subject to owner review. Explicitly decide same-user multi-session behavior and prevent unintended cross-session focus/handoff. |
| Elevation | D: [CurrentUserOnly][doc-pipes] on Windows checks account **and elevation**; name has no elevation discriminator. | Never silently weaken this check or imply same-account cross-elevation support. Decide separate instances versus clear refusal, and test both launch directions. |
| Different install paths | Current key does not encode path; #11365 reports different behavior on .NET 8 than Framework 4.8. | Agree logical-app versus installation-specific identity; include copied builds, launch mechanisms, GUID/no GUID, and versions. Do not “fix” by unreviewed path hashing. |
| Version / identity collision | GUID with major/minor is stable across patch/build versions; MVID fallback is module-specific. | Document coexistence/upgrades; do not assume all modern projects contain a stable GUID. Validate collision and packaging scenarios. |
| First launch / rapid concurrent launches | Server is acquired before model startup; UI may not yet be dispatching. Only one connection processed at a time; user callback can block it. | Define bounded startup handoff, acceptance semantics, failure diagnosis, and backpressure; test no dispatcher, busy UI, many launches, and malformed/incomplete clients. |
| Exit / immediate reopen / crash | Primary can own pipe after UI exit begins; sender may write before UI delivery fails. | Define whether to refuse/retry/re-elect; no silent acknowledged loss or duplicate processing claims. Use deterministic coordination in tests rather than fixed sleeps. |
| Remote desktop / Terminal Services | Local pipe transport is not remote-machine activation. Session isolation and focus are separate from transport locality. | Test multiple active/disconnected/reconnected sessions. RDP support does not mean opening a remotely reachable IPC endpoint. |

No synchronization primitive, protocol, contract signature, payload limit, or identity API is selected by
this inventory. Those choices require #14974/#14976 review. Existing VB behavior remains untouched.

### Known issue evidence (K)

| Report | Observed upstream evidence at research date | Inventory consequence |
|---|---|---|
| [#11232](https://github.com/dotnet/winforms/issues/11232), open | .NET 8 / .NET 9 preview: second user on Terminal Server/RDP receives `CantStartSingleInstanceException`. Reporter says Framework worked. | Access restriction alone is not adequate multi-user coordination. Not reproduced locally. |
| [#11365](https://github.com/dotnet/winforms/issues/11365), open | .NET 8.0.204: same-folder relaunch detected, copied different-folder application runs separately; Framework 4.8 reportedly differs. | Path/deployment behavior needs controlled repro; do not replace source facts with an inferred cause. |
| [#8441](https://github.com/dotnet/winforms/issues/8441), closed | MDI creation/activation cross-thread failure reported in .NET 6–8; comments verify fixes in .NET 9 main and .NET 8 builds. [Verification comment](https://github.com/dotnet/winforms/issues/8441#issuecomment-1728681546). | Preserve UI-thread event **and activation**; add an MDI scenario, not just receipt of arguments. |
| [#4805](https://github.com/dotnet/winforms/issues/4805), closed | Main-form-null/custom-context notification report; comments explain tray apps and also record an early verification problem. [Scenario](https://github.com/dotnet/winforms/issues/4805#issuecomment-859369370). | Current fallback context-marshalling code is evidence of support intent, not proof of every pre-pump/no-main-form case. |
| [#3296](https://github.com/dotnet/winforms/issues/3296), closed | Request for Framework-compatible major/minor identity; discussion distinguishes projects with a GUID and MVID-only builds. | Current source and identity tests confirm GUID/version versus MVID branches; do not claim universal stable identity. |
| [#4279](https://github.com/dotnet/winforms/issues/4279), open | .NET 5: splash shown only with `Sub Main`, not the configured form startup object. | Entry-point/template participation needs validation independently of splash timing. |
| [#9807](https://github.com/dotnet/winforms/issues/9807), open | Request to modernize the VB network class's obsolete `WebClient` usage. | Availability events are distinct from transfer APIs. Do not implement that network-stack rewrite under splash/application-service parity. |

## Splash timing, threading, failure, and ownership

[Published contract][doc-splash]: minimum display duration is in milliseconds (normally 2,000); a fast main
form should not reveal early, and a slow startup should not add another full delay after it is ready.
[Current source][vb-splash] schedules [`DisplaySplash`][vb-display] with `Task.Run`; that method starts the timer
just before `Application.Run(splash)`. This is not a measurement of first paint and does not create an explicitly
STA-configured dedicated splash thread. The form may have been constructed on the main thread.

The main form's [Load handler][vb-splash-wait] waits on an `AutoResetEvent` released after the timer's
`TaskCompletionSource` completes, then [hides the splash][vb-hide] using synchronous `Invoke`/`Dispose`.
The change avoids a CPU spin but still blocks main UI startup. `ShowSplashScreen` runs only once.
Values at or below zero skip the timer in current source; do not advertise stricter validation without review.

U: startup cancellation, missing main form, constructor/display failure, user closing the splash early,
failure before its handle exists, timer completion during teardown, and exception on the detached splash task
are not covered by a comprehensive splash cleanup `Finally` in the application-model code.
The positive-path test is not evidence that these work.

R: preserve visual intent/suppression and configuration precedence, with explicit ownership and observed failure
completion, UI-thread-affine construction/disposal, and cancellable coordination that does not block the main
dispatcher. Whether a separate splash UI thread is necessary is a #14974/#14977 decision, not a requirement to
duplicate the main host lifetime. Define when minimum timing begins, which paths bypass it during failure/exit,
and how an already-created main form participates. Test milestones using controllable synchronization/time;
do not assert exact wall-clock/paint timings.

## Application services, generated settings, and tooling

### Availability and information

The [network implementation][network] subscribes only when needed, captures a synchronization context after
the application principal/context are established, posts OS callbacks, and only emits changes to the result of
`NetworkInterface.GetIsNetworkAvailable`. It catches selected unsupported/security/network-information failures
during setup; delivery is neither a connectivity SLA nor a guarantee that a remote resource is reachable.
An event can wait behind a busy UI handler. R: use a replaceable/testable availability source and lifetime-bound
UI marshaling; avoid forcing network initialization on applications that do not subscribe.

[Application information][assembly-info] combines cached assembly attributes and live process/domain queries.
It is not the same thing as `IHostEnvironment` or configuration. [Computer information][computer-info] is an
adjacent existing service; reuse `RuntimeInformation`, `Environment`, culture APIs, and existing VB functionality.
U: single-file publishing, trimming/AOT, assembly-location fallback, and runtime/OS metadata differences need
validation before asserting complete deployment parity. No new telemetry/logging destination is implied.

### Settings-on-exit is partly generated behavior

The runtime `SaveMySettingsOnExit` property does **not** itself persist data.
The pinned project-system settings generator, [`AddDefaultInstance`][settings-generator] and
[autosave generation][settings-autosave],
emits a lazy one-time `My.Application.Shutdown` subscription under `_MyType = "WindowsForms"` and calls
`My.Settings.Save()` only if the flag is true. This is external generator evidence, not proof of every installed
Visual Studio generator version. The checkout's application template enables the flag while the base class's
Boolean default is false.

Consequences:

- Autosave depends on generated settings participation/default-instance access and normal `Shutdown` delivery.
  Do not promise saves after canceled startup, forced termination, or every exception.
- Ordering relative to user `Shutdown` handlers follows actual subscription order; no fixed “always save before”
  or “always save after” user code guarantee was established. An earlier throwing handler can prevent autosave.
- Existing `ApplicationSettingsBase` user-scoped properties, provider configuration, upgrade behavior, paths,
  and explicit saves remain supported. Do not automatically convert `Settings.settings` into JSON.
- The [existing service prototype][settings-service] already offers explicit async load/save/upgrade/recovery/import.
  It does not automatically attach VB autosave, choose when to save, or turn configuration JSON into preferences.
  R: reuse it only by explicit opt-in, with one owner for persistence. #14975 owns shared settings-on-exit policy;
  Stream C owns the storage service. Avoid blocking/async-void saves in a terminal UI event.

U: inspect generated `Settings.Designer.vb` from supported installed tooling, including custom providers,
subscription ordering, errors, and explicit-plus-implicit saves in the later compatibility suite.
Do not claim that external generator inspection validates those end-to-end artifacts.

### Language and design-time boundaries

[ApplicationEvents.vb][template-events] is the user-editable partial class; generated
[`Application.Designer.vb`][template-designer] warns against manual edits.
[`Application.myapp`][template-myapp] and [project metadata][template-project] configure generation.
Normal event handlers use `Handles Me.Startup` and analogous events. Default form instances and compiler/project
support for `My.Application`, `My.Settings`, `My.User`, and `My.Computer` are language/tooling conveniences;
the underlying application-service classes can already be consumed from C#.

The [integration-template event file][integration-events] contains a stale “StartupNextInstance not yet
implemented” comment; the current runtime and helper tests contradict treating that comment as a capability
absence. It is not edited here. The shipped template is evidence of current defaults, not proof of every IDE path.

R: keep service-free designable constructors, generated code, `ISite` services, and Designer behavior.
No host startup, single-instance election, exception subscription, or settings I/O from merely creating a control
in the Designer. Reuse Stream B's [runtime activation guards][service-activation].
Actual IDE regeneration, startup-object choices, default-instance behavior, and out-of-process Designer usage
remain executable validation gates. New templates belong to Stream D, not this issue.

## Mapping to existing shared mechanisms (not #14974 contract design)

| Existing mechanism in this checkout | Verified capability | Gap/requirement for parity owners |
|---|---|---|
| [WinFormsApplicationBuilder][builder] | Public `UseStartupForm`, deferred `UseApplicationContext`, `UseHost(IHostBuilder)`, `Build`; successful build transfers ownership. Factories/services/scopes include internal prototypes. | Select startup without premature UI construction. No raw-argument store, cancellable VB startup event, single-instance, or splash feature is supplied by these methods. Do not expose internal scope APIs by implication. |
| [WinFormsApplication][runner] | Caller-owned STA; one root pump; optional host startup off UI before deferred UI; startup/run/cleanup failure collection; cancellation and committed asynchronous stop. | Use this runner rather than invoking a nested VB `Run`. Reconcile pre-pump startup, secondary-instance early exit, cancellation results, and resource cleanup. |
| [WinFormsApplicationLifetime][lifetime] | UI-owner-thread, ordered, once-only started/stopping/stopped; started after UI activation, before pump; no started on failed startup. Subscriber failures collected after other subscribers. | Not VB `Startup` or successful-only `Shutdown`. Preserve existing milestone meanings; decide adapter placement/error semantics in #14974. |
| [Host adapter][host], [UI stop barrier][barrier] | Real `IHost`/`IHostApplicationLifetime`; UI completion precedes host teardown, provider ownership retained by existing adapter. | Host tokens are not UI events or cancellable VB events. Hosted startup must not wait for UI dispatch. Attach async shutdown work within the existing coordinated lifetime, not a competing host. |
| [DI/scope foundation][scopes] | Explicit runtime activation, UI ownership, designer guards; internal prototype constraints. | Track splash/network/activation owners through the established lifetime; do not introduce a VB-only container, ambient provider, or new scope manager. |
| [Configuration extension][configuration] | Opt-in Generic Host configuration/command-line providers; validation may fail before deferred UI. | Raw VB arguments and forwarded activation payloads are not configuration reloads. Preserve raw arguments; review mixed switch syntax before passing it to a key/value provider. |
| [Settings service][settings-service], [exception logging][exception-logging], [package guidance][hosting-package] | Typed persistence/import, opt-in diagnostics, options and hosted-service integration already exist as prototypes. | Reuse policy seams; no new settings/logging/configuration infrastructure. Startup exception and save-on-exit policy gaps remain, not solved merely by registration. |

## Decisions, rejected alternatives, and review gates

These are inventory recommendations (R), not public API approvals:

1. **Keep the legacy framework unchanged and migration opt-in.** Reject replacement/obsoletion or silently
   adapting every existing `Run`; virtual overrides and generated patterns make that a compatibility hazard.
2. **One shared primary UI lifetime.** Reject a second VB host/message-loop system or running VB `Run` inside
   the builder. Reuse Streams A/B/C; the established builder also remains useful without a Generic Host.
3. **Preserve capabilities, not defects.** Startup cancellation, forwarding, splash timing, and UI safety are
   targets; unobserved tasks, blocking startup, accidental collisions, or skipped failure cleanup are not promises.
   Any opt-in behavior difference must be explicit, not applied to existing VB binaries.
4. **Do not equate similarly named events/tokens.** Reject mapping `Startup` directly to `ApplicationStarted`,
   `Shutdown` directly to every `ApplicationStopped`, or VB `Cancel` directly to a stop token.
5. **Explicit security and focus policy.** Reject machine-global insecure synchronization, weakened pipe access,
   automatic cross-user/elevation forwarding, or guaranteed foreground focus. Session/path/version decisions
   and protocol failure semantics remain review gates.
6. **Keep storage and diagnostics opt-in.** Reject settings conversion, logging redirection, new configuration
   precedence, automatic exception-detail collection, or duplicate network-transfer infrastructure.
7. **Separate language projection from runtime capability.** Retain `My`/`Handles`/default instances; do not try
   to create a C# equivalent language feature or change project generators in this issue.

Trade-off: exact legacy implementation reuse would reduce code changes but would also carry lifecycle/security
limitations into the shared model. A shared implementation plus narrowly scoped, explicitly reviewed migration
adapters is the direction to evaluate in #14974. It is not an implemented or approved adapter design.

## Test evidence and remaining validation

Read-only inspection found the following useful evidence; **no tests were executed for this documentation change**:

| Existing test evidence | What it establishes in source | What it does not establish |
|---|---|---|
| [ApplicationBase tests][app-tests], [ConsoleApplicationBase tests][console-tests], [AssemblyInfo tests][info-tests] | Culture/environment/log/info wrappers and executable-excluded arguments have assertions. | Complete cross-language lifecycle or deployment compatibility. |
| [VB application-base tests][vb-tests] | Property defaults/validation, no-startup-form exception, defaults propagation, positive splash show/hide. Process-wide changes use isolated execution where provided. | Cancellation/failure splash cleanup, timer bounds, shutdown order, or all exception modes. |
| [Identity tests][identity-tests] | GUID+major/minor and MVID fallback branches. | Actual installed/path/session/user/elevation identity boundaries. |
| [C# pipe tests][pipe-tests], [VB helper tests][vb-pipe-tests] | Sequential/overlapping clients, argument transport, malformed payloads, and connection timing scenarios are represented. | Exactly-once UI handling, acknowledgement, malicious payload limits, real multi-user sessions, or race freedom. Some tests use sleeps/timeouts. |
| [VB integration tests][vb-integration], [test executable][vb-integration-program] | Process-launch/run smoke paths, including a named single-instance path. The parent tests assert process exit. | A deterministic end-to-end handoff assertion or full lifecycle parity; process exit alone does not demonstrate correct activation. |
| [Network tests][network-tests] | Availability wrapper and ping cases. | UI-thread change notifications, subscription cleanup under canceled startup, or offline/OS callback failures. |
| [Shared runtime tests][runner-tests], [lifetime tests][lifetime-tests], [hosting tests][host-tests] | Existing source assertions cover lifecycle ordering, cancellation, close veto versus committed stop, failures, context ownership, and hosted-worker shutdown. | VB parity adapters, old VB generated binaries, splash/single-instance services, or hardware/RDP validation. |

Required downstream evidence (R), not executed work:

- #14978: paired VB/C# event traces for defaults/startup/normal shutdown/canceled startup, close veto, exceptions,
  host/service failures, settings-on-exit, and UI affinity; include legacy source and previously built binary
  consumers. Exercise custom overrides/context/no-main-form, Debug/Release and debugger modes, original/forwarded
  empty/quoted/Unicode/large arguments, and subscription disposal.
- #14978: deterministic IPC and splash fixtures with readiness/acceptance/completion signals and controlled time;
  test early activation, malformed/incomplete payloads, no dispatcher, failed startup, canceled startup,
  splash early close, and failed disposal. Avoid sleeps/exact elapsed-time assertions as correctness gates.
- #14980: real distinct users, same user in distinct Windows sessions, Terminal Services/RDP, reconnect,
  elevation pairs, copied paths/build versions, rapid restart/crash, x64, Windows ARM64, and slow hardware such
  as Surface Go 2. CI process tests alone cannot certify these environments.
- #14981: reproducible legacy/shared startup and shutdown, minimum splash delay, activation/IPC latency,
  allocations, working set/retained memory, handle/thread counts, repeated launch/exit, and network subscriptions.
  No baseline numbers or performance conclusions are claimed here.

### Child ownership and dependencies

| Child issue | Input / responsibility after this inventory |
|---|---|
| [#14974](https://github.com/dotnet/winforms/issues/14974) | Review this matrix with VB/WinForms owners; define shared contracts, language consumption, opt-in adapters, ordering/cancellation/error semantics, and unresolved identity policies. Depends on #14973. |
| [#14975](https://github.com/dotnet/winforms/issues/14975) | Approved startup/shutdown/exception/command-line and settings-on-exit policy using Stream A lifetime and Streams B/C services. Depends on #14974. |
| [#14976](https://github.com/dotnet/winforms/issues/14976) | Secure identity, IPC, next-instance handoff, focus, boundary and race behavior; investigate cited single-instance reports. Depends on #14975. |
| [#14977](https://github.com/dotnet/winforms/issues/14977) | Approved splash/information/network capabilities, including failure/cancellation cleanup; reuse services, not transfer-stack rewrites. Depends on #14976. |
| [#14978](https://github.com/dotnet/winforms/issues/14978) | Cross-language and existing-framework compatibility suite, local and CI, including settings and multi-process behavior. Depends on #14977. |
| [#14979](https://github.com/dotnet/winforms/issues/14979) | Paired VB/C# examples and incremental `ApplicationEvents.vb` migration; retain existing projects/settings and document deliberate differences. Coordinate Stream D templates. Depends on #14978. |
| [#14980](https://github.com/dotnet/winforms/issues/14980) | Real multi-user/session/architecture/slow-device validation and recorded limitations, including path/elevation/race matrix. Depends on #14979. |
| [#14981](https://github.com/dotnet/winforms/issues/14981) | Reproducible baseline-versus-shared performance/resource-stability report. Depends on #14980. |

This local deliverable is ready to inform **#14974 review only**; none of those child implementations starts here.
Stakeholder acceptance, public API/package approval, executable compatibility, generated tooling validation,
remote CI, environmental testing, and benchmarks remain unverified. Documentation/link/whitespace checks do not
substitute for them.

## Source and documentation references

Local links name canonical files and relevant member ranges at the research baseline.

[app-base]: ../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/ApplicationServices/ApplicationBase.vb
[console]: ../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/ApplicationServices/ConsoleApplicationBase.vb
[vb-constructor]: ../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/ApplicationServices/WindowsFormsApplicationBase.vb#L136-L176
[vb-properties]: ../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/ApplicationServices/WindowsFormsApplicationBase.vb#L266-L379
[vb-exception-event]: ../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/ApplicationServices/WindowsFormsApplicationBase.vb#L404-L452
[vb-network]: ../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/ApplicationServices/WindowsFormsApplicationBase.vb#L457-L523
[vb-display]: ../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/ApplicationServices/WindowsFormsApplicationBase.vb#L529-L540
[vb-model]: ../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/ApplicationServices/WindowsFormsApplicationBase.vb#L545-L597
[vb-splash-wait]: ../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/ApplicationServices/WindowsFormsApplicationBase.vb#L608-L651
[vb-activation]: ../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/ApplicationServices/WindowsFormsApplicationBase.vb#L660-L691
[vb-hide]: ../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/ApplicationServices/WindowsFormsApplicationBase.vb#L712-L729
[vb-init]: ../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/ApplicationServices/WindowsFormsApplicationBase.vb#L765-L841
[vb-onrun]: ../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/ApplicationServices/WindowsFormsApplicationBase.vb#L849-L895
[vb-startup]: ../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/ApplicationServices/WindowsFormsApplicationBase.vb#L902-L948
[vb-exception]: ../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/ApplicationServices/WindowsFormsApplicationBase.vb#L959-L973
[vb-splash]: ../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/ApplicationServices/WindowsFormsApplicationBase.vb#L982-L1014
[vb-identity]: ../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/ApplicationServices/WindowsFormsApplicationBase.vb#L1026-L1042
[vb-run]: ../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/ApplicationServices/WindowsFormsApplicationBase.vb#L1076-L1124
[vb-context]: ../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/ApplicationServices/WindowsFormsApplicationBase.WinFormsAppContext.vb
[shutdown-mode]: ../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/ApplicationServices/WindowsFormsApplicationBase.ShutdownMode.vb
[defaults]: ../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/ApplicationServices/ApplyApplicationDefaultsEventArgs.vb
[startup-args]: ../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/ApplicationServices/StartupEventArgs.vb
[next-instance-args]: ../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/ApplicationServices/StartupNextInstanceEventArgs.vb
[exception-args]: ../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/ApplicationServices/UnhandledExceptionEventArgs.vb
[pipes]: ../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/ApplicationServices/SingleInstanceHelpers.vb
[assembly-info]: ../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/ApplicationServices/AssemblyInfo.vb
[user]: ../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/ApplicationServices/User.vb
[network]: ../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/Devices/Network.Availability.vb
[network-download]: ../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/Devices/Network.DownloadFile.vb
[network-upload]: ../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/Devices/Network.UploadFile.vb
[network-ping]: ../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/Devices/Network.Ping.vb
[computer-info]: ../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/Devices/ComputerInfo.vb
[computer-memory]: ../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/Devices/ComputerInfo.InternalMemoryStatus.vb
[template-events]: ../../pkg/Microsoft.Dotnet.WinForms.ProjectTemplates/content/WinFormsApplication-VisualBasic/ApplicationEvents.vb
[template-designer]: ../../pkg/Microsoft.Dotnet.WinForms.ProjectTemplates/content/WinFormsApplication-VisualBasic/My%20Project/Application.Designer.vb
[template-myapp]: ../../pkg/Microsoft.Dotnet.WinForms.ProjectTemplates/content/WinFormsApplication-VisualBasic/My%20Project/Application.myapp
[template-project]: ../../pkg/Microsoft.Dotnet.WinForms.ProjectTemplates/content/WinFormsApplication-VisualBasic/Company.WinFormsApplication1.vbproj
[integration-events]: ../../src/test/integration/VisualBasicTemplate/ApplicationEvents.vb
[settings-generator]: https://github.com/dotnet/project-system/blob/1379b2dce76234664ad1b970e0bd72dc95c75e68/src/Microsoft.VisualStudio.Editors/SettingsDesigner/SettingsSingleFileGeneratorBase.vb#L353-L435
[settings-autosave]: https://github.com/dotnet/project-system/blob/1379b2dce76234664ad1b970e0bd72dc95c75e68/src/Microsoft.VisualStudio.Editors/SettingsDesigner/SettingsSingleFileGeneratorBase.vb#L650-L672
[application-exit]: ../../src/System.Windows.Forms/System/Windows/Forms/Application.cs#L1146-L1233
[thread-exceptions]: ../../src/System.Windows.Forms/System/Windows/Forms/Application.ThreadContext.cs#L607-L668
[builder]: ../../src/System.Windows.Forms/Microsoft/Extensions/WinForms/WinFormsApplicationBuilder.cs
[runner]: ../../src/System.Windows.Forms/Microsoft/Extensions/WinForms/WinFormsApplication.cs
[lifetime]: ../../src/System.Windows.Forms/Microsoft/Extensions/WinForms/WinFormsApplicationLifetime.cs
[host]: ../../src/System.Windows.Forms/Microsoft/Extensions/WinForms/WinFormsApplicationHost.cs
[barrier]: ../../src/System.Windows.Forms/Microsoft/Extensions/WinForms/WinFormsHostStopBarrier.cs
[scopes]: ../../src/System.Windows.Forms/Microsoft/Extensions/WinForms/WinFormsUiScope.cs
[service-activation]: ../../src/System.Windows.Forms/Microsoft/Extensions/WinForms/WinFormsServiceActivation.cs
[configuration]: ../../src/Microsoft.Extensions.WinForms.Hosting/src/WinFormsHostBuilderExtensions.cs
[settings-service]: ../../src/Microsoft.Extensions.WinForms.Hosting/src/IUserSettingsService.cs
[exception-logging]: ../../src/Microsoft.Extensions.WinForms.Hosting/src/WinFormsExceptionLogging.cs
[hosting-package]: ../../src/Microsoft.Extensions.WinForms.Hosting/src/PACKAGE.md
[app-tests]: ../../src/Microsoft.VisualBasic/tests/UnitTests/Microsoft/VisualBasic/ApplicationServices/ApplicationBaseTests.cs
[console-tests]: ../../src/Microsoft.VisualBasic/tests/UnitTests/Microsoft/VisualBasic/ApplicationServices/ConsoleApplicationBaseTests.cs
[info-tests]: ../../src/Microsoft.VisualBasic/tests/UnitTests/Microsoft/VisualBasic/ApplicationServices/AssemblyInfoTests.cs
[identity-tests]: ../../src/Microsoft.VisualBasic/tests/UnitTests/Microsoft/VisualBasic/ApplicationServices/WindowsFormsApplicationBaseTests.cs
[vb-tests]: ../../src/Microsoft.VisualBasic.Forms/tests/UnitTests/System/Windows/Forms/WindowsFormsApplicationBaseTests.vb
[pipe-tests]: ../../src/Microsoft.VisualBasic/tests/UnitTests/Microsoft/VisualBasic/ApplicationServices/SingleInstanceTests.cs
[vb-pipe-tests]: ../../src/Microsoft.VisualBasic.Forms/tests/UnitTests/System/Windows/Forms/SingleInstanceHelpersTests.vb
[vb-integration]: ../../src/Microsoft.VisualBasic/tests/IntegrationTests/Microsoft.VisualBasic.IntegrationTests/Microsoft/VisualBasic/ApplicationServices/WindowsFormsApplicationBaseTests.cs
[vb-integration-program]: ../../src/Microsoft.VisualBasic/tests/IntegrationTests/VisualBasicRuntimeTest/Program.cs
[network-tests]: ../../src/Microsoft.VisualBasic/tests/UnitTests/Microsoft/VisualBasic/Devices/NetworkTests.cs
[runner-tests]: ../../src/test/unit/System.Windows.Forms/Microsoft/Extensions/WinForms/WinFormsApplicationRuntimeTests.cs
[lifetime-tests]: ../../src/test/unit/System.Windows.Forms/Microsoft/Extensions/WinForms/WinFormsApplicationLifetimeTests.cs
[host-tests]: ../../src/test/unit/System.Windows.Forms/Microsoft/Extensions/WinForms/WinFormsApplicationHostingTests.cs
[doc-model]: https://learn.microsoft.com/en-us/dotnet/visual-basic/developing-apps/development-with-my/overview-of-the-visual-basic-application-model
[doc-exception]: https://learn.microsoft.com/en-us/dotnet/api/microsoft.visualbasic.applicationservices.windowsformsapplicationbase.unhandledexception?view=windowsdesktop-10.0
[doc-network]: https://learn.microsoft.com/en-us/dotnet/api/microsoft.visualbasic.applicationservices.windowsformsapplicationbase.networkavailabilitychanged?view=windowsdesktop-10.0
[doc-settings]: https://learn.microsoft.com/en-us/dotnet/api/microsoft.visualbasic.applicationservices.windowsformsapplicationbase.savemysettingsonexit?view=windowsdesktop-10.0
[doc-splash]: https://learn.microsoft.com/en-us/dotnet/api/microsoft.visualbasic.applicationservices.windowsformsapplicationbase.minimumsplashscreendisplaytime?view=windowsdesktop-10.0
[doc-pipes]: https://learn.microsoft.com/en-us/dotnet/api/system.io.pipes.pipeoptions?view=net-10.0
