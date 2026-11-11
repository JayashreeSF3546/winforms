# Shared splash screen and application services

Issue: [#14977 — Implement shared splash-screen and application-service capabilities][issue].
Parent: [#14972][parent]. Branch: `Application_Builder_14972_VB_Parity`.

Status: **implemented internal opt-in prototype; public API/compatibility acceptance remains open**.
Read this alongside the [inventory][inventory], [contract proposal][contracts],
[shared lifecycle implementation][lifecycle], and [single-instance implementation][instance].
None of the new names below are approved public APIs. Classic VB framework source, binary
contracts, project generation, and default builder behavior are unchanged.

## Requirement in simple terms

VB and C# applications should share three optional capabilities:

1. Show an application-owned splash and keep it displayed for a selected minimum duration,
   **without freezing the UI just to wait for the clock**.
2. Read application metadata from the application's explicitly selected assembly.
3. Receive current IP-network availability changes on the application's own UI thread.

This is not a second application framework, an Internet connectivity test, a download/upload
rewrite, or a new DI/settings/logging/configuration system.

## Simple startup and stop flow

```text
Configure builder -> Build (freeze feature options, snapshot selected assembly metadata)
  -> Run on caller STA -> existing optional host startup -> existing owner dispatcher
  -> Starting (original argument tokens)
       Cancel / failure / committed stop?
         yes -> no splash acquisition or root activation -> existing cleanup
         no  -> optional splash factory on this STA -> validate ownership -> Show without activation
                  record monotonic time AFTER successful Show returns
             -> existing deferred root Form/context construction (still synchronous)
             -> reject splash == main Form or an already-visible main Form
             -> existing ApplicationStarted (still before the pump, not a new Ready event)
             -> enter ONE existing root message loop
                  no splash / exact suppression -> normal root visibility
                  selected splash:
                    minimum already elapsed / zero -> release splash -> show main
                    otherwise -> one-shot timer posts an owner-dispatcher check
                                 root loop remains responsive
                                 early check? rearm for the remaining interval
                                 minimum reached? release splash -> show main
             -> after successful main visibility acknowledgement, and absent a stop:
                  enable next-instance acceptance -> optional network subscription + initial snapshot
  -> close/context exit OR StopAsync / host stop / accepted run-token cancellation
  -> existing owner-thread stopping:
       invalidate/unsubscribe network -> invalidate/dispose timer and splash
  -> for splash-selected startup, feature Shutdown/settings preparation only after readiness
     (without a splash, existing #14975 eligibility remains unchanged)
  -> existing UI/context/scope teardown -> existing host barrier/save/stop -> stopped/provider disposal
```

### Exact splash semantics and limits

- `SplashScreenFactory : Func<WinFormsSplashScreen>` is optional. No factory means no splash,
  timer, or minimum gate. Factory invocation is after successful `Starting`, unlike legacy VB's
  earlier initialization splash. A canceled `Starting` never constructs a splash.
- `WinFormsSplashScreen` is an **internal** Form base with sealed nonactivating display behavior.
  Supply a fresh, unshown owner-STA instance; a null/disposed/foreign-thread result fails startup.
  An invalid foreign-thread result does not transfer ownership and is not disposed on the wrong
  thread. The supplier remains responsible for invalid results and resources a throwing factory
  never returns. Validated acquisition transfers ownership; later Show/factory/activation failure
  releases acquired resources.
- The default minimum is two seconds **only if a factory is selected**. Build snapshots the
  duration, factory, and `TimeProvider` reference; retained options cannot reconfigure the run.
  Negative durations, durations above the timer's supported maximum, and a null clock are rejected.
- Exact ordinal `/nosplash` or `-nosplash` tokens skip acquisition, leaving the original tokens
  untouched. `/NOSPLASH` does not suppress. Zero duration still selects a splash, but does not
  allocate a timer or impose an artificial delay.
- The epoch is successful `Form.Show()` return (visible acknowledgement), **not factory entry,
  first paint, or physical screen presentation**. The gate ends before splash disposal/main
  visibility. It counts elapsed monotonic time, including synchronous root construction. Scheduling
  can finish later under load, never intentionally earlier; fractional timer delays round up.
  `TimeProvider.System` is the production clock/scheduler; tests replace this BCL seam explicitly.
  No new scheduling framework or prior repository-specific clock abstraction is introduced.
- Main construction still happens before root-pump entry. Main `Load` runs when the existing
  visibility seam finally shows it; `Shown`/paint can occur later. `ApplicationStarted` keeps its
  previous meaning: constructed root, not displayed/ready UI. Do not use it as splash completion.
  Next-instance requests remain `NotReady` throughout the splash minimum and main visibility/Load;
  queued requests are not accepted/replayed early. Existing identity/session/elevation rules remain.
- Factories, `Starting`, Form constructors, `Load`, disposal, and injected source operations remain
  synchronous and must be short/nonblocking. **This prototype does not make heavy synchronous
  initialization responsive**, guarantee a painted splash before construction, or introduce async
  UI initialization. That requires an explicit reviewed lifetime contract, not `DoEvents` or a
  second pump. Root factories must return hidden UI; arbitrary self-showing auxiliary windows and
  other UI threads are outside this gate. A custom root context without a MainForm is supported,
  but the framework cannot gate windows that the custom context shows independently.
- Early accepted close, disposal, or hiding of the splash commits stop rather than revealing the
  main UI early. Cancellation/startup failure/host stop do not honor an artificial remaining
  minimum. Disposing the splash normally detaches its early-close handlers first. Stop requested
  reentrantly by disposal or main Load is checked before revealing/subscribing further.
  Cross-thread stop is a request, not preemption of an already-admitted synchronous visibility,
  source operation, or user callback. Owner-thread guards observe it at the next boundary.
- One `WinFormsSplashLifetime` owns the acquired Form and at most one one-shot `ITimer`.
  Dispose is once-only, clears the dispatcher/ready callback, invalidates late work, releases the
  timer, and disposes a still-live Form in a finally. The callback never owns a Form on a worker.
  No task is abandoned or joined, no timer wait blocks STA, and no new Application.Run, nested
  pump, spin, sleep, explicit foreground activation, or process control is introduced.
- Managed scheduling/ready callback failures are retained and stop the run. Owner-thread failures
  use existing terminal `ReportFailure` with `ApplicationService`; they cannot be suppressed or
  retried. A failed dispatcher post is retained and requests existing shutdown without pretending
  to invoke an owner-thread reporter. Native callback fatal failures still have the limitations
  documented by #14975; this does not add a competing ThreadException policy.
- A pump that stops during the splash gate has not completed feature startup: no feature Shutdown
  or selected settings save. Existing stopping/stopped still occur. After readiness, ordinary
  close vetoes and committed nonvetoable host/token/application stop keep existing semantics.

## Application information

`WinFormsApplicationFeatures.ApplicationAssembly` explicitly selects the metadata source.
`WinFormsApplication.Information` is null when no assembly is selected. With selection, Build
creates one immutable `WinFormsApplicationInformation` snapshot; access is not lazy and does not
query the process, entry assembly, host application name, or current working directory.
The snapshot can also be constructed explicitly at an internal composition boundary and registered
with the **existing** provider if needed. No automatic registration/provider is added.

| Members | Source / absent-value semantics |
|---|---|
| `AssemblyName`, `Version`, `AssemblyCulture` | Selected assembly's `AssemblyName`; missing name/culture is empty, version may be null. Culture is assembly identity, not current UI/thread/installed culture. |
| `Title`, `ProductName`, `CompanyName`, `Description`, `Copyright`, `Trademark` | Corresponding selected assembly attributes; absent attributes produce empty strings. No FileVersionInfo/executable or host-name substitutions. |
| `AssemblyLocation`, `DirectoryPath` | Selected assembly location/directory; absent location is null for both. Dynamic assemblies explicitly have no location. Single-file embedded assemblies may have no location; this intentionally does not substitute AppContext.BaseDirectory, executable path, or Environment.CurrentDirectory. |
| `FrameworkDescription`, `OSDescription`, `ProcessArchitecture`, `OSArchitecture` | BCL RuntimeInformation sampled at snapshot creation. These are runtime identity, not live performance counters or a deployment certification. |

No ownership of the Assembly or process is transferred. Reflection failures are not swallowed into
empty metadata. Dynamic-assembly absence is tested; single-file, trimmed, NativeAOT, unusual load
contexts, non-Windows UI execution, and restricted runtime environments are not validated deployment
targets here. No new runtime memory/working-set/handle/thread/loaded-assemblies/stack-trace service is
invented; callers can retain appropriate VB services or query the BCL at their intended sampling time.

## Network notification flow

```text
No selected NetworkAvailabilityChanged callback -> no source access/subscription
Root visibility succeeds and no stop -> use borrowed injected source or default platform adapter
  -> subscribe once -> read initial IsAvailable as baseline (no artificial initial Changed event)
Any source thread signals NetworkAddressChanged
  -> coalesce pending invalidations -> BeginInvoke on the actual owner dispatcher
  -> reject if disposed/stopping -> UI re-reads current availability
  -> compare with last UI state -> if changed, invoke selected Action<bool> on owner STA
Stopping -> mark inactive + clear dispatcher BEFORE remove -> remove handler once
  -> stale queued callbacks / late source notifications do nothing
```

- `IWinFormsNetworkSource` exposes `IsAvailable` and a `NetworkAddressChangedEventHandler` event
  named `Changed`. The production `WinFormsNetworkSource` delegates to
  `NetworkInterface.GetIsNetworkAvailable()` and `NetworkChange.NetworkAddressChanged`; it does not
  cache payloads from potentially reordered worker callbacks. The source property is also the
  explicit current-state query when needed; reads can fail and are not converted to offline.
- `NetworkAvailabilityChanged : Action<bool>` selects owner-UI projection. A source supplied without
  a callback is not accessed. There is no pre-ready subscription, early replay, initial notification,
  or new delivery once its final owner-thread stop check observes a committed stop. An already
  admitted callback can finish; stop never interrupts arbitrary synchronous user code.
  Subscription is after main visibility acknowledgement,
  not after Shown/paint. Coalescing intentionally reports **current state**, not every transient
  transition. Changes before subscription establish baseline, not a transition history.
- Provider identity/context must be established by the application before Run. No arbitrary
  SynchronizationContext is captured from the configuration thread or an OS callback, and no UI
  context is acquired after teardown. An in-progress source callback either posts before
  invalidation or sees inactive state; queued work checks stopping again before delivery.
- Sources are **borrowed**. Unsubscription belongs to the runner; disposal of an externally
  supplied source remains with its original owner/provider, including an existing host provider.
  Start marks the subscription attempt before event add so partially failing adds still get remove.
  Stop is checked again after add, so reentrant stop during subscription skips the initial read.
  Teardown invalidates dispatch even if a custom remove throws. A provider whose remove operation
  fails to remove its own handler cannot be guaranteed to release that external subscription;
  this failure is propagated, not claimed as successful cleanup.
- Setup/add/read/callback failure is terminal for this explicitly opted-in prototype, retained and
  owner-reported through `ApplicationService`, then existing stop/cleanup. Unsubscribe failure is
  retained through existing cleanup aggregation without recursive feature reporting. A failed
  dispatcher post records failure and requests stop without an off-thread user reporter.
  Critical/fatal failures are not made recoverable. This deliberately does not invent an
  offline/unsupported/error union or a recovery policy ahead of public API review.
- IP-network availability is **not Internet, DNS, authentication, or backend reachability**. No
  connection probes, transfer clients, adapter changes, global network settings, or logging of
  network/user/application data are added.

## Internal composition examples (not external migration samples)

Inside this assembly or its existing friend tests, after normal WinForms defaults and on the
caller-owned STA (`MainForm` below is the application's own short-construction Form type):

```csharp
using WinFormsApplication application = WinFormsApplication.CreateBuilder()
    .UseStartupForm<MainForm>()
    .ConfigureApplicationFeatures(features =>
    {
        features.ApplicationAssembly = typeof(MainForm).Assembly;
        features.SplashScreenFactory = () => new WinFormsSplashScreen();
        features.MinimumSplashDisplayTime = TimeSpan.FromSeconds(2);
        features.NetworkAvailabilityChanged = available =>
        {
            // Owner-STA callback: update the application's UI, not an Internet-status guarantee.
            Debug.WriteLine($"IP network available: {available}");
        };
    })
    .Build();
WinFormsApplicationInformation information = application.Information!;
application.Run();
```

For test isolation, assign `SplashTimeProvider` and a borrowed `NetworkSource` in that same feature
configuration. Neither causes a new host/provider. Existing `UseHost` may still select the one
unbuilt Generic Host and its existing services. No global current application or `My` service
locator is introduced. These CLR contracts use Form/BCL/delegate types, not VB-specific types;
external VB/C# consumption cannot be claimed while this surface remains internal. Compiled
cross-language validation/adapters are #14978/review work, not simulated by these C# friend tests.

## Known reports and rejected alternatives

Evidence checked on 2026-11-10 against public issue bodies/comments and this branch:

- [#4279][splash-report] remains open: reported .NET 5 splash missing with a Form startup object.
  The [maintainer comment][splash-comment] explains that bypassing Sub Main bypasses the VB
  Application Framework and points to project-system#6787. Here the selected factory is invoked
  by the shared Run path independently of VB project generation. **This does not fix or reproduce
  the legacy generated-startup issue**, and no template/project-system change is made.
- [#9807][network-report] remains open: modernization of legacy Network transfer methods away
  from WebClient, with compatibility/testing concerns in comments. Local `Network.DownloadFile.vb`
  and `Network.UploadFile.vb` still use WebClientExtended. Local `Network.Availability.vb` instead
  uses NetworkInterface/NetworkChange and posts to captured context. This prototype reuses those
  BCL availability primitives, **not the transfer APIs**, so no HttpClient/WebClient stack or
  credentials/server tests are introduced and #9807 is not claimed resolved.
- Local `WindowsFormsApplicationBase` starts a detached splash pump via Task.Run and waits on
  `_formLoadWaiter.WaitOne()` during main Load. Existing VB tests exercise Show/Hide and property
  compatibility, not this new shared minimum gate. Both old source and tests remain untouched.
- Rejected: reuse the legacy wait or detached splash pump; add DoEvents/nested Run; block startup
  on a timer task; add async-void startup/cleanup; reinterpret ApplicationStarted as paint-ready;
  guess EntryAssembly/calling assembly under a test/host driver; treat host content root as assembly
  location; forward cached network payloads; capture a configuration-thread context; silently turn
  unsupported source errors into offline; dispose a borrowed source; rewrite legacy transfer APIs.

## Remaining acceptance gates

1. API and VB/WinForms compatibility review of #14974 and these provisional internal types,
   readiness/early-close/error policy, packaging, and source lifetime. This is feasible implementation,
   **not approved public/shared-feature completion or full parent acceptance**.
2. Stream A review of the visibility seam and meaning of not-ready pump shutdown; any asynchronous
   initialization contract must preserve the one root lifetime instead of being invented here.
3. Review metadata special-deployment semantics and opt-in differences from legacy lazy Info.
4. #14978 cross-language/CI parity after relevant review; #14979 samples/templates are not started.
5. Real OS network transitions and unsupported platform/runtime deployment validation, single-file/
   trimming/AOT, multi-user/session/RDP, x64/ARM64/machine validation, performance/resource measurement
   and remote CI remain unperformed. Fake notifications prove no real network transition.

## Focused local validation

The new `WinFormsDesktopServicesTests` covers monotonic early/due checks, real timer dispatch on
both root engines, STA affinity and disposal, zero/exact suppression, startup cancellation/failure,
application/host/token/early-close/early-hide stop, reentrancy, distinct hidden main Form, single-
instance NotReady/ready integration, explicit assembly/dynamic absence, available/unavailable
network baselines, coalescing, owner marshaling, pre-ready/late notifications, source/setup/read/
callback/remove failures and borrowed ownership. No sleep-based timing proof, network mutation,
external server, or unrelated process control is used. Owned STAs have cancellation watchdogs.

### Results (2026-11-10, Debug x64)

Authoritative affected product/hosting/unit-test graph: **0 warnings, 0 errors** using `build.cmd`,
including repository analyzers. SDK: `12.0.100-alpha.1.26531.102`; the rebuilt executable's
runtimeconfig selects Core `12.0.0-alpha.1.26554.104`. The missing-cmake message is informational
for this managed graph, not validation of native-tool readiness.

| Selection | Passed | Failed / skipped |
|---|---:|---:|
| New `WinFormsDesktopServicesTests` (14 methods with theories) | 30 | 0 / 0 |
| `WinFormsApplicationFeaturesTests` | 33 | 0 / 0 |
| `WinFormsApplicationBuilderTests` | 24 | 0 / 0 |
| `WinFormsApplicationLifetimeTests` | 10 | 0 / 0 |
| `WinFormsApplicationRuntimeTests` | 49 | 0 / 0 |
| `WinFormsApplicationHostingTests` | 33 | 0 / 0 |
| `WinFormsSingleInstanceTests` | 21 | 0 / 0 |
| `ApplicationContextTests` | 27 | 0 / 0 |
| `ApplicationTests.Application_Exit_*` | 35 | 0 / 0 |
| **Total (30 new + 232 coupled regressions)** | **262** | **0 / 0** |

Do not double-count the single-instance suite's two child-worker executions as independent tests.
The source-subscription reentrancy test was executed **red before the stop-after-add guard**:
1 failed with `Stopped before initial read.` from `WinFormsNetworkAvailability.Start`.
The guard now skips that read and the final full feature selection passes. The initial focused
run's two activation assertions were corrected to respect existing `Unknown` on in-handler stop:
the test now queues stop after handler return; no single-instance completion policy was changed.
Other new cases were not separately executed against the pre-feature product.

The handoff's earlier `%TEMP%\WinForms14976Artifacts` directory was absent. Instead of assuming
old binaries or using the space-constrained D-drive output graph, a new task-owned
`%TEMP%\WinForms14977Artifacts` tree reused existing local `artifacts\obj`/`bin` files as incremental
inputs. **The affected product, hosting package and tests were rebuilt**, not tested from stale
outputs. BuildAssist regenerated its six Framework COM interop DLLs under the normal D-drive
AxHosts path; those were copied into the C-drive tree. The missing UnsupportedTypes dependency
was built with the following bounded command (not a cache clean or manifest change):

```powershell
$a = Join-Path $env:TEMP 'WinForms14977Artifacts'
Copy-Item artifacts\bin\AxHosts -Destination "$a\bin" -Recurse -Force
.\build.cmd -projects 'src\test\unit\*\UnsupportedTypes\UnsupportedTypes.csproj' `
    -msbuildEngine vs -verbosity minimal /p:ArtifactsDir="$a\" `
    -binaryLogName "$a\UnsupportedTypes.binlog"
```

Final behavior verification commands from the repository root:

```powershell
$a = Join-Path $env:TEMP 'WinForms14977Artifacts'
$env:DOTNET_ROOT = "$PWD\.dotnet"
$env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
.\build.cmd -projects src\test\unit\System.Windows.Forms\System.Windows.Forms.Tests.csproj `
    -verbosity minimal /p:Restore=false /p:ArtifactsDir="$a\" `
    -binaryLogName "$a\DesktopServices14977-Final.binlog"

$exe = "$a\bin\System.Windows.Forms.Tests\Debug\net12.0-windows7.0\System.Windows.Forms.Tests.exe"
foreach ($class in 'WinFormsDesktopServicesTests','WinFormsApplicationFeaturesTests',
    'WinFormsApplicationBuilderTests','WinFormsApplicationLifetimeTests',
    'WinFormsApplicationRuntimeTests','WinFormsApplicationHostingTests',
    'WinFormsSingleInstanceTests','ApplicationContextTests') {
    & $exe --filter-class "System.Windows.Forms.Tests.$class" --parallel none --report-trx `
        --results-directory "$a\TestResults\DesktopServices14977-Final-$class"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
& $exe --filter-method 'System.Windows.Forms.Tests.ApplicationTests.Application_Exit_*' `
    --parallel none --report-trx `
    --results-directory "$a\TestResults\DesktopServices14977-Final-ApplicationExit"
git --no-pager diff --check
```

After a whitespace-only outdent of the new runner methods, the affected graph was rebuilt again
with `-binaryLogName "$a\DesktopServices14977-Formatted.binlog"`: **0 warnings, 0 errors**.
The focused class was rerun on that rebuilt binary: **30 passed, 0 failed/skipped**, using:

```powershell
& $exe --filter-class System.Windows.Forms.Tests.WinFormsDesktopServicesTests --parallel none `
    --report-trx --results-directory "$a\TestResults\DesktopServices14977-Formatted"
```

The 232 regression results above use the identical functional implementation before that
whitespace-only change.

Binlogs/TRX remain in that owned C-drive tree. New/changed C# uses UTF-8 BOM/CRLF.
Only the new splash-validation resource was added to SR and its 13 generated XLF files, with
untranslated targets marked new. Local documentation links and whitespace were checked.
No new package/manifests, public API approvals, classic VB modifications, commits, PRs,
workflows, samples, benchmarks, full-suite/remote CI, or machine-network changes were made.

[issue]: https://github.com/dotnet/winforms/issues/14977
[parent]: https://github.com/dotnet/winforms/issues/14972
[inventory]: winforms-application-builder-vb-parity.md
[contracts]: winforms-application-builder-vb-csharp-contracts.md
[lifecycle]: winforms-application-builder-shared-lifecycle.md
[instance]: winforms-application-builder-single-instance.md
[splash-report]: https://github.com/dotnet/winforms/issues/4279
[splash-comment]: https://github.com/dotnet/winforms/issues/4279#issuecomment-747077908
[network-report]: https://github.com/dotnet/winforms/issues/9807
