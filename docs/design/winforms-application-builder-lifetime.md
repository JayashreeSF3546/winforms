# WinForms Application Builder: host and lifetime architecture

Status: architecture recommendation for
[dotnet/winforms#14941](https://github.com/dotnet/winforms/issues/14941), pending API and architecture approval.
The contract prototype, core runtime, and public Generic Host adapter below are implemented for
review; upstream API approval remains pending.

Parent: [dotnet/winforms#14082](https://github.com/dotnet/winforms/issues/14082).
Related design: [dotnet/winforms#11415](https://github.com/dotnet/winforms/issues/11415).

## Scope and recommendation

Keep the existing WinForms message pump and `ApplicationContext` as the authority for UI lifetime.
The application entry point supplies the STA thread; a new application runner owns one invocation of
`Application.Run(ApplicationContext)` on that thread. A separate, non-UI coordinator handles asynchronous
Generic Host operations when hosting integration is present. Neither layer synchronously calls the other
from a shutdown callback.

The foundational builder must work without dependency injection, configuration, logging, or hosted services.
Generic Host integration composes this runner with the real host rather than reimplementing Generic Host.
The core builder is therefore not initially an `IHostApplicationBuilder`, and the core application is not
initially an `IHost`: those interfaces require services and other facilities outside this stream.
This intentionally narrows the parent proposal; it must not be presented as complete Generic Host support.
The architecture sections describe the original recommendation. The runtime and public adapter
sections at the end document the implementation, including the subsequently requested hosting integration.

The [service infrastructure boundaries](winforms-application-builder-services.md) for
dotnet/winforms#14956 build on that adapter without replacing its startup or UI-completion ordering.

Do not change behavior for applications that call the existing `Application.Run`, `Application.Exit`,
or `Application.ExitThread` APIs without the builder.

## Findings from existing infrastructure

The local source references below describe this branch's behavior, not hypothetical new behavior.

| Area | Evidence and consequence |
|---|---|
| Current-thread execution | `Application.Run(Form)` creates an `ApplicationContext`; both overloads enter the current thread's `ThreadContext.RunMessageLoop`. There is no implicit UI thread creation. See [Application.cs](../../src/System.Windows.Forms/System/Windows/Forms/Application.cs), `Run` overloads. |
| Context authority | `ApplicationContext.MainForm` observes `HandleDestroyed`, excluding handle recreation. The default `OnMainFormClosed` calls `ExitThreadCore`; custom contexts may choose a different exit policy. See [ApplicationContext.cs](../../src/System.Windows.Forms/System/Windows/Forms/ApplicationContext.cs). |
| Exit requests | `Application.Exit` raises form-closing notifications and can return without exiting if a form cancels. It affects all application thread contexts. `Application.ExitThread` targets the calling thread, through its application context when present. See `Application.cs`, `Exit` and `ExitThread`. |
| Loop nesting | `RunMessageLoopInner` rejects a reentrant main loop, not a later, sequential `Application.Run`. Modal and `DoEvents` loops are separate loop reasons. See [Application.ThreadContext.cs](../../src/System.Windows.Forms/System/Windows/Forms/Application.ThreadContext.cs), `RunMessageLoopInner`. |
| Startup failure boundary | The main-loop count is incremented, the context is attached, and the main form is made visible before the existing pump's `try/finally`. A failure in visibility/startup cannot be assumed to receive the same cleanup as a failure inside message dispatch. |
| Quit and modal unwind | Both [Application.ComponentManager.cs](../../src/System.Windows.Forms/System/Windows/Forms/Application.ComponentManager.cs), `FPushMessageLoop`, and [Application.LightThreadContext.cs](../../src/System.Windows.Forms/System/Windows/Forms/Application.LightThreadContext.cs), `FPushMessageLoop`, consume `WM_QUIT`, dispose thread windows, and repost quit for a non-main loop. An open modal dialog may already be disposed before the outer `Run` returns. |
| Modal context | [Application.ModalApplicationContext.cs](../../src/System.Windows.Forms/System/Windows/Forms/Application.ModalApplicationContext.cs) overrides `ExitThreadCore` with a no-op. Do not treat the currently active modal context as the application's root context. |
| Cleanup | `DisposeThreadWindows` disposes the application context and thread windows, but currently catches disposal exceptions. A wrapper cannot promise to recover exceptions already swallowed by this existing path. Thread-context disposal also raises thread/process exit notifications and uninitializes owned OLE state. |
| Process-wide events | `Application.ApplicationExit` represents the last application message loop, not necessarily this runner. It is not a suitable sole completion signal for a particular application instance. |

### Synchronization context and configuration

[WindowsFormsSynchronizationContext.cs](../../src/System.Windows.Forms/System/Windows/Forms/WindowsFormsSynchronizationContext.cs)
installs automatically during control creation or loop entry, subject to `AutoInstall`.
`InstallIfNeeded` preserves a non-default synchronization context; it does not forcibly replace it.
Its constructor creates a thread-owned marshaling control and native handle. `Post` queues through
`BeginInvoke`; it does not establish that a message pump is already dispatching. `Send` can block.
The last message loop calls `Uninstall(turnOffAutoInstall: false)` to restore the previous context.
The public parameterless `Uninstall` instead disables future automatic installation.

Consequently, configuration must precede synchronization-context installation and any startup instance:

1. The entry point calls generated `ApplicationConfiguration.Initialize()` or equivalent explicit settings.
2. The builder records configuration and startup selection without creating controls or handles.
3. `Build` validates and snapshots that selection, without activating a form or context.
4. The runner establishes the UI synchronization context before invoking a startup factory.

`SetCompatibleTextRenderingDefault` and `SetDefaultFont` reject changes after a native window has been
created; `SetHighDpiMode` checks whether a parking window has been created and can return `false`.
Do not defer these settings until after host construction that might activate UI dependencies.
Do not call generated `ApplicationConfiguration.Initialize` from framework code: it belongs to the application.
If the builder offers explicit configuration, apply it before accepting a pre-created startup instance,
and report settings that cannot be applied rather than claiming success.

## Generic Host contracts and reconciliation

The research baseline is the tagged .NET 10 implementation; recheck it against the actual hosting package
selected for integration. Source links are pinned so these conclusions remain auditable.

| Contract | Relevant behavior |
|---|---|
| [IHost](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/Microsoft.Extensions.Hosting.Abstractions/src/IHost.cs) | Exposes `Services`, asynchronous start/stop, and disposal. A blocking WinForms `Run` alone is not an implementation of this contract. |
| [IHostBuilder](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/Microsoft.Extensions.Hosting.Abstractions/src/IHostBuilder.cs) and [HostApplicationBuilder](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/Microsoft.Extensions.Hosting/src/HostApplicationBuilder.cs) | Compose services and configuration. `Build` is single-use, constructs the provider, and resolves a host; it does not start the host or a UI pump. |
| [IHostLifetime](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/Microsoft.Extensions.Hosting.Abstractions/src/IHostLifetime.cs) | `WaitForStartAsync` gates host startup. It must not await a milestone that itself requires host startup to finish. |
| [IHostApplicationLifetime](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/Microsoft.Extensions.Hosting.Abstractions/src/IHostApplicationLifetime.cs) | Provides notification tokens, not UI-thread events. `StopApplication` requests stopping; it does not itself run or await `IHost.StopAsync`. It is not intended to be replaced. |
| [Host](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/Microsoft.Extensions.Hosting/src/Internal/Host.cs) | Startup awaits the lifetime gate, validators, and service startup before notifying started. Shutdown invokes lifecycle stopping callbacks, notifies stopping, stops services, notifies stopped, then calls `IHostLifetime.StopAsync`. |
| [ApplicationLifetime](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/Microsoft.Extensions.Hosting/src/Internal/ApplicationLifetime.cs) | Token callbacks execute synchronously during cancellation; callback exceptions are logged, not propagated as normal host startup/shutdown exceptions. A UI event and a host token therefore do not have identical exception semantics. |
| [BackgroundService](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/Microsoft.Extensions.Hosting.Abstractions/src/BackgroundService.cs) | In .NET 10, `ExecuteAsync` runs through `Task.Run`. Stop cancels its execution token and waits subject to the stop token. The host observes background failure and can request application stop. None of these operations automatically marshal to the UI thread. |
| [HostingAbstractionsHostExtensions](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/Microsoft.Extensions.Hosting.Abstractions/src/HostingAbstractionsHostExtensions.cs) | `RunAsync` starts, waits for shutdown, stops, then disposes. `WaitForShutdownAsync` responds to stopping and calls `StopAsync` without reusing the token that requested shutdown. Disposal alone is not graceful stop. |

Two conclusions constrain the adapter:

- Do not replace the real `IHostApplicationLifetime`. The .NET 10 host requires its own
  `ApplicationLifetime` implementation. Observe its tokens and call `StopApplication` instead.
- `IHostLifetime.StopAsync` is too late to make the host's stopped token mean the UI loop has exited.
  Integration needs an asynchronous stop participant that waits for UI completion during hosted-service
  shutdown, before the host notifies stopped. Registering and ordering that participant belongs to the
  hosting/services workstream, not to the minimal builder contracts.

The design in dotnet/winforms#11415 correctly identifies the need for explicit STA ownership, UI marshaling,
and a WinForms-to-host stop signal. Its dedicated-thread hosted-service approach is useful for a future
host-first adapter, but is not the core runner model selected here. Do not copy its suggested replacement
of `IHostApplicationLifetime`, or its DI/form-provider APIs, into this foundation.

## Ownership decisions

### UI thread

The entry point creates and owns the thread, normally through `[STAThread] Main`.
`Run` validates STA and rejects an already-active main message loop before activating startup UI.
It does not change an initialized thread's apartment, move a supplied form to another thread, or use
`Task.Run(Application.Run)`. The runner owns UI resources, not the lifetime of the caller's OS thread.
Returning from `Run` leaves that thread available to the caller.

One runner may be active per UI thread. A built application runs once; attempting to rerun that object
fails explicitly. This restriction does not prohibit sequential legacy `Application.Run` calls or fresh
builder instances on the same thread. Additional UI threads remain under application control.

An eventual host-first API that creates a dedicated STA must be an explicit, separately reviewed adapter.
It must use deferred startup creation on that STA and must not accept forms created on another thread.
It must not silently change the threading contract of this runner.

### Message loop and startup selection

The runner calls the existing root `Application.Run(ApplicationContext)` exactly once.
Form selection uses an ordinary `ApplicationContext(form)`; context selection preserves the supplied
context's override behavior and main-form switching. No replacement pump, `DoEvents` bootstrap, idle
polling, or interception of arbitrary window messages is needed.

Startup selection is exclusive: exactly one form factory, form instance, or application-context factory.
Reject missing or conflicting selections during `Build`; reject null, disposed, already-running, or
wrong-thread startup resources before taking ownership. Factories run only on the owning STA.
For a pre-created form, creation-time configuration and context guarantees are caller obligations;
the builder cannot retroactively initialize its constructor. Handleless controls are not evidence of
cross-thread safety, so the instance overload requires same-thread creation even before a handle exists.

Ownership transfers to the application at successful `Build` for supplied instances, and upon successful
factory return for deferred instances. A failed `Build` leaves supplied resources with the caller.
Disposing an unrun application disposes transferred resources on the owning thread. Normal loop cleanup
uses the existing context's disposal; do not independently dispose the same context again. Startup
failure before loop ownership is established must dispose successfully returned factory objects.
A constructor that throws remains responsible for resources it allocated before failing.

### Synchronization-context contract

Use the existing internal `InstallIfNeeded` before startup activation. Preserve an existing
`WindowsFormsSynchronizationContext` on its owning thread. For the new runner, reject `AutoInstall = false`
or a different custom current context rather than silently overriding it or promising a WinForms context
that was never installed. This validation is opt-in and does not change legacy behavior.

On failure before entering a loop, undo only installation performed by this runner, using the internal
`Uninstall(turnOffAutoInstall: false)` path and existing thread-owned resource cleanup.
After loop entry, coordinate with the existing final-loop restoration instead of uninstalling twice.
Do not dispose an externally installed synchronization context or globally disable auto-installation.

## Startup and lifecycle notifications

The proposed normal sequence is:

```text
Application configuration
CreateBuilder -> select startup -> Build
Host initialization/start, if an adapter is present
Validate owning STA -> install WinForms synchronization context
Create startup form/context
WinForms ApplicationStarted
Application.Run(root context) -> message dispatch
WinForms ApplicationStopping
Root message loop exits -> UI cleanup completion
Host stop completion, if an adapter is present
WinForms ApplicationStopped
Dispose runner-owned registrations and remaining non-UI resources
```

`ApplicationStarted` means startup objects exist and the runner is about to enter `Application.Run`;
it does not mean the form has been shown or posted work has executed. Handlers may post work but must not
wait synchronously for it. Factories and lifecycle handlers must not run a nested main loop.
`Form.Load`/`Shown` failures can occur after this event; this event is not a promise of successful rendering.
If stopping is requested by an early handler, do not report a running application or enter an unbounded
pump after cleanup has already started.

The three WinForms notifications are synchronous events on the owning STA. Mark each transition before
invoking subscribers, so reentrant shutdown cannot raise it twice. Invoke all subscribers while recording
their failures; a throwing subscriber must not suppress mandatory cleanup or later terminal notification.
They are not asynchronous event hooks; `async void` failures retain their normal WinForms semantics.

`ApplicationStarted` is omitted on failure/cancellation before that milestone.
Once `Run` has accepted ownership, teardown raises stopping and stopped once, including startup failure.
An unrun application's disposal does not simulate a started/stopped run.
Stopped means teardown has terminated, possibly with errors, not that shutdown succeeded.

Strict stopping-before-loop-exit ordering requires an opt-in internal observation point for root-context
termination and teardown, including direct quit and exceptional unwind. Merely subscribing to the public
`Application.ThreadExit` event, or observing return from `Run`, is too late for every path. The runtime
issue must add the smallest builder-scoped hook in the existing lifetime machinery, cover visibility
failure before the current pump's `try/finally`, and leave unregistered legacy callers unchanged.
Do not promise strict ordering until these paths are demonstrated.

### Hosting startup boundary

The non-UI coordinator owns the real host's start/stop tasks. Host initialization completes before the
startup factory runs. In this model, hosted startup must not wait for UI dispatch or startup-form access:
no UI pump is available yet. UI-dependent work must be deferred until WinForms startup and dispatch.
Host-started and WinForms-started are deliberately different milestones; do not alias their notifications.

The STA may wait for this pre-pump initialization only when the coordinator and its startup services have
no UI dependency. Do not capture the STA context in host operations. Integration requiring UI-dependent
host startup needs a different, reviewed startup boundary, not a hidden nested pump.

## Shutdown protocol

Keep separate internal signals for a stop request, UI cleanup completion, and total run completion.
Use one idempotent transition and one shared completion per run; concurrent requests join it.
Set completion even on failure, with the captured failure available to the coordinator.
Do not hold a lifetime lock while invoking user callbacks or making cross-thread calls.

### Host to WinForms

1. A direct host `StopAsync`, `StopApplication`, or background-service failure initiates host stopping.
2. Its stopping-token callback records a request and posts to the UI dispatcher; it does not call `Send`,
   wait for the UI, or recursively call host `StopAsync`.
3. On the STA, raise WinForms stopping and call `Application.Exit(CancelEventArgs)`.
4. Exit the root loop and perform UI cleanup. The adapter's stop participant asynchronously awaits this
   UI completion before allowing the host to notify stopped.
5. After host stop completion, raise WinForms stopped on the owning STA and release remaining resources.

`Application.Exit` is process-wide. The hosted runner opts into application-wide shutdown, not a private
window-close operation. A host-first integration must document this and must not claim to await unrelated,
application-owned UI threads unless it explicitly tracks them.

Host stopping cannot be undone by a form's `FormClosing` cancellation: the host's stopping token is
irreversible. For this opt-in path, honor closing notifications first, but if canceled, terminate the owned
root context through the existing thread-context shutdown machinery. Do not rely solely on a potentially
overridden `ApplicationContext.ExitThreadCore` to guarantee termination. This fallback may dispose windows
without another cancelable close; it is an intentional hosted-shutdown policy, not a change to legacy exit.
Applications that need a veto must finish their close confirmation before requesting host stop.

A request received before dispatcher creation is retained and checked at each startup boundary.
A request racing dispatcher teardown joins UI completion instead of posting to a destroyed handle.
If a post fails unexpectedly while the run is active, report the failure; do not return a successful stop.

### WinForms to host

A successful main-form close, accepted `Application.Exit`, or root-context exit enters WinForms stopping.
A canceled user close/exit does not stop the host. A custom context that keeps running after a form closes
must keep that policy; do not use `FormClosed` alone as the stop signal.

The builder-scoped root-lifetime observer signals the non-UI coordinator to call `StopApplication`.
The coordinator, not the UI callback, owns the single host `StopAsync` operation.
It also watches UI completion as a fallback for unexpected loop termination.
UI completion is signaled before waiting for host completion, so the host's stop participant cannot
deadlock with the runner. After the pump has exited, host shutdown must not depend on UI dispatch;
services needing live UI must release that dependency before requesting application termination.

When using the Generic Host `RunAsync`/`WaitForShutdownAsync` driver, that driver owns the stop operation.
Do not additionally run a competing coordinator stop call. An adapter must choose one driver explicitly
and serialize its own stop entry points; it must not assume arbitrary concurrent raw host `StopAsync`
calls are made idempotent by the Generic Host.

## Exceptions and cancellation

| Condition | Required result |
|---|---|
| Invalid build options or thread/context preconditions | Throw a specific validation exception before activation; no fabricated lifetime success. |
| Host initialization failure | Do not activate startup UI. Observe the original failure, stop any partially started host where appropriate, and release owned resources. |
| Form/context constructor or factory failure | Do not raise started. Run stopping/cleanup/stopped for an accepted run; preserve the original exception and any additional cleanup failures. |
| Form visibility/initialization failure | Handle the pre-pump failure boundary explicitly; do not leave loop accounting, context subscriptions, handles, or synchronization-context state attached. |
| UI dispatch exception | Preserve `Application.ThreadException` and `UnhandledExceptionMode`. Do not install a global catch handler or change process policy. If the selected policy lets an exception escape the pump, terminate the run, coordinate host stop, and propagate it. A handled exception need not terminate the run. |
| Shutdown or lifecycle-subscriber failure | Continue independently safe cleanup and terminal notification, then propagate the failure; aggregate additional failures without losing the initial stack. Existing internally swallowed disposal failures cannot be recovered by the wrapper. |
| Startup cancellation | Before accepting a run, reject an already-canceled request without activating UI. After acceptance, check between stages and clean up before surfacing cancellation; a completed started milestone is not rolled back. |
| Cancellation after startup | Treat a run-lifetime cancellation request as a stop request, not permission to abandon the UI thread or return while it is still running. |
| Shutdown timeout/cancellation | Bound the caller's graceful wait, not the underlying ownership obligations. Retain the stop request and observable cleanup completion. Never publish stopped or dispose live UI resources merely because a wait was canceled. |
| Process termination or non-cooperative UI code | No in-process guarantee of cleanup, stop notification, or thread termination. Do not use `Thread.Abort`, `Environment.Exit`, or forced process termination as normal shutdown. |

Startup and shutdown cancellation are distinct from lifetime notification tokens.
Do not pass an already-canceled run token into the mandatory cleanup operation.
Cancellation callbacks only request work; they do not throw user lifecycle exceptions on an arbitrary
canceling thread. If cancellation and a real failure race, preserve the failure rather than hiding it as
cancellation. A timeout does not turn incomplete cleanup into a successful run.

## Smallest contract for the prototype

These are responsibilities for API review in
[dotnet/winforms#14942](https://github.com/dotnet/winforms/issues/14942), not new approved signatures.

| Type | Minimum responsibility |
|---|---|
| `WinFormsApplicationBuilder` | A `CreateBuilder` entry point, exclusive deferred `UseStartupForm<T>()` with a parameterless-constructor constraint, `UseStartupForm(Form)`, deferred `UseApplicationContext` selection, and single-use `Build`. Freeze configuration after build. |
| `WinFormsApplicationOptions` | Internal snapshot of startup selection and necessary application-configuration choices. No public options-pattern abstraction or mutable post-build settings. |
| `WinFormsApplication` | Single-run `Run`, access to its lifetime, and idempotent `Dispose`. Capture ownership/thread metadata needed for validation. Running applications must finish coordinated shutdown before disposal can release live UI resources. |
| `WinFormsApplicationLifetime` | Started, stopping, and stopped events with the milestones above. No global mutable `Current`, service provider, or replacement host lifetime. |

Do not add `RunAsync` merely by wrapping `Run` in a task: that would change STA and instance affinity.
Cancellation signatures and the context-factory delegate shape require API review before public exposure.
Keep cross-thread stop requests, dispatcher readiness, and completion tasks internal unless a concrete
consumer demonstrates a need for a public contract.

## Alternatives and trade-offs

| Alternative | Decision |
|---|---|
| Always create a dedicated STA | Reject for the core: breaks caller-thread and pre-created-form expectations. Reserve an explicit host-first adapter for a separately reviewed use case. |
| Make the core builder inherit hosting interfaces immediately | Reject: imports DI/configuration/logging contracts before their owning streams. Preserve an internal composition boundary instead. |
| Replace the host's application lifetime | Reject: unsupported by the researched host implementation and creates two sources of lifecycle truth. |
| Stop the UI only in `IHostLifetime.StopAsync` | Reject: the host has already notified stopped by then. A pre-stopped completion barrier is required. |
| Block the UI in a host stopping callback | Reject: synchronous token callbacks and UI-dependent stop work can deadlock. Queue a request and await completion outside the callback. |
| Allow host shutdown to be vetoed by `FormClosing` | Reject after host stopping begins: the host cannot resume. Preserve vetoes for user-initiated close before that boundary. |
| Use global `ApplicationExit` as runner completion | Reject: it depends on other message loops and cannot distinguish startup failure from this runner's exit. |
| Replace existing exception handling or repair all swallowed disposal exceptions | Reject: changes compatibility outside the new runner. Document the existing limitation. |

## Follow-up ownership and acceptance evidence

| Issue/workstream | Required follow-up |
|---|---|
| dotnet/winforms#14942 | Review the minimal contracts, factory/instance ownership, configuration timing, and cancellation signatures. No runtime message-loop implementation. |
| dotnet/winforms#14943 | Implement STA validation, early context installation, root-loop ownership and builder-scoped teardown observation, startup-failure cleanup, and the internal stop/completion protocol. Demonstrate rather than assume that all loop backends preserve the required ordering. |
| dotnet/winforms#14944 | Deterministic CI coverage for lifecycle, cancellation, exceptions, modal unwind, repeated requests, and resource cleanup. Use barriers/tasks or posted callbacks, not sleeps or timing assertions. |
| dotnet/winforms#14945 | C# and VB form/context/graceful-shutdown samples. Show hosted extensibility only to the extent supported; do not simulate a real `IHost` or introduce hosted-service infrastructure here. |
| dotnet/winforms#14946 | Compare startup/shutdown, allocations, working set, threads, and handles with `Application.Run(Form)`, including at least 100 complete ownership cycles. |
| Hosting/services stream | Implement the actual Generic Host adapter, choose a single host driver, add the pre-stopped UI completion participant, and validate direct `StopAsync`, background failure, and both notification families against the selected hosting version. |
| Packaging/API review | Approve namespace/assembly placement before exposing contracts. A hosting dependency in a framework assembly needs explicit transport/WindowsDesktop integration, not just a NuGet reference. See [packageContent.targets](../../eng/packageContent.targets), `GetPackageContent`, and [transport package guidance](../../pkg/Microsoft.Private.Winforms/README.md). |

Required runtime evidence includes a throwing factory, throwing `Load`, custom-context exit policy,
canceled close without host stop, irrevocable host stop despite close cancellation, stop during startup,
stop with a modal dialog open, raw quit, and a failure before pump entry. Also verify custom synchronization
context rejection without mutation, `AutoInstall` preservation, an unrun application's disposal, repeated
fresh runs on one STA, and no effect on existing direct `Application.Run` callers.

The actual adapter must separately prove that its host stopped token never precedes owned UI completion
on successful shutdown. Cancellation/failure paths must report incomplete teardown honestly instead of
claiming the same success ordering. CI, runtime tests, samples, benchmarks, and API approval remain work
for their owning issues; this research document supplies no execution evidence for unimplemented behavior.

## Contract prototype (dotnet/winforms#14942)

The prototype places public `WinFormsApplication`, `WinFormsApplicationBuilder`, and
`WinFormsApplicationLifetime` types in `Microsoft.Extensions.WinForms` within `System.Windows.Forms`.
`WinFormsApplicationOptions` remains an internal immutable snapshot, not a public options-pattern API.
This placement was selected for the prototype; upstream API approval is still required before shipping.
No new package dependency or framework transport assembly is introduced.

```csharp
ApplicationConfiguration.Initialize();
var builder = WinFormsApplication.CreateBuilder();
builder.UseStartupForm<MainForm>();
using var application = builder.Build();
```

Alternatively, select a same-thread existing form with `UseStartupForm(form)` or defer context creation
with `UseApplicationContext(() => new MyApplicationContext())`. Startup selection is exclusive and is
validated when selected, not silently replaced by subsequent selections. Build validates again before
transferring ownership and freezes the builder only after success. No factory runs during build or
disposal of an unactivated application. Existing application configuration remains at the entry point;
the prototype does not add a competing configuration API or execute configuration after handles exist.

The internal startup activation seam constructs the selected context at most once without showing forms
or starting a pump. Factory exceptions are not reflection-wrapped, and a failed activation cannot be
retried on the same application. A factory must return a valid context whose UI belongs to the calling
thread. Reentrant disposal during factory execution is rejected so returned resources cannot escape ownership.
Invalid output rejected before activation is not adopted; its creator remains responsible for it.
Known form-handle thread ownership is checked without creating a handle. WinForms does not retain a
handleless control's creation thread, so same-thread construction remains a caller precondition; this
prototype does not change all controls to track additional metadata.

The lifetime service implements ordered, once-only transitions and defers reentrant transitions until
the current notification's subscribers finish. Subscriber failures do not skip other subscribers;
multiple failures are aggregated, and a single failure preserves its exception identity and stack.
The prototype does not connect these internal transitions to message-loop or host events.

At the contract-prototype boundary, `Run()` deliberately threw `NotSupportedException` without
activating startup UI or raising events. The runtime below replaces that placeholder.

## Core runtime (dotnet/winforms#14943)

`Run()` and `Run(CancellationToken)` now execute the selected application on the caller-owned STA.
`StopAsync(CancellationToken)` is a cross-thread, irreversible stop request with an independently
cancelable wait. It does not create a UI thread or move UI to a worker thread.

```csharp
ApplicationConfiguration.Initialize();
using var application = WinFormsApplication.CreateBuilder()
    .UseStartupForm<MainForm>()
    .Build();
application.Run();
```

The runner validates the owner thread, apartment, synchronization context, and absence of an active
loop before accepting execution. It installs the existing WinForms context before deferred startup
creation, enters the existing `Application.Run(ApplicationContext)`, and automatically releases owned
startup resources on exit. An already-canceled run is rejected before ownership of execution is accepted.
Cancellation after acceptance finishes cleanup before propagating `OperationCanceledException`.
A cancellation token passed to `StopAsync` cancels only that caller's wait.
Observe shutdown completion from a non-UI context: a continuation captured to the terminating UI
dispatcher cannot be relied on after the message loop exits.
An exception that escapes a native window callback can terminate the process under the selected
exception policy; a managed runner cannot guarantee cleanup after that process-termination boundary.
The component-manager backend also retains its existing COM callback exception handling: the runner
cannot recover the original message-filter failure after that boundary has swallowed it or converted
it into a diagnostic failure. Original escaping message-filter exception identity is covered on the
managed light-loop backend, not promised across the COM boundary.

Builder-scoped `ThreadContext` observation covers accepted application/context exit, raw quit, and
exceptional loop unwind. Only builder-owned main-form visibility is moved inside the pump's cleanup
boundary. Builder cleanup reports noncritical disposal failures and also disposes hidden or disabled
managed thread windows. Existing runners without a registered builder retain their legacy paths.
Normal canceled form closing does not begin stopping; committed runner shutdown first requests
`Application.Exit`, then posts root quit even if closing is vetoed or a custom context ignores exit.

The runner restores the exact entry synchronization context and does not dispose a caller-installed
WinForms context. A context installed by the runner is uninstalled without disabling `AutoInstall`
and is disposed after UI cleanup. Accepted execution always attempts stopping and stopped notifications,
and preserves independently collected startup, dispatch, lifecycle, host, and cleanup errors.
Synchronous disposal during activation or execution is rejected: request `StopAsync` without blocking
the UI, or let the root context terminate. Disposing an unrun application releases transferred startup
resources without simulating lifecycle events.

### Public Generic Host adapter

The hosting integration uses the real `IHost` and `IHostApplicationLifetime` contracts, not a
parallel WinForms-specific host interface. The configured core framework reference already supplies
`Microsoft.Extensions.Hosting.Abstractions` and its abstraction dependencies; no additional NuGet
reference or WindowsDesktop transport assemblies are needed for this target.

Select an unbuilt `IHostBuilder` with `UseHost(hostBuilder)`. Application `Build` consumes the host
builder, registers an internal hosted UI-completion barrier, and decorates its singleton `IHost`
factory. Both `application.Host` and `host.Services.GetRequiredService<IHost>()` expose the same
coordinated adapter; the original host stays private. The host builder must preserve these
registrations and the standard singleton host factory. Do not separately build the supplied builder,
start the exposed host before `Run`, or use `Host.RunAsync` as a second driver.

```csharp
ApplicationConfiguration.Initialize();
using var application = WinFormsApplication.CreateBuilder()
    .UseHost(Host.CreateDefaultBuilder(args))
    .UseStartupForm<MainForm>()
    .Build();
application.Run();
```

Only the selected `IHostBuilder` contract is supported here; a `HostApplicationBuilder` overload
and general DI/form-activation APIs are not introduced. Host construction occurs during application
build without starting hosted services or activating startup UI. Host startup finishes off the UI
thread before startup UI creation. The real host's application lifetime is never replaced.
Once host building begins, this builder is consumed even if host construction fails, because the
underlying Generic Host builder cannot be retried. A failed build leaves a supplied form caller-owned.

Direct `application.Host.StopAsync`, host service requests through DI-resolved `IHost`, runner
`StopAsync`, and `IHostApplicationLifetime.StopApplication` all converge on the same host stop task.
The adapter requests UI shutdown and signals the real host lifetime off the UI thread, waits for UI
cleanup, then stops the underlying host. The registered hosted participant additionally prevents
host stopped notification before UI completion. It awaits **UI completion**, never total run
completion, to avoid a host/runner wait cycle. WinForms stopped follows host shutdown.
Concurrent callers share the stop operation rather than executing hosted-service stop twice.
No synchronous host callback waits for UI dispatch.

A token passed to the exposed host's `StopAsync` cancels only that caller's wait. It does not cancel
the shared shutdown operation. The hosted barrier deliberately ignores host shutdown cancellation:
Generic Host continues toward its stopped notification after hosted-service cancellation or failure.
Releasing the barrier on timeout would therefore break the ordering guarantee. A non-cooperative UI
can delay shutdown indefinitely; neither timeout nor cancellation is permission to report completed
UI cleanup. Host services may still observe the host's own shutdown timeout after UI cleanup.

The application owns the host on successful build. It disposes the provider once after coordinated
shutdown. Disposing an unrun application signals UI completion before stopping and disposing its
host; it does not fabricate WinForms lifetime events. Host disposal follows the application's
owner-thread and inactive-runtime rules. This contract does not make arbitrary provider disposal,
process termination, a custom host that ignores its services, or a separately retained private host
instance safe.

Actual Generic Host regression tests use the `Microsoft.Extensions.Hosting` test-only package.
The product still depends only on framework-provided hosting/DI abstractions, so that test dependency
adds no WindowsDesktop transport requirement.

### Lifecycle regression coverage (dotnet/winforms#14944)

The existing unit project covers startup selection, failed factories and `Load`, custom-context exit
policy, startup and dispatch cancellation, lifecycle-subscriber failures, root quit, modal unwind,
cross-thread stop, and synchronization-context ownership. Focused additions exercise:

| Suite | Additional regression boundary |
|---|---|
| Runtime | Fresh applications alternating with legacy loops on one STA; hidden and disabled managed windows are disposed and their native handles destroyed; exact entry-context restoration. |
| Runtime | Startup-subscriber failure racing cancellation plus context-disposal failure preserves both original exceptions, terminal notifications, and the shared stop failure. |
| Runtime | A handled posted exception still reaches `Application.ThreadException` under the caller's selected exception policy. |
| Real Generic Host | Accepted form close initiates host shutdown, while a vetoed close does not; mixed concurrent runner/host stop requests execute service shutdown once. |
| Real Generic Host | A background-service failure using `StopHost` requests UI shutdown, with host stopped notification only after UI cleanup. The service task retains its original failure; Generic Host's existing background-exception policy is not replaced. |
| UI integration | Public APIs run deferred contexts and existing forms; `await Task.Yield()` dispatches back to the owning STA; all lifecycle events retain thread affinity. Replacing a context's main form allows the old form to close without terminating the root loop. |

Key startup, failure, shutdown-source, modal, resource-cleanup, exception-policy, real-host, and
integration scenarios explicitly select both the light loop and component-manager loop using
`AppContextSwitchScope`. This makes backend coverage part of ordinary test discovery rather than
depending on a local runtimeconfig edit. Scopes restore the process-wide switch. Test execution is
sequential through the repository's existing xUnit configuration; the new integration collection
also explicitly prohibits parallel execution.

Tasks and posted callbacks establish ordering; no sleeps, polling, or elapsed-time assertions drive
the scenarios. Dedicated caller-owned threads have a 60-second failure watchdog so a shutdown
regression reports failure instead of hanging the test runner indefinitely. This is not a startup or
shutdown performance threshold. Small repeated ownership cycles check resource isolation, not the
100-cycle measurements assigned to dotnet/winforms#14946.

No separate CI pipeline is needed. The SDK-style unit and UI integration projects discover the
tests automatically. `azure-pipelines-PR.yml` selects x64, x86, and arm64 builds;
`eng/pipelines/build-PR.yml` supplies Debug/Release legs and runs unit plus integration tests on
the public x64/x86 legs. Its existing conditions do not execute tests on arm64. These tests have
neither `IgnoreForCI` nor `failing` exclusions.

Remote CI execution and upstream API approval require external verification.

### C# and Visual Basic samples (dotnet/winforms#14945)

The runnable [lifetime samples](../../src/test/integration/WinFormsApplicationBuilder/README.md)
demonstrate deferred default forms, a context that replaces its main form, real background service
shutdown, run cancellation, and direct coordinated host stop despite an ordinary close veto.
Both languages retain a synchronous caller-owned `STAThread` entry point and configure the
application before creating controls. The VB sample deliberately does not enable its separate
application-framework startup driver.

The existing integration suite builds both executables and runs isolated smoke cases across all
five scenarios, both message-loop backends, and light/dark settings. The sample guide documents
manual Terminal Services, native ARM, visual, and slow-hardware acceptance checks separately;
automated process completion must not be presented as evidence for those environments.

### Lifetime measurements (dotnet/winforms#14946)

The [measurement harness](../../src/test/integration/WinFormsApplicationLifetimePerformance/README.md)
compares conventional `Application.Run(form)`, builder-only ownership, and a real hosted builder in
isolated processes on both loop backends. It separates process-launch-to-first-dispatch, first-use
UI startup, warm startup, and shutdown through disposal; records process-wide allocations; and
samples post-collection managed/native memory, threads, kernel handles, GDI/USER counts, and weak
ownership references. A normal comparison performs 100 measured cycles after first-use/warmup.

Existing integration CI discovers short correctness checks for the harness. It does not impose
shared-machine timing gates or claim remote/ARM/Terminal Services measurements. Resource trends
and one-time cache growth must be interpreted against the matching warmed legacy baseline;
shipping overhead budgets require representative workload evidence and owner approval.

### Follow-on DI and UI-scope research

The [DI and UI-scope research](winforms-application-builder-di-scopes.md) for
dotnet/winforms#14948 evaluates runtime activation and designer-safe service assignment, including
the scope-cleanup boundary that must precede the existing UI-completion/host-stop barrier.
It is a recommendation for the next prototype, not an implemented DI or Designer contract.
