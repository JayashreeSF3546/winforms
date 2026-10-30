# Application Builder DI/scopes prototypes

These C# and VB samples belong to dotnet/winforms#14953. They exercise the **actual internal**
scope implementation through explicit sample-only friend-assembly grants. They are repository-only
prototypes, **not an approved public API or copy-ready external application template**.
No reflection, replacement scope coordinator, ambient provider, or generated service hookup is used.

## Build and run

From the repository root, build both samples and their smoke tests with Arcade:

```powershell
.\build.cmd -projects src\test\integration\UIIntegrationTests\System.Windows.Forms.UI.IntegrationTests.csproj -verbosity minimal
```

Run either language using the repository SDK:

```powershell
$env:DOTNET_ROOT=Join-Path $PWD '.dotnet'
.\.dotnet\dotnet.exe run --no-build --project src\test\integration\ApplicationBuilderScopes\CSharp\ApplicationBuilderScopes.CSharp.csproj -- --light
.\.dotnet\dotnet.exe run --no-build --project src\test\integration\ApplicationBuilderScopes\VisualBasic\ApplicationBuilderScopes.VisualBasic.vbproj -- --light
```

Options: `--classic`, `--light`, `--dark`, `--component-manager`, `--smoke`,
and `--cancel` (the last option requires `--smoke`). Classic omits visual-style initialization.
Light enables visual styles with Classic color mode; Dark enables visual styles and Dark color mode.
Color flags do not change the process's OS theme.

The console displays architecture, Terminal Services status, scope identities, typed assignment,
and component/UserControl/Form/service disposal. The executable intentionally keeps console output
available instead of requiring a debugger to see diagnostics.

## Interactive scenarios

- **Open modeless** creates an independently scoped form with an explicit logical owner.
  Close it and verify its session is released while the owner remains usable.
- **Open modal** creates a fresh dialog scope. Close the dialog; its UI and scope are disposed
  when the modal invocation unwinds, while the owner's services remain available.
- Open descendants from those windows, then close their owner. Logical descendant scopes
  must finish before the owner's service scope. Microsoft DI scopes themselves are flat.
- **Next page** replaces `ApplicationContext.MainForm`, shows the next page, and closes the old
  page without losing the new page's independent scope. Navigation is enabled only on root pages.
- **Cancel application** cancels `Run`, which cleans all tracked UI/scopes before reporting
  cancellation. It does not start background work or introduce another cancellation service.
- Close the last root window to complete normally. The final `PASS` line verifies that every
  resolved session was disposed exactly once; it is not a long-running leak-test result.

The form receives a typed constructor dependency. Its existing Designer-created `SessionControl`
and container-owned `SessionComponent` receive that same instance through explicit typed callbacks.
Each receiver rejects repeated initialization; the nonvisual component also rejects initialization
after disposal. No UI object stores a provider or creates its own scope.

`InitializeComponent` contains only ordinary construction and layout. Parameterless constructors
remain service-free. The service state is internal and absent from property browsing/serialization.
Diagnostics run during normal UI disposal while services are still alive. The scoped contract
remains **base-last disposal**, and scoped dependencies in this sample are synchronously disposable.
The host and application scope are owned by the builder, not by the forms.

## Automated coverage

```powershell
$env:DOTNET_ROOT=Join-Path $PWD '.dotnet'
& .\artifacts\bin\System.Windows.Forms.UI.IntegrationTests\Debug\net11.0-windows7.0\System.Windows.Forms.UI.IntegrationTests.exe `
  --filter-class 'System.Windows.Forms.UITests.WinFormsApplicationScopeSampleTests' `
  --no-progress --report-trx
```

The 24 process cases combine two languages, three color selections, two message-loop backends,
and normal/canceled completion. Each isolated process exercises modeless close, modal close,
navigation, typed child assignment, UI-before-service disposal, and exactly-once scope cleanup.
UI actions are event-driven; the process deadline is only a hang guard.
These cases are included in the existing integration-test project and public x64/x86 CI jobs.

Two additional local Designer cases construct the actual sample Forms without runtime services,
create and site their UserControl/component types through `IDesignerHost`, and reconstruct those
added objects through CodeDOM serialization. Referenced types and in-memory ResX metadata are
provided explicitly; no legacy binary serializer is enabled. These cases do not save/reload a
root source document or certify Visual Studio OOP behavior.

Local Debug/x64 result: the build command above passed with zero warnings/errors; the test command
above passed **26 cases**, with zero failures/skips. Targeted whitespace formatting verification
and `git diff --check` also passed. No remote CI or other architecture/configuration pass is claimed.

## Manual and external acceptance still required

Record versions, architecture, language, backend, source diffs, and results for each environment:

1. In the Designer team's agreed environment/harness, open the Form and UserControl without
   runtime services. Edit layout and drag/drop a control and nonvisual component. Save, close,
   reopen, rename, delete, undo, redo, and reload. Confirm parent/container relationships and
   ordinary generated code survive without service/provider/scope calls. **Actual VS OOP
   compatibility has not been certified**; these source-project prototypes do not by themselves
   establish a supported Designer project/SDK configuration.
2. Visually inspect Classic, light, and Dark Mode in both languages: labels, buttons, focus,
   resizing, DPI changes, nested windows, and cancellation.
3. Repeat interactive navigation/modal/modeless/owner-close/cancellation on native Windows ARM,
   in Terminal Services including disconnect/reconnect, and on representative slow hardware.
   Record responsiveness rather than inventing a fixed timing threshold.
4. Publish the changes and obtain CI results. Local process smoke tests are not remote CI,
   visual acceptance, or Designer-team approval.

