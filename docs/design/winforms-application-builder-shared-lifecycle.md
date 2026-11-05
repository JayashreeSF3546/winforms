# Shared lifecycle implementation prototype

Issue: [#14975 — Implement shared startup, shutdown, exception, and command-line features](https://github.com/dotnet/winforms/issues/14975).
Parent: [#14972](https://github.com/dotnet/winforms/issues/14972).

Status: **internal opt-in runtime prototype, not an approved public API or complete parity sign-off**.
The [#14973 inventory](winforms-application-builder-vb-parity.md) and
[#14974 contract proposal](winforms-application-builder-vb-csharp-contracts.md) remain the
compatibility/design references. Their proposed public names are not shipping contracts.

## Requirement in simple terms

VB already allows an application to inspect its arguments and cancel startup, perform shutdown
work, and select exception/settings policies. C# and new VB applications should be able to share
that work through the existing Application Builder host. They must not run a second VB application
framework or a second message loop.

The important distinction is **when** each callback runs:

- `Starting` is before deferred root UI creation. It can cancel startup.
- Existing `ApplicationStarted` is after root UI creation. It cannot provide that guarantee.
- `Shutdown` is for an eligible normal root-pump return. Failed/canceled startup is cleanup, not
  successful shutdown.
- Existing `ApplicationStopping`/`ApplicationStopped` still cover accepted runs that fail or cancel.
- Settings save is explicitly selected work, not an automatic new store or a generated VB autosave.

## Implemented code flow

```text
ConfigureApplicationFeatures (internal; optional)
  -> Build: copy original argument tokens and callbacks
  -> Run: existing preflight and optional host startup off UI
  -> install existing owner-thread WinForms synchronization context
  -> Starting (sender = built application; synchronous STA)
       Cancel / committed stop?
         yes -> skip UI activation, ApplicationStarted, root pump, Shutdown and save
                -> existing stopping -> cleanup -> drain host -> stopped -> return
         no  -> existing deferred root activation -> ApplicationStarted -> one existing root pump
  -> normal root-pump return:
       existing stopping -> Shutdown once -> optional settings snapshot preparation on STA
  -> existing UI/context/scope teardown and synchronization-context restoration
  -> release UI completion barrier
  -> join selected UI-free persistence using still-live borrowed services
  -> existing coordinated host stop -> ApplicationStopped -> provider disposal
  -> return, or propagate retained failure(s)/accepted run-token cancellation
```

The root-pump return seam is in `Application.ThreadContext.RunMessageLoopInner`, **before** its
`finally` uninstalls the synchronization context and disposes root resources. Putting this callback
after `Application.Run` in the runner would be too late. Both existing engines can also call
`DisposeThreadWindows` when consuming WM_QUIT before returning. Only for this opt-in feature,
`DeferThreadWindowDisposal` leaves that release to the existing finally after callback completion.
Escaping failures and outer runner cleanup also release the deferral. No second disposal owner or
pump is introduced. Message-loop bookkeeping can still report
an active loop at this seam; callbacks must not pump or wait for further UI dispatch.

`WinFormsApplication.CompleteFeatureShutdown` makes feature shutdown once-only. Stopping can
already have occurred for a committed stop; the existing lifetime remains once-only too.
An ordinary `FormClosing` veto does not reach this seam. A committed application/host/token stop
still uses the existing nonvetoable shutdown path. A Form that has already closed can already be
disposed; snapshot preparation must not assume every Form remains alive.

## Internal composition surface

`WinFormsApplicationBuilder.ConfigureApplicationFeatures` is **internal**, like the branch's
pending-review scoped activation prototypes. It rejects duplicate registration and observes the
existing owner-thread/single-use builder checks. Successful build retains its own copy; subsequent
mutations of a retained configuration object have no effect on the application.

`WinFormsApplicationFeatures` contains:

| Member | Purpose and limit |
|---|---|
| `CommandLine` | Caller-supplied argument tokens excluding the executable. Omitted input snapshots `Environment.GetCommandLineArgs().Skip(1)` at Build. Read-only private array copy, preserving order, spaces, empty tokens and Unicode; null elements rejected. |
| `Starting` | Synchronous ordinary multicast callback. Registration order; first failure ends dispatch. Final mutable `Cancel` value wins after successful handlers, including a later handler clearing it. No retry or async-void startup. |
| `Shutdown` | Synchronous ordinary multicast callback on normal root-pump return, without a prior retained failure. No cancellation. First handler failure prevents later handlers and save preparation but not cleanup. |
| `ReportFailure` | Bounded **terminal failure reporting**, not the proposal's full exception-decision API. Reports observed startup/managed root-loop/shutdown callback failures on owner STA, retaining the original before any reporter failure. It cannot suppress, continue or retry. |
| `PrepareSettingsSave` | Explicit STA snapshot preparation returning a UI-free `Func<Task>`. Borrow the selected existing settings service or application-owned legacy persistence operation; do not transfer service ownership. Null returned operations are failures. Disabled by default. |

`WinFormsStartingEventArgs.CommandLine` exposes a `ReadOnlyCollection<string>` and inherited
`Cancel`. `WinFormsShutdownEventArgs.Reason` is `UserExit`, `RequestedStop`, or
`RunTokenCancellation`. The first recorded committed request wins; normal stopping/host echoes
do not replace its reason. Reasons describe shutdown, not whether Run ultimately throws.

No arguments are joined, reparsed or passed to host configuration. The existing
`ConfigureWinFormsConfiguration` remains a separate explicit choice. This avoids breaking file
paths and turning raw UI arguments into configuration keys.

## Settings ordering and ownership

Snapshot preparation runs only after successful eligible Shutdown handlers. The selected operation
is started once on a worker without a UI synchronization context, after UI teardown. Both runner
cleanup and `WinFormsApplicationHost.StopCoreAsync` join the same task before the inner host stops.
The adapter gate also covers host-origin stop racing the runner. Services remain available until
the operation finishes; the feature never disposes them.

Use the existing `IUserSettingsService<T>.SaveAsync` for typed persistence; no storage abstraction,
second provider, settings format, import policy, or implicit legacy conversion is added.
`UserSettingsCommit.NotificationCompletion` remains a separately selected observation obligation,
not part of persistence by definition; never await a notification from its own Changed handler.
Migrating consumers must disable their old generated autosave writer before selecting another.

UI-bound asynchronous cleanup is **not** supported by this seam. No async-void event handlers,
blocking UI continuations, async-only UI resource disposal, or extra shutdown pump are introduced.
The synchronous runner joins UI-free persistence only after UI teardown, just as it already joins
UI-free host teardown. Persistence must not await application/host StopAsync, UI dispatch, or its
own completion notification recursively. It must terminate; no new timeout/abandonment policy is
invented. Final ordering relative to host lifecycle participants still needs Stream A/C review.

Ordinary save failure is recorded once and does not prevent host stop or provider disposal. It is
not reported as an owner-thread lifecycle callback after UI teardown. Multiple ordinary failures
use existing ordered aggregation; cancellation never replaces an earlier failure.

## Exception boundaries

`RecordMessageLoopFailure` retains an escaping managed root-loop/startup-visibility failure before
the loop's teardown records cleanup failures. The outer runner recognizes that same failure and
does not report or record it twice. This early recording is opt-in; default builder error behavior
is unchanged.

UI dispatch continues to use **the existing `WinFormsExceptionLogging.Attach` policy owner**.
No new ThreadException/AppDomain/task subscription is added. A focused composition test verifies
that a recoverable exception explicitly routed through WinForms can be handled by that adapter
and still reach normal Shutdown.

The prototype does not implement the proposed `WinFormsExceptionDecision` recovery surface.
Native callback fatal failures, arbitrary worker failures, fail-fast, corrupted-state failures and
unobserved tasks are not made recoverable. Generic Host background-service policy is unchanged.
Build failure before an application is acquired remains a caller failure, not a lifecycle event.
Cleanup failures and persistence failures propagate through existing runner aggregation.

## Preserved behavior and deferred decisions

- No public API declarations/approval files or classic VB runtime files change.
- A builder with no feature registration retains its current UI/lifetime/host behavior.
- No new host, DI/configuration/logging/settings framework, runner or pump is added.
- There is no new asynchronous UI initialization/shutdown contract on this branch to reuse.
  Async host startup/stop and UI-free save remain joined by their existing coordination model.
- Automatic VB `ShutdownStyle.AfterAllFormsClose`, language event projections and legacy exception
  suppression are not silently invented. Existing explicitly selected ApplicationContext behavior
  remains available; broader parity needs stakeholder decisions.
- No single-instance/IPC (#14976), splash/services (#14977), cross-language parity suite (#14978),
  migration samples (#14979), platform validation (#14980) or benchmarks (#14981) are implemented.

### Remaining acceptance gates

1. VB/WinForms stakeholder compatibility review and API review of #14974 and the internal seams.
2. Stream A/C agreement on final settings stop/timeout ordering and any unified exception-decision
   adapter. `ReportFailure` is deliberately narrower than that unresolved proposal.
3. Reviewed language-consumable public surface/adapters before external C#/VB migration is claimed.
4. Remote CI execution; local validation is not CI success.

The feasible bounded implementation can be reviewed independently, but **full approved shared-feature
acceptance is not claimed** while these gates remain open.

## Focused validation

`WinFormsApplicationFeaturesTests` exercises snapshot isolation and raw/config separation, startup
cancellation/final-Cancel ordering, host draining, first-failure propagation, normal shutdown reasons
and ordering, reentrant stop, async persistence joining and live-service ownership, save failures,
typed settings integration, ordinary close veto versus committed stop, transferred Form cleanup,
and composition with the existing UI exception adapter. Root-pump engine cases use both Light and
MSO component-manager paths where supported. The managed-filter failure test intentionally uses the
Light engine rather than assuming a native callback fatal failure can be caught.
Deterministically ordered stop/token requests verify first-reason precedence and that a save failure
is not replaced by cancellation.

The repository's authoritative command is `build.cmd`; tests use the executable's xUnit v3/Microsoft
Testing Platform `--filter-class` and `--parallel none`, not VSTest `--filter`. Bounded outputs use
the existing C-drive `%TEMP%\WinForms14967Artifacts` tree; no cache cleanup or project manifests
are required for this feature.

### Local results (2026-11-05, Debug x64)

Authoritative affected-graph build: **succeeded, 0 warnings, 0 errors**. It rebuilt the product,
hosting package and unit executable, rather than relying on old binaries. Initial analyzer findings
were corrected. The first behavior run exposed WM_QUIT's early disposal; the opt-in deferral above
fixed it. A diagnostic-completion test also needed to dispose the existing logging subscription
before joining its documented completion task.

| Test selection | Passed | Failed / skipped |
|---|---:|---:|
| New `WinFormsApplicationFeaturesTests` (13 methods, including theories) | 33 | 0 / 0 |
| `WinFormsApplicationBuilderTests` | 24 | 0 / 0 |
| `WinFormsApplicationLifetimeTests` | 10 | 0 / 0 |
| `WinFormsApplicationRuntimeTests` | 49 | 0 / 0 |
| `WinFormsApplicationHostingTests` | 33 | 0 / 0 |
| `WinFormsUiScopeTests` | 34 | 0 / 0 |
| `WinFormsServiceActivationTests` | 18 | 0 / 0 |
| `WinFormsServiceIntegrationTests` | 6 | 0 / 0 |
| `WinFormsExceptionLoggingTests` | 19 | 0 / 0 |
| `WinFormsUserSettingsTests` | 26 | 0 / 0 |
| `WinFormsConfigurationTests` | 27 | 0 / 0 |
| Legacy `ApplicationContextTests` | 27 | 0 / 0 |
| Legacy `ApplicationTests.Application_Exit_*` | 35 | 0 / 0 |
| **Total (33 new + 308 regressions)** | **341** | **0 / 0** |

Commands used from repository root (regression classes ran serially, each in its own process):

```powershell
$env:DOTNET_ROOT = "$PWD\.dotnet"
$env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
.\build.cmd -projects src\test\unit\System.Windows.Forms\System.Windows.Forms.Tests.csproj `
    -verbosity minimal /p:Restore=false /p:ArtifactsDir="$env:TEMP\WinForms14967Artifacts\" `
    -binaryLogName "$env:TEMP\WinForms14967Artifacts\VbLifecycle14975.binlog"

$exe = "$env:TEMP\WinForms14967Artifacts\bin\System.Windows.Forms.Tests\Debug\net12.0-windows7.0\System.Windows.Forms.Tests.exe"
& $exe --filter-class System.Windows.Forms.Tests.WinFormsApplicationFeaturesTests `
    --parallel none --report-trx `
    --results-directory "$env:TEMP\WinForms14967Artifacts\TestResults\VbLifecycle14975-Verified"

$classes = @(
    'WinFormsApplicationBuilderTests', 'WinFormsApplicationLifetimeTests',
    'WinFormsApplicationRuntimeTests', 'WinFormsApplicationHostingTests',
    'WinFormsUiScopeTests', 'WinFormsServiceActivationTests',
    'WinFormsServiceIntegrationTests', 'WinFormsExceptionLoggingTests',
    'WinFormsUserSettingsTests', 'WinFormsConfigurationTests'
)
foreach ($class in $classes) {
    & $exe --filter-class "System.Windows.Forms.Tests.$class" --parallel none --report-trx `
        --results-directory "$env:TEMP\WinForms14967Artifacts\TestResults\VbLifecycle14975-Regressions\$class"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
& $exe --filter-class System.Windows.Forms.Tests.ApplicationContextTests --parallel none --report-trx `
    --results-directory "$env:TEMP\WinForms14967Artifacts\TestResults\VbLifecycle14975-Regressions\ApplicationContextTests"
& $exe --filter-method 'System.Windows.Forms.Tests.ApplicationTests.Application_Exit_*' `
    --parallel none --report-trx `
    --results-directory "$env:TEMP\WinForms14967Artifacts\TestResults\VbLifecycle14975-Regressions\ApplicationExit"
git --no-pager diff --check
```

The current manifest-selected build automatically bootstrapped a missing repository-local SDK
`12.0.100-alpha.1.26531.102` and runtime toolsets `12.0.0-alpha.1.26554.104`.
The rebuilt executable's runtimeconfig targets that Core runtime, not the older preexisting
`12.0.0-alpha.1.26551.101`. No manifest edits or manual SDK/package installation were performed.
The build printed an informational missing-cmake native-tools message but completed the selected
managed graph successfully. Generated XLF updates contain only the new duplicate-registration
resource, with untranslated targets marked new. Changed C# files use UTF-8 BOM and CRLF.

No full solution/suite, remote CI, external language-consumption tests, samples, benchmarks or
multi-session validation were run; they are not results of this bounded child implementation.
