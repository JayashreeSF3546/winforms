# WinForms Application Builder: shared VB/C# feature contracts and migration adapters

Status: **proposal for API and compatibility review; documentation only**.
Issue: [#14974 — Design shared VB/C# application-feature contracts and migration adapters][issue].
Parent: [#14972 — Bring Visual Basic Application Framework capabilities to the shared model][parent].

## What this issue proposes

VB already has application features such as startup cancellation, shutdown handlers, next-instance
activation, and splash screens. This issue designs how **both VB and C# can opt into those capabilities
through the same builder**, without removing the existing VB Application Framework.

Think of a contract as an agreement: what data a handler receives, when it runs, which thread runs it,
what cancellation means, and who cleans up after a failure. This issue defines those agreements and the
translation needed for existing VB code. It does **not** implement the features.

```text
VB or C# consumer
  -> configure one builder and optional unbuilt Generic Host
  -> opt into shared features / explicit migration adapters
  -> build once -> run once on the caller's STA -> coordinated cleanup

Existing VB application that does not opt in
  -> existing WindowsFormsApplicationBase.Run -> unchanged behavior
```

The [#14973 inventory][inventory] remains the capability/evidence baseline; this document does not
repeat it. Read upstream child and parent issue bodies and local canonical source on 2026-11-05.
Checkout: `ebec5583ee19d398d57b4e57b9b7cac9f32fcad0`, branch
`Application_Builder_14972_VB_Parity`. Inventory source links describe this checkout, not all releases.

Labels:

- **E — existing:** inspected source on this branch; builder and companion-package APIs are still
  prototypes pending their own reviews, not a claim of availability in released .NET.
- **P — proposed:** contracts/semantics below, including every new feature or adapter name.
- **U — unresolved:** an explicit review or implementation gate, not an implicit default.

## Existing foundations and limits (E)

| Canonical mechanism | Reuse / restriction |
|---|---|
| [`WinFormsApplication.CreateBuilder`][runner], [`WinFormsApplicationBuilder`][builder] | Configure/build on the owner thread. Public `UseStartupForm<TForm>()`, `UseStartupForm(Form)`, `UseApplicationContext(Func<ApplicationContext>)`, `UseHost(IHostBuilder)`, `Build()`. Deferred factories avoid constructing the main UI during build. An existing Form transfers ownership only on successful build and cannot satisfy a strict before-form-construction startup guarantee. |
| [`WinFormsApplication`][runner] | Caller-owned STA, one accepted run and one root pump; host starts off UI before deferred activation. `Run(CancellationToken)` and `StopAsync(CancellationToken)` already have different cancellation meanings. `Host` exposes the coordinated host or null. No shared feature registration, raw-argument snapshot, or VB pre-UI event exists here yet. |
| [`WinFormsApplicationLifetime`][lifetime] | Owner-thread, once-only started/stopping/stopped. Started is after startup UI creation, before the pump, **not** after `Shown`/paint. Stopping includes failed accepted startup; stopped includes teardown errors. Notifications continue to remaining subscribers and collect ordinary failures. These meanings remain unchanged. |
| [`WinFormsApplicationHost`][host], [`WinFormsHostStopBarrier`][barrier] | Reuse the real `IHost` and its root provider. UI cleanup precedes host teardown; the provider remains alive through host stop. Do not separately build/start/run/dispose the original host. Generic Host lifetime tokens are not UI-thread events. |
| [DI/UI scopes][scopes-design], [`WinFormsUiScope`][scopes] | Borrow the existing provider and ownership tree. Scope/factory seams include **internal** prototypes; do not treat them as public APIs. Current synchronous UI-scope cleanup does not support async-only services. No ambient provider in `My` or controls. |
| [`ConfigureWinFormsConfiguration`][configuration], [services design][services-design] | Explicitly opt into existing configuration/options/logging mechanisms. Raw file names, `/nosplash`, or forwarded arguments must not automatically become Generic Host key/value configuration. Do not duplicate defaults/providers. |
| [`IUserSettingsService<T>`][settings], [`UserSettingsCommit`][settings-commit] | Explicit typed operations/import, not VB autosave or an automatic legacy-store conversion. Change notifications have no UI affinity; persistence and notification completion are distinct. |
| [`WinFormsExceptionLogging`][logging], [package guidance][package] | Existing explicit UI-policy/diagnostic subscription owner. UI routing attaches at ApplicationStarted, too late for pre-UI failures. Process-wide diagnostics do not recover workers or observe tasks. Coordinate policy integration with Stream C rather than add a competing subscriber. |

## Proposed shared contract surface (P)

Names and shapes are **review vocabulary**, not declarations or approved APIs. Prefer a small opt-in
extension surface over additions to VB base types. Candidate namespace: `Microsoft.Extensions.WinForms`;
package/assembly placement is U (core lifecycle cannot require the optional services package).

| Contract / candidate shape | Data, rules, and ownership | Implementation owner |
|---|---|---|
| `ConfigureApplicationFeatures(Action<WinFormsApplicationFeaturesOptions>)` on the existing builder | One configuration per builder; no host/loop/provider created by the extension. Options and handler lists are copied/frozen by successful Build. Duplicate configuration, mutation after build, or simultaneous legacy/shared orchestration is rejected, not silently merged. No features enabled by default. | #14975; API gate |
| `WinFormsApplicationFeaturesOptions.CommandLine` | Input sequence excluding executable element zero; omitted input snapshots environment arguments excluding zero. Clone into a read-only collection with privately owned backing storage. Preserve order, empty strings, Unicode, and the supplied tokens; no reparsing/quote reconstruction. Null sequence means omitted input; null elements are rejected before runtime. Consumers cannot mutate original startup arguments through retained arrays. | #14975 |
| `Starting` with `WinFormsStartingEventArgs` | Candidate `EventHandler<T>` event on options. Read-only `CommandLine`, mutable `Cancel`, no Form requirement. Runs once on owner STA after host startup and synchronization-context setup, before deferred root UI activation. No dispatcher/pump-progress promise. Sender is the built application, not options or `My.Application`. | #14975 |
| `Shutdown` with `WinFormsShutdownEventArgs` | Candidate `EventHandler<T>` event on options. Read-only reason (`UserExit`, `RequestedStop`, `RunTokenCancellation`) for eligible normal shutdown; no Cancel. Runs once after normal pump return, before owned UI/provider disposal. Sender is the built application. Failure/canceled-startup cleanup uses existing lifetime notifications instead. Reason precedence for a race: recorded committed request wins; otherwise UserExit. | #14975 |
| `ExceptionPolicy` with `WinFormsExceptionContext` / `WinFormsExceptionDecision` | One explicitly selected synchronous decision callback, not several competing exception handlers. Original exception, phase, source, continuation eligibility, terminating status if known. Decisions: propagate, request stop and propagate, or continue UI dispatch **only when eligible**. Observer/logging is separate from policy. Default is underlying existing behavior, not silent suppression. | #14975 + Stream C |
| Command-line context for activation | Original startup snapshot never replaced. Each accepted next-instance payload gets a separate immutable snapshot and request identity. Forwarding is not configuration reload, startup re-entry, or host restart. | #14976 |
| `SingleInstance` / `NextInstance` contracts | Explicit logical application identity plus reviewed isolation/version/path policy; bounded activation payload, foreground request flag, acceptance/completion results. Candidate UI callback payload has immutable arguments and mutable foreground preference; it is not cancellable Startup. See activation rules below. | #14976 |
| `Splash` policy | Opt-in factory, minimum duration, suppression policy, ownership and readiness/close/failure contract; not a main Form instance. Factory/thread strategy and exact surface are U. No default splash created by merely using the builder. | #14977 |
| Information / network capabilities | Application metadata from assembly sources; runtime information from BCL/retained services; testable network-availability source and UI projection. Explicit subscriptions, source lifetime, and failure semantics below. No new transfer/telemetry infrastructure. | #14977 |
| Settings-on-exit policy | Explicit choice of retained legacy store or existing typed service; explicit UI snapshot preparation and persistence operation. Not a new storage interface. Save participation must join the existing coordinated stop, not an async-void event or a second provider. | #14975 + Stream C; samples #14979 |

Hostless operation remains supported: starting/shutdown callbacks and raw arguments do not require DI.
Host-backed callbacks may use the **same** `application.Host.Services` at the composition boundary;
controls receive typed dependencies, not a global service locator. Register service-backed features on
the supplied unbuilt host builder before Build; do not resolve scoped services from its root.

### Event ordering, cancellation, and results (P)

```text
Configure WinForms defaults before handles; snapshot args; select deferred startup
  -> optional role selection (#14976; secondary path below)
  -> Build one application / optional host
  -> accepted Run on STA
  -> host startup and validation (no waiting for UI dispatch)
  -> owner-thread context setup
  -> optional coordinated splash (#14977; exact readiness gate under review)
  -> Starting: Cancel? --yes--> skip root UI and pump; cleanup
  -> create root Form/context -> existing ApplicationStarted -> one root pump
  -> close veto? --yes--> stay running, no Shutdown/stopping caused by that close
  -> accepted stop / normal pump return -> existing ApplicationStopping once
  -> eligible feature Shutdown -> prepare selected settings snapshot
  -> detach UI-bound services; dispose owned UI/scopes; restore context
  -> release existing host UI barrier; complete selected persistence / host stop
  -> existing ApplicationStopped -> host/provider disposal -> return or throw
```

`ApplicationStopping` can occur **before** pump return for committed StopAsync/host/token stop.
It is not delayed or reraised to fit this diagram. `ApplicationStopped` is not a safe place for UI work
or save-on-exit: UI is already gone and the host has stopped, although host disposal follows it.
Generic Host token callbacks can race with UI notifications; only the UI sequence above is ordered.
No promise is made about arbitrary Form constructors, Load/Shown/paint, worker callbacks, or other UI threads.

| Boundary | Proposed precise rule / existing constraint |
|---|---|
| Defaults vs Starting | Apply existing font/DPI/styles/color/reveal mechanisms before handles; never wait until Starting to change defaults requiring handle-free initialization. VB ApplyApplicationDefaults adapters execute in that earlier configuration phase. Generated/user configuration precedence must be explicit, not dependent on registration accidents. |
| Starting subscribers | Invoke in registration order, synchronously on owner STA. Preserve VB-style mutable Cancel within this one dispatch: inspect final value after all successful handlers; a later handler can clear it. Stop user dispatch on first exception; route failure, then clean up. Do not use lifetime's notify-all aggregation for these feature callbacks. Async-void handlers, nested Run/DoEvents startup pumping, and synchronous waits for UI dispatch are unsupported. |
| Startup cancel | Final Cancel=true skips root factory, ApplicationStarted, pump, and feature Shutdown; cleans up acquired resources, including splash/host. For an accepted run, existing stopping/stopped occur. Proposed ordinary return if cleanup succeeds. No retry/resume. This is not runner-token cancellation. |
| Run token / preflight rejection | Existing already-canceled token rejects before runtime entry; Dispose still owns built resources. Wrong STA/context or nested-loop rejection is not an accepted feature startup. Existing accepted-run token cancellation reports OperationCanceledException after cleanup if no other failures take precedence. |
| Close cancellation vs stop | FormClosing can veto an interactive close before committed stop. Feature Shutdown cannot cancel. Existing StopAsync/host stop is committed, cannot be vetoed, and must not be synchronously awaited on UI. StopAsync's token cancels the caller's wait only. |
| Eligible Shutdown | Only after successful pre-UI startup, pump entry and normal pump return, without an escaping run/startup failure; includes an orderly committed stop or accepted run-token stop. Startup cancellation, primary startup failure, secondary forwarding, and failure before pump entry skip it. A handled recoverable UI dispatch exception alone does not disqualify later normal shutdown. |
| Shutdown subscribers | Registration order; first ordinary failure stops later feature handlers, is retained, and cannot prevent teardown. Existing lifetime subscribers still follow their notify-all/aggregate contract. Shared Shutdown runs with owner context still installed, unlike base VB Shutdown after OnRun restores it: an explicit opt-in difference. |
| Settings participation | Disabled by default. Prepare a snapshot only on eligible shutdown after successful Shutdown callbacks; do not implicitly save after their failure or after canceled startup. Host-backed persistence is a selected UI-free operation completed during coordinated host stop before stopped/provider disposal. Never await a change notification from its own handler. Ordering among hosted stop participants/timeouts needs Stream A/C review; no persistence guarantee on kill/fail-fast. |
| Errors and precedence | Retain original ordinary run/policy failures; cleanup proceeds and additional failures are collected using runner conventions. One ordinary failure propagates with its identity/stack; multiple failures may aggregate. Cleanup/persistence failure prevents a success claim. Cancellation does not mask an earlier failure. No automatic exit-code assignment, restart, or retry. |
| Ownership / abandonment | Before successful Build, caller owns supplied Forms and external resources. After Build, application owns transferred startup resources/host; feature resources transfer only after successful acquisition. Idempotent release, observe owned tasks, detach subscriptions even if Run never starts. Critical failure/forced termination is outside graceful cleanup guarantees. |

This requires **new reviewed integration seams inside the existing runner**, not wrapper code that
pretends ApplicationStarted is pre-UI or assumes a late event can perform async teardown. No seam is
implemented by #14974. Plain builder use retains its current behavior.

### Exception decisions and limits (P)

| Source | Thread / allowed outcome |
|---|---|
| Startup/build | Host build can fail before a built application exists; propagate to the caller with local acquisition cleanup, not an application lifecycle event. Host startup is off UI; ordinary startup failure can be reported once on owner STA after the owned host task is observed. Pre-UI handler/factory failures terminate startup. ContinueUi is invalid here; do not turn VB ExitApplication=false into retry/resume or implicit success. |
| UI dispatch | Only exceptions actually routed by WinForms on this owning thread are eligible for ContinueUi. Preserve underlying debugger/UnhandledExceptionMode behavior. RequestStop uses the existing committed stop, not process kill; it differs from legacy Application.Exit, which can encounter close veto. |
| Feature Shutdown / disposal | No continuation of failed multicast dispatch or recreation of disposed UI. Record and propagate after required cleanup. Do not recursively route failures from the exception-policy callback; retain original plus policy failure. |
| Owned async splash/IPC/network tasks | Observe at their owning boundary, classify source, and marshal application callbacks only while a valid owner dispatcher exists. No fake UI availability during pre-pump wait or after stopping. |
| Hosted workers / arbitrary background work | Preserve Generic Host BackgroundServiceExceptionBehavior. AppDomain and unobserved-task events are opt-in diagnostics on originating threads, not cancellable UI events; no automatic SetObserved or recovery. Native/corrupted-state failures, stack overflow, fail-fast, and terminating notifications cannot be continued by policy. |

One UI policy subscription owner must arbitrate with [existing exception logging][logging].
Proposed pre-UI failure handling needs a runner seam, not additional Application.ThreadException
subscriptions. Diagnostic failures must not replace the original application failure; no argument,
settings, exception-detail, or personal-data logging is enabled by this proposal.

## VB compatibility and migration adapters (P)

**Source/binary requirement:** leave `ApplicationBase`, `ConsoleApplicationBase`,
`WindowsFormsApplicationBase`, their public/protected members, overrides, event delegates, and existing
[event argument types][startup-args] unchanged. Preserve generated partial classes and supported tooling.
Do not silently redirect old Run, change defaults/identity/stores, obsolete the framework, or retarget
previously built binaries to a new lifecycle. Opt-in differences below affect only migrating consumers.

| Legacy behavior / convenience | Proposed mapping and compatibility boundary |
|---|---|
| Startup / OnInitialize / OnStartup | [Base sequence][vb-model] gates initialization/startup before OnRun. Translate extracted user startup logic to Starting with a read-only argument copy and final Cancel result. OnInitialize overrides are not just event handlers: review defaults, principal/culture and custom work explicitly. Do not invoke protected lifecycle overrides reflectively. |
| Shutdown | [Base OnRun/OnShutdown][vb-hooks] raises it only after normal OnRun return; no Cancel. Translate successful-shutdown logic to feature Shutdown, **not** every stopping/stopped notification. Context-restoration timing and token-stop delivery differ as documented above. Overrides replacing OnRun/OnShutdown require manual migration. |
| Existing event signatures | Candidate VB adapter accepts application-supplied callbacks using [existing delegates][vb-delegates]: `StartupEventHandler`, `ShutdownEventHandler`, `StartupNextInstanceEventHandler` and `UnhandledExceptionEventHandler`. Allocate existing args over defensive read-only copies; preserve the legacy sender via explicit caller input. Copy Cancel/foreground decisions back. Exact adapter overloads need API review; do not replace old delegates with new ones. |
| UnhandledException / ExitApplication | Translate the original exception and exit intent only for approved phases. Legacy lifecycle suppression without resumption and Application.Exit semantics do not match shared propagation/committed stop. Default shared adapter must document these partial-parity limits; any legacy suppression wrapper must be an explicit, reviewed caller boundary, never silently swallow Run failures. |
| CommandLineArgs | Forward startup snapshot with executable excluded. Never replace it with next-instance arguments or host configuration. Existing base constructors/argument caching remain unchanged. |
| ShutdownStyle / MainForm | Select deferred Form/context using existing builder. AfterMainFormCloses vs AfterAllFormsClose requires a reviewed context policy in #14975; it is not an existing builder option. Custom/tray contexts need no visible main form; global OpenForms is not a cross-thread ownership tree. |
| IsSingleInstance / StartupNextInstance | Separate identity/activation adapter (#14976); no nested old Run/pipe listener. Translate [legacy next-instance args][next-args] and BringToForeground to shared request; focus is best effort. Legacy GUID/version/MVID identity is an explicit migration policy, not an assumed secure shared default. |
| SplashScreen / minimum duration | Translate intent, suppression switches and duration, not legacy UI-blocking waits or detached-task mechanics. Do not use the same Form as splash and root; ownership/thread/failure changes are explicit (#14977). |
| Network / Info / runtime information | Shared availability source/UI projection and assembly/BCL information services (#14977). Network availability is not Internet reachability. Host ApplicationName/content root are not replacements for all AssemblyInfo values. Retained VB service use is permitted; do not duplicate wrappers merely to remove a VB namespace. |
| SaveMySettingsOnExit / My.Settings | Generated autosave, not the runtime Boolean alone, performs the save; see [inventory settings evidence][inventory-settings]. Retain ApplicationSettingsBase/providers/store unless explicitly importing to the existing typed service. Disable/remove the old generated writer through supported migration/tooling choices before enabling shared save. Never edit generated files by hand or allow two automatic writers. |
| My.*, Handles, ApplicationEvents.vb, default Form instances | Language/tooling conveniences, not shared runtime contracts. C# uses normal delegates/events; VB can use AddHandler or a consumer-owned WithEvents projection if later approved. Existing `Handles Me.Startup` on MyApplication continues only under its existing framework. No automatic retargeting, compiler change, or promise of new My.Application members. |
| Culture / My.User / Log / other My services | Retain existing methods and principal policy; configure BCL culture/authentication and existing ILogger explicitly if migrating. Do not infer IPC access from Thread.CurrentPrincipal or silently redirect VB Log. Resources, deployment/ClickOnce, transfer APIs and Designer infrastructure are outside this contract. |

### Adapter execution and migration flow

Candidate adapter concept: **registration/argument translation only**, not a derived replacement
WindowsFormsApplicationBase, new host, or runner. Adapter registers once with shared features and borrows
typed dependencies. It does not raise MyApplication's existing events through reflection, call old
OnRun/Run, build a provider, start a second root loop, or activate a VB default Form early.

```text
Phase 0: unchanged VB framework + ApplicationEvents.vb + generated settings
  -> Phase 1: extract reusable business logic; legacy handlers still call it
  -> review overrides, generated autosave, defaults, sender and exception differences
  -> Phase 2: explicitly choose shared entry point for an application/run
     register extracted logic directly or via reviewed argument adapter
     remove legacy orchestration / duplicate writer/subscription from that entry path
  -> Phase 3: opt into identity/activation, splash/services as their owners deliver them
  -> Phase 4: paired VB/C# tests and migration samples (#14978/#14979)
```

Incremental adoption is per capability/business logic, **not two lifecycles in one process**.
Keeping the legacy entry point is always valid until requirements and review gates are met.
Project startup-object/generation changes must use supported tooling; changing the generated
Application.Designer.vb or relying on `Handles Me.*` to bind to a different sender is not a migration plan.
Applications with arbitrary protected overrides may remain legacy rather than use an incomplete adapter.
Designer construction stays service-free and must not elect an instance or perform host/settings I/O.

## Later capability policy boundaries (P/U)

| Capability | Required agreement before its owning implementation |
|---|---|
| Identity / role selection (#14976) | Recommend current-user + current-Windows-session isolation with no implicit elevation crossing; logical ID, executable-path and version coexistence policy must be explicitly reviewed. Preserve unchanged legacy identity separately. Reject insecure machine-global fallback. Role selection is before primary host Build/start and root UI; cannot require an already-built provider. This needs an explicit pre-build composition/preflight seam; the current Build has no such API. |
| Secondary / activation (#14976) | Secondary forwards and exits without Starting, feature Shutdown, primary host/provider or splash; process entry/configuration code can already have run. Distinguish primary, accepted secondary, rejected/access denied, unavailable/stopping, timeout/unknown and handler failure. Never interpret every ownership error as a secondary. |
| Delivery / focus (#14976) | Validated, size-bounded payload and authenticated boundary; serialize requests on owner UI only after readiness. Startup queue must be bounded and rejected on canceled/failed startup or stop. Distinguish transport acceptance, queue acceptance and UI-handler completion; lost acknowledgement can leave outcome unknown. No exactly-once guarantee across crashes; request-ID retry/dedup retention policy is U. Handler completes before best-effort focus; no focus guarantee or main-form requirement. |
| Session/path/race validation (#14980) | User/session/RDP/reconnect/elevation/path/version matrix, rapid reopen, activation during startup/teardown, x64/ARM64/Surface Go 2. #14976 protocol tests do not certify those environments. |
| Splash (#14977) | Proposed minimum measured from explicit displayed/readiness acknowledgement, not factory call; readiness definition/thread strategy is U. Startup coordination does not block UI in a timer wait. Explicit suppression preserves exact legacy `/nosplash` and `-nosplash` intent without removing raw args. Cancellation/failure/early close releases owned splash promptly rather than honoring a minimum at the cost of shutdown. Observe errors; no abandoned task/second primary lifetime. |
| Network (#14977) | Lazy opt-in source; establish principal/context before UI subscription. Source emits state changes, UI projection serializes owner-thread callbacks only while running. Unsubscribe at stopping; drain/reject queued callbacks before releasing dispatcher. Unsupported source/setup failure is distinguishable from offline; handler errors use approved policy, never silently disappear. No startup reachability check or network-transfer rewrite. |
| Information (#14977) | Separate cached assembly metadata and live process/runtime queries; reuse entry/calling-assembly sources and BCL. Missing/unsupported data has an explicit unavailable result, not an invented value. Single-file/trimming/AOT and assembly location need validation; host name/content root alone are insufficient. |
| Settings (#14975/#14979) | Explicit one-writer selection and failure result; UI-affine legacy snapshot/save policy vs UI-free typed persistence. Retain storage paths/providers/schema unless importing deliberately; typed commit success does not imply notification success. Automatic save scheduling/host stop ordering is a review gate, not current IUserSettingsService behavior. |
| Benchmarking (#14981) | Measure legacy/shared startup/shutdown, splash/IPC/activation, memory/working set/handles/threads after implementations and validation. No performance or leak claims from this design. |

## Small consumer illustrations

**Existing branch APIs (E), not released-platform guidance.** Inside an application-supplied STA entry
point, after existing WinForms defaults configuration, with an application-defined parameterless MainForm:

```csharp
using WinFormsApplication application = WinFormsApplication.CreateBuilder()
    .UseStartupForm<MainForm>()
    .Build();
application.Lifetime.ApplicationStarted += (_, _) =>
    Console.WriteLine("Startup UI created; not VB Startup or Form.Shown.");
application.Run();
```

```vb
Using application As WinFormsApplication = WinFormsApplication.CreateBuilder().
    UseStartupForm(Of MainForm)().
    Build()
    AddHandler application.Lifetime.ApplicationStarted,
        Sub(sender, e) Console.WriteLine("Startup UI created; not VB Startup or Form.Shown.")
    application.Run()
End Using
```

Both import `Microsoft.Extensions.WinForms`; the caller supplies STA/defaults and any diagnostics.
Host integration, if selected, is `.UseHost(unbuiltHostBuilder)` before Build in either language;
never call that host builder's Build or Run separately. These excerpts are not standalone samples.

**Proposed API sketches (P), deliberately not compile-ready against current source.**
Only `ConfigureApplicationFeatures`, options, Starting and its args below are new review candidates.
`args` is the entry-point string array without the executable. The examples use no phantom currently
available pre-UI lifetime event:

```csharp
var builder = WinFormsApplication.CreateBuilder().UseStartupForm<MainForm>();
builder.ConfigureApplicationFeatures(features =>
{
    features.CommandLine = args;
    features.Starting += (_, e) =>
    {
        if (e.CommandLine.Contains("--validate-only"))
            e.Cancel = true; // skip deferred main UI; still release acquired resources
    };
});
using var application = builder.Build();
application.Run();
```

```vb
Dim builder = WinFormsApplication.CreateBuilder().UseStartupForm(Of MainForm)()
builder.ConfigureApplicationFeatures(
    Sub(features)
        features.CommandLine = args
        AddHandler features.Starting,
            Sub(sender, e)
                If e.CommandLine.Contains("--validate-only") Then e.Cancel = True
            End Sub
    End Sub)
Using application = builder.Build()
    application.Run()
End Using
```

`Contains` in these sketches uses ordinary LINQ imports. Equivalent callbacks get equivalent args,
threading and cancellation. A later VB adapter can call extracted startup logic accepting existing
StartupEventArgs and copy Cancel back; it cannot make `Handles Me.Startup` bind to the builder.
No shipping declaration, PublicAPI change, generated artifact or migration sample is added here.

## Acceptance, definition of done, and downstream test plan

This child's reviewable deliverable is the contract/mapping design, not the parent's completed runtime.

| #14974 acceptance / DoD | Evidence in this document / remaining gate |
|---|---|
| Confirmed capabilities mapped to shared foundations | Existing-mechanism table, proposed surface and inventory link; no duplicated capability inventory. |
| VB/C# ergonomics and compatibility adapters specified | Paired labeled sketches, event argument/sender translation, opt-in phased migration and override/tooling restrictions. |
| Ordering/threading/cancellation/error/ownership defined | Phase flow and boundary tables; accepted stop separated from interactive close; normal shutdown separated from teardown. |
| Decisions, alternatives and dependencies recorded | Policy boundaries below/above with issue ownership; unresolved seams surfaced rather than claimed as available. |
| Independently reviewable child-only change | One design file; no runtime/API/template/test implementation. Local links/format/citations checked; no code/generated artifacts requiring build or CI changed. Stakeholder/API acceptance is still pending. |

Required later tests (not added/run here):

- **#14975:** deterministic phase traces and owner-thread assertions for normal/canceled/failed startup,
  factory failure, close veto, token/host stop races, callback failures, once-only teardown and no double-run.
  Immutable arguments including mutation of the original input array, empty/Unicode tokens and host parsing
  separation. Settings snapshot/save ordering and failure without UI waits; lifetime semantics unchanged.
- **#14976/#14977:** controlled IPC readiness/queue/acceptance/completion and explicit unknown outcomes;
  malformed/bounded payloads, stopped dispatcher, custom context/no main form; controlled-clock splash
  readiness/minimum/early-close/cancel/failure; replaceable network source and subscription cleanup.
- **#14978:** equivalent VB/C# consumers plus unchanged legacy source and previously built binary consumers,
  existing event args/delegates/overrides, generated Settings.Designer.vb with custom providers, sender and
  subscription order, Debug/Release with and without debugger, no competing exception subscription/provider.
  Isolate process-wide WinForms state; CI uses signals/controlled clocks, not sleeps or exact elapsed-time
  correctness assertions. Environment-only cases are reported separately, not hidden as CI passes.
- **#14979–#14981:** supported IDE migration/regeneration and Designer checks; real multi-session/hardware
  validation; reproducible baseline/shared performance and resource measurements. Preserve old-project path.

### Review gates and rejected alternatives

1. **API/VB compatibility review:** approve names, assemblies/dependencies, event vs callback shapes,
   sender/Cancel semantics, exception differences, accepted token-stop Shutdown and migration restrictions.
   Review existing prototype foundations as well; this document cannot approve their public surface.
2. **Stream A/B/C integration review:** approve pre-UI/normal-shutdown seams and settings async stop placement,
   exception-subscription arbitration, scope/disposal restrictions, and hostless behavior.
3. **#14976/#14977 reviews:** identity/session/path/version/elevation, acknowledgement/dedup/queue limits,
   splash readiness/thread model, network/information unavailable results. The lifecycle design must leave
   room for them but must not implement them in #14975.
4. **Compatibility/tooling acceptance:** generated settings and startup-object support, arbitrary overrides
   and legacy suppression limitations; exact legacy-behavior adapter mode is not promised without evidence.

Rejected: replacing/obsoleting the VB framework; a VB-only lifetime/container; old Run nested in new Run;
mapping Startup to ApplicationStarted or Shutdown to all stopped events; cancellable late Shutdown;
unreviewed public APIs; ambient My service provider; automatic settings conversion/two writers;
second exception observer; insecure global IPC fallback; universal worker recovery; UI-blocking splash
waits; requiring all migrated applications to rewrite forms/templates.

Trade-off: the shared model preserves capabilities and avoids duplicate infrastructure, but exact legacy
timing/suppression/generated-event behavior cannot be promised by renaming events. Explicit extraction and
reviewed adapters cost migration effort while preserving the untouched legacy path.

**Handoff:** local #14974 design is ready for API/compatibility review. Implementation-ready: **NO until
the relevant gates are resolved**. Recommended next child: [#14975][next], only after explicit instruction
and review of the lifecycle/exception/settings contracts. Do not start it from this deliverable.

## Source references

Existing behavior statements above are static source findings, not executed validation. Authoritative
legacy behavior and external generated-settings/documentation evidence are catalogued in [#14973][inventory].

[issue]: https://github.com/dotnet/winforms/issues/14974
[parent]: https://github.com/dotnet/winforms/issues/14972
[next]: https://github.com/dotnet/winforms/issues/14975
[inventory]: winforms-application-builder-vb-parity.md
[inventory-settings]: winforms-application-builder-vb-parity.md#settings-on-exit-is-partly-generated-behavior
[builder]: ../../src/System.Windows.Forms/Microsoft/Extensions/WinForms/WinFormsApplicationBuilder.cs
[runner]: ../../src/System.Windows.Forms/Microsoft/Extensions/WinForms/WinFormsApplication.cs
[lifetime]: ../../src/System.Windows.Forms/Microsoft/Extensions/WinForms/WinFormsApplicationLifetime.cs
[host]: ../../src/System.Windows.Forms/Microsoft/Extensions/WinForms/WinFormsApplicationHost.cs
[barrier]: ../../src/System.Windows.Forms/Microsoft/Extensions/WinForms/WinFormsHostStopBarrier.cs
[scopes]: ../../src/System.Windows.Forms/Microsoft/Extensions/WinForms/WinFormsUiScope.cs
[scopes-design]: winforms-application-builder-di-scopes.md
[services-design]: winforms-application-builder-services.md
[configuration]: ../../src/Microsoft.Extensions.WinForms.Hosting/src/WinFormsHostBuilderExtensions.cs
[settings]: ../../src/Microsoft.Extensions.WinForms.Hosting/src/IUserSettingsService.cs
[settings-commit]: ../../src/Microsoft.Extensions.WinForms.Hosting/src/UserSettingsCommit.cs
[logging]: ../../src/Microsoft.Extensions.WinForms.Hosting/src/WinFormsExceptionLogging.cs
[package]: ../../src/Microsoft.Extensions.WinForms.Hosting/src/PACKAGE.md
[vb-model]: ../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/ApplicationServices/WindowsFormsApplicationBase.vb#L545-L597
[vb-delegates]: ../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/ApplicationServices/WindowsFormsApplicationBase.vb#L16-L46
[vb-hooks]: ../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/ApplicationServices/WindowsFormsApplicationBase.vb#L849-L948
[startup-args]: ../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/ApplicationServices/StartupEventArgs.vb
[next-args]: ../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/ApplicationServices/StartupNextInstanceEventArgs.vb