Public API approval and Designer/OOP agreement remain dotnet/winforms#14951 gates.
## Dedicated scope stress probe (dotnet/winforms#14954)

The C# executable also hosts a development-only `--stress` mode. It is separate from the
interactive C#/VB sample and uses the existing friend grant; no new public API or friend assembly
is introduced. It exercises the shared runtime scope implementation, not language-specific
Designer generation. Run without a debugger on an otherwise quiet Windows desktop.

```powershell
.\build.cmd -projects src\test\integration\UIIntegrationTests\System.Windows.Forms.UI.IntegrationTests.csproj -configuration Release -verbosity minimal
.\src\test\integration\ApplicationBuilderScopes\MeasureScopes.ps1
```

The script launches four isolated processes: manual/scoped ownership, each with light and
component-manager message loops. Each process runs **10 warmup cycles and 1000 measured cycles**.
One cycle creates/shows/closes/disposes three Forms: a modeless parent, modeless child, and
modal grandchild. That is **3000 measured windows per process**, plus warmup. Windows appear
automatically; do not interact with them.

Raw stdout/stderr, JSON reports, checkpoint CSVs, source revision/worktree status, and a summary
are saved under `artifacts\log\ScopeStress\<timestamp>`. Override `-Cycles`, `-Warmup`, `-Interval`
or `-Configuration` as needed. The 20-minute per-process watchdog detects hangs, not acceptable
application latency. Each command-line option is also available on the executable:

```powershell
$env:DOTNET_ROOT=Join-Path $PWD '.dotnet'
& .\artifacts\bin\ApplicationBuilderScopes.CSharp\Release\net11.0\ApplicationBuilderScopes.CSharp.exe `
  --stress --mode scoped --cycles 1000 --warmup 10 --interval 100
```

Add `--component-manager` or choose `--mode manual` for the matching baseline. Stdout contains
`READY` after warmup and then a JSON report after application shutdown.

### What the probe verifies

- Every window has a fresh service scope, a scoped disposable service, two nested UserControls,
  a container-owned component, a Bitmap, and a Font.
- UI disposal must happen while the service is alive. Services/components/controls/Forms must
  complete cleanup, and a long-lived publisher must have no remaining component subscription.
  The publisher is exercised while components are live as well as after each cycle.
- Every Form must actually be shown, then disposed with its native window destroyed. Modal
  completion is posted through the dispatcher. Scoped cycles alternate explicitly closing a
  modeless child and disposing it through its owner's close.
- After collect/finalize/collect at each warmed checkpoint, all tracked cycle Forms, controls,
  components, services, scoped providers, lifetime records, Bitmaps and Fonts must be collectible.
  These are strict checks: a nonzero survivor count fails the process.
- The final report separately records whether the application, context, and coordinator are
  still weakly reachable immediately after hosted shutdown. Those are diagnostic fields, not
  silently treated as a cleanup pass.

The manual baseline uses ordinary `Show`/`ShowDialog` and explicit `IServiceScope` ownership,
with the same service/UI graph and child-before-parent cleanup. Both modes use one real host,
application scope, coordinator-owned dispatcher Form, and application message loop per process.
Only measured windows bypass the coordinator in manual mode. This isolates repeated UI-scope
turnover from repeatedly starting hosts/message loops; it is **not** a replacement for the
separate application-lifetime benchmark or its outstanding component-manager growth investigation.
The baseline is a manual reference, not a measurement of an older source revision or a claimed fix.

### Interpreting resources and throughput

The probe captures managed heap after full collection, private bytes, kernel handles, GDI objects,
and USER objects after warmup and every 100 cycles (including a final partial batch). Weak-reference
storage is allocated before warmup and holds no strong references to measured graphs. Checkpoint
report objects themselves remain until output and contribute a small, bounded instrumentation
cost. Scoped providers are tracked through the `IServiceProvider` passed to the scoped service
factory; disposal counts refer to the marker service, not an instrumented replacement DI container.

Batch elapsed time and process-wide allocated bytes exclude checkpoint collections/resource
sampling, but include actual UI creation, dispatch, teardown, and scheduling overhead. Summary
throughput is measured windows per second, not a first-frame/startup latency measurement.
Initialization and host shutdown are outside measured batches. No sleeps, timing assertions,
fixed memory thresholds, or machine-wide tuning are used.

Review **all checkpoints**, not just the final delta. Sustained memory/native-resource growth or
retained application objects requires investigation even if strict cycle cleanup passes.
JIT/runtime/native caches and unrelated threads can affect process totals; a zero survivor count
does not prove there is no native leak. Do not label a successful process or a short run
"leak-free." Repeat matching configurations and retain reports before drawing conclusions.

Four short cases in `WinFormsScopeStressTests` verify both modes/backends and checkpoint/cleanup
semantics, including a partial final batch. Existing public integration-test jobs discover them;
remote CI, native ARM, Terminal Services, Designer acceptance, and the previous stream's
resource-growth findings remain separate gates.

Local Release/x64 execution completed all four 1000-cycle processes (**12000 measured windows**,
plus 120 warmup windows). All cycle weak survivors and subscriptions were zero; every process
disposed 3030 services/components and 6060 nested UserControls. Application/context/coordinator
weak references cleared after shutdown. GDI/USER counts stayed at 45/29 across all checkpoints;
managed heap settled near 1.50-1.52 MB after early warming, and kernel handles did not accumulate.
The Release build passed with zero warnings/errors and all **30** selected stress/sample tests
passed. This workload did not reproduce the separate repeated-application growth findings.
Full commands, resource deltas, throughput, limitations, and the raw artifact location are in
`docs\design\winforms-application-builder-di-scopes.md`.
