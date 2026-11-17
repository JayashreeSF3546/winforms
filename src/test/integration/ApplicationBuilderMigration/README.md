# VB → shared model, with a native C# equivalent

Issue [dotnet/winforms#14979](https://github.com/dotnet/winforms/issues/14979):
**Create VB-to-shared-model and C# feature samples with migration guidance.**
Parent [#14972](https://github.com/dotnet/winforms/issues/14972).

## What is proposed?

Keep existing VB applications working. Let VB and C# applications choose the same application
builder and gradually adopt desktop features, without rewriting their business logic or running
two application frameworks together.

**Status: useful runnable public-prototype stages plus gated internal-feature exercises;
not full issue acceptance.** “Public” below means publicly declared in *this repository branch*,
not an approved/released NuGet API. Shared feature registration, activation, splash, information,
and network integration are still internal. These samples have **no friend access**, private
reflection, copied runtime implementation, or secret compile flag to cross that boundary.

## The simple flow

```text
Before: existing VB project → MyApplication → ApplicationEvents.vb → Form
                                      ↓ extract UI-free business logic
                       MigrationSession.Start / ProcessItem
                                      ↓ select ONE driver
Legacy selection: MyApplication.Run → Startup (may Cancel) → Form → Shutdown
Shared selection: explicit preflight (may return) → Build → deferred Form
                  → ApplicationStarted → one message loop → close
                  → stopping → UI disposal → stopped → return/dispose application
                                      ↓ opt in one feature after its public review
                         compare outcomes → retain or roll back
```

In the public stage, preflight is **your own call before Build**, not the internal `Starting`
event. A rejected preflight creates no builder, host, UI, or lifetime events. `ApplicationStarted`
is after root creation and before the pump, not cancellable startup, `Shown`, or first paint.
This sample intentionally has **no host/provider**. If needed, use the existing
[host lifetime samples](../WinFormsApplicationBuilder/README.md): supply one unbuilt host with
`UseHost`, let the application drive/dispose it, and never also run `Host.RunAsync`.

## Files and selections

| File | Purpose |
|---|---|
| `VisualBasic/ApplicationEvents.vb` | Real native `Handles Me.Startup` and `Handles Me.Shutdown`; delegates to extracted state |
| `VisualBasic/MyApplication.vb` | **Manual snapshot**, not designer output: public `WindowsFormsApplicationBase`, explicit policies and main-form factory |
| `VisualBasic/Program.vb` | STA entry point; selects `legacy` **or** `shared`, never both |
| `VisualBasic/MigrationSession.vb` | The exact same UI-free startup decision and work counter reused by both selections |
| `CSharp/Program.cs`, `MigrationSession.cs` | Equivalent native C# composition/handlers/work logic, not a call into the VB executable |
| Each `MainForm` | Native UI: JSON token display, processed-item count, event trace, close veto and close button |
| Each project | References actual public APIs in the repository-built Forms; no dependency on parity workers |
| `Directory.Build.targets` | Exports resolved payload for integration tests, without guessing sibling output paths |
| [Process tests](../UIIntegrationTests/WinFormsApplicationMigrationTests.cs) | Six real executable cases, explicit automation only |

These are repository-only source examples alongside the existing builder samples. The test-utility
project classification is build infrastructure, **not** `InternalsVisibleTo`. Neither sample
references the signed internal-feature test workers. C# has no VB application-framework driver.

### Existing VB projects are not automatically changed

Leave a working project, its `ApplicationEvents.vb`, generated `Application.Designer.vb`, `.myapp`,
`My.Settings`, providers and selected startup object unchanged while extracting logic.
No change to the old VB runtime is necessary. Keep a version-controlled rollback point.

The sample's `MyType=Empty` and explicit STA entry point allow runtime selection for this teaching
executable. They are **not instructions to change every old project** or proof that the VS project
generator supports that layout. Real generated VB defaults can differ: the runtime settings flag
defaults false, while the shipped application template enables autosave; generated authentication
and default-form-instance policies are also project choices. Our manual baseline deliberately
uses application-defined authentication, no single instance, no splash, no settings store and
main-form-close shutdown. It does not claim to model every old application's defaults.

## First launch: exact commands from the repository root

Use Windows and PowerShell 7 (preserves empty native arguments); start in
`D:\WinForms\Frameworksource\MyRepo\winforms`. Restore/build requires the pinned SDK from
`global.json` (currently `12.0.100-alpha.1.26531.102`), Core runtime selected by the manifests
(currently `12.0.0-alpha.1.26554.104`), and the repository's WindowsDesktop build prerequisites.
Run `.\Restore.cmd` if the repo SDK is missing. Do not install tools/change global caches
just to run the example. Product references supply this branch's Forms binaries; an installed
SDK's Forms alone does not contain these prototypes.
The samples' generated runtime configuration currently requests Core only; their copied project
payload supplies Forms. This environment also has WindowsDesktop `12.0.0-alpha.1.26531.102`.
Do not hand-edit runtime configuration to substitute an installed Desktop assembly.

```powershell
$env:DOTNET_ROOT = "$PWD\.dotnet"
$env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
# Reuse the C-drive graph from #14978. Another C-drive ArtifactsDir is also valid.
$a = Join-Path $env:TEMP 'WinForms14978Artifacts'
$p = 'src\test\integration\UIIntegrationTests\System.Windows.Forms.UI.IntegrationTests.csproj'
.\build.cmd -projects $p -verbosity minimal /p:ArtifactsDir="$a\" `
    -binaryLogName "$a\Migration14979-Debug-x64.binlog"
if ($LASTEXITCODE -ne 0) { throw 'Build failed; do not run stale binaries' }

$vb = "$a\bin\ApplicationBuilderMigration.VisualBasic\Debug\net12.0-windows7.0\ApplicationBuilderMigration.VisualBasic.exe"
$cs = "$a\bin\ApplicationBuilderMigration.CSharp\Debug\net12.0-windows7.0\ApplicationBuilderMigration.CSharp.exe"
& $vb legacy -- '日本語' '' 'two words' 'literal "quotes"' 'C:\space path\'
# Close the first app before selecting the next driver:
& $vb shared -- '日本語' '' 'two words' 'literal "quotes"' 'C:\space path\'
& $cs shared -- '日本語' '' 'two words' 'literal "quotes"' 'C:\space path\'
```

For just one sample, use the same `build.cmd` options with `-projects` set to
`src\test\integration\ApplicationBuilderMigration\VisualBasic\ApplicationBuilderMigration.VisualBasic.vbproj`
or the equivalent `CSharp\ApplicationBuilderMigration.CSharp.csproj`.
No plain solution `dotnet build` is needed. The displayed paths above are Debug with implicit
x64 platform. Explicit `Platform=x64` adds an `x64` directory; use the build's printed output path.
Update `net12.0-windows7.0` if the manifest target changes.

### Visual Studio debugging (F5)

Set either migration project as the startup project. Select **Shared builder** in the
launch-profile dropdown beside the Start button, then press **F5**. The checked-in profile
supplies `shared -- invoice-100.pdf`; no manual argument setup is required. The VB project
also offers **Legacy VB framework**, supplying `legacy -- invoice-100.pdf`.

C# profiles are in `CSharp/Properties/launchSettings.json`; VB profiles are in
`VisualBasic/My Project/launchSettings.json`. If Visual Studio retains an older profile,
select the named profile again or reload the project. Custom command-line launches still
require the selection and `--` separator shown above. Do not select `--probe` for manual use.

### What to click

1. A resizable Form opens and stays open. Inspect the JSON array: `""` is an empty token;
   quotes are escaped for display, not reparsed. The executable, selection, `--probe` and separator
   are not business arguments. Every token **after `--`** is retained in order.
2. Click **Process item** twice. The counter becomes 2; the native handler calls the extracted
   `ProcessItem` method. No network, settings file, database or cloud is involved.
3. Check **Veto ordinary close**, then click **Close** or the window X. It remains open and
   shows `close-vetoed`. This is a FormClosing veto, not a cancellable shutdown event.
4. Uncheck the box and click **Close**. After exit, the console prints JSON evidence with the
   counter, arguments, final events and loaded Forms identity. Legacy includes `shutdown`;
   shared includes `started`, `stopping`, `stopped`. There is no fake shared `Shutdown` API.
5. Repeat for VB shared and C# shared. No default timer or automatic close is enabled.

Startup-cancel demonstration (no Form should appear):

```powershell
& $vb legacy -- '--cancel-startup' 'retained argument'
& $vb shared -- '--cancel-startup' 'retained argument'
& $cs shared -- '--cancel-startup' 'retained argument'
```

Expect `startup,canceled,returned`, count 0. `--cancel-startup` is sample-owned exact-token
business policy, remains in the displayed arguments, and is not a builder switch.

## Incremental migration, not a rewrite

### 1. Before → extract (existing VB APIs; runnable legacy selection)

An application that previously mixed validation and controls in `ApplicationEvents.vb` first
moves just UI-free decisions/state into its own class:

```vb
' Existing VB framework event. MyApplication still owns the only message loop.
Private Sub ApplicationStartup(sender As Object, e As StartupEventArgs) Handles Me.Startup
    e.Cancel = Not _session.Start(e.CommandLine)
End Sub
```

Keep generated wiring and stores intact. Run the existing application after extraction.
The checked-in baseline also retains a native `Handles Me.Shutdown` handler; it only records an
event, because this sample has no persistence to save.

### 2. Select one entry point (existing public branch APIs; runnable shared selections)

```vb
' Imports System.Windows.Forms and Microsoft.Extensions.WinForms.
' Public prototype stage: explicit application preflight BEFORE Build/host creation.
If session.Start(session.Arguments) Then
    Dim builder = WinFormsApplication.CreateBuilder().UseApplicationContext(
        Function() New ApplicationContext(New MainForm(session, probe, "shared")))
    Using application As WinFormsApplication = builder.Build()
        AddHandler application.Lifetime.ApplicationStarted, Sub(sender, e) session.Events.Add("started")
        application.Run()
    End Using
End If
```

```csharp
// using System.Windows.Forms; using Microsoft.Extensions.WinForms;
// Same public prototype stage, composed natively in C#.
if (session.Start(session.Arguments))
{
    WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder()
        .UseApplicationContext(() => new ApplicationContext(new MainForm(session, probe)));
    using WinFormsApplication application = builder.Build();
    application.Lifetime.ApplicationStarted += (_, _) => session.Events.Add("started");
    application.Run();
}
```

Real source contains additional trace/disposal hooks; no additional runtime wrapper.
Choose the old driver **or** the builder in project-supported startup configuration;
do not call old `MyApplication.Run` inside a builder callback. Do not edit Designer output
to resolve services or hide a host/provider in a Form constructor. Parameterless designer
construction/`InitializeComponent` must remain free of service/IPC/settings I/O.

### 3. Opt in feature-by-feature (INTERNAL prototype only; public adoption blocked)

The following is an **illustration from existing friend-test composition**, not code that
can be pasted into either normal sample. It has no new public consumer project:

```vb
' INTERNAL: ApplicationBuilderParity/VisualBasic/Program.vb, after API gate only.
features.Starting =
    Sub(sender, e)
        ' Eventually map extracted validation here; final e.Cancel skips deferred UI.
        e.Cancel = scenario = "cancel"
    End Sub
features.Shutdown = Sub(sender, e) probe.Ui("shutdown")
```

```csharp
// INTERNAL: ApplicationBuilderParity/CSharp/Program.cs, after API gate only.
features.Starting = (_, e) => { e.Cancel = scenario == "cancel"; };
features.Shutdown = (_, _) => probe.Ui("shutdown");
```

The actual [VB worker](../ApplicationBuilderParity/VisualBasic/Program.vb) and
[C# worker](../ApplicationBuilderParity/CSharp/Program.cs) independently compose those handlers,
the existing host, settings callbacks, splash and network. Read them together; test probes are
not migration adapters or a supported shipping dependency. Their two narrowly signed friendships
are unchanged. Proposed adapter vocabulary in #14974 is not proof of an implemented public adapter.

Exercise both languages through the existing test harness, **not as normal consumer samples**:

```powershell
$test = "$a\bin\System.Windows.Forms.UI.IntegrationTests\Debug\net12.0-windows7.0\System.Windows.Forms.UI.IntegrationTests.exe"
& $test --filter-class System.Windows.Forms.UITests.WinFormsApplicationMigrationTests `
    --parallel none --report-trx --results-directory "$a\TestResults\Migration14979"
& $test --filter-class System.Windows.Forms.UITests.WinFormsApplicationParityTests `
    --parallel none --report-trx --results-directory "$a\TestResults\Migration14979-InternalParity"
```

The first six cases launch normal public-prototype samples with explicit `--probe`, dispatch
**Process item**, veto one close, accept another, and check final trace/count/disposal and tokens.
Cancel cases must skip UI. The 32 existing parity cases are development-only exercises:
normal/cancel/startup failure/settings failure/UI policy/background failure/network/splash gate
and stop, plus activation outcomes/path isolation. They do **not** establish public consumability.
The integration project's existing CI graph builds/copies both sets of executables; no extra
pipeline or solution reorder is needed.

For an individual internal-only lifecycle/splash exercise:

```powershell
# These test workers auto-close and emit machine evidence; they are NOT interactive samples.
foreach ($language in 'VisualBasic','CSharp') {
    $worker = "$a\bin\ApplicationBuilderParity.$language\Debug\net12.0-windows7.0\ApplicationBuilderParity.$language.exe"
    & $worker normal 'payload'
    & $worker cancel 'payload'
    & $worker splash-gate 'payload'
    & $worker splash-stop 'payload'
}
# Let the process-owning runner coordinate activation readiness, secondary launch and cleanup:
& $test --filter-method '*SameExecutable_ReportsActualDeliveryOutcome' --parallel none
```

Splash exercises use a virtual clock, not a performance measurement. Activation tests use unique
logical IDs and bounded owned-process cleanup; they do not qualify real user/session/RDP deployments.
The normal worker also exercises explicitly selected shutdown persistence. Do not duplicate its
settings callback as a new settings subsystem.

### 4. Compare, then retain or roll back

Keep the old entry-point/configuration selection until the new selection passes the app's own
tests. Roll back by selecting the retained legacy driver, **not by running both**. Do not erase,
rename, reset or overwrite settings to make rollback appear successful. Stop between feature
adoptions and record intentional differences.

## Feature mapping and compatibility checklist

Source-backed detail:
[inventory](../../../../docs/design/winforms-application-builder-vb-parity.md),
[contracts](../../../../docs/design/winforms-application-builder-vb-csharp-contracts.md),
[implemented lifecycle](../../../../docs/design/winforms-application-builder-shared-lifecycle.md),
[activation](../../../../docs/design/winforms-application-builder-single-instance.md),
[splash/services](../../../../docs/design/winforms-application-builder-splash-services.md),
[parity evidence](../../../../docs/design/winforms-application-builder-parity-testing.md).
The implementation documents supersede earlier proposed names/shapes.

| Existing VB behavior | Public runnable stage / internal gated mapping and difference |
|---|---|
| `Startup`, `e.Cancel` | VB legacy uses real event. Public shared does preflight before Build, not host-aware startup. Internal `Starting` runs after host startup/context setup, before deferred UI; canceled accepted run cleans up and emits stopping/stopped but no root/Started/feature Shutdown/save. |
| `CommandLineArgs` | Pass raw tokens, executable excluded. No join/split/quote reconstruction or implicit host configuration parsing. Forwarded arguments are separate from initial arguments. |
| `Shutdown` | Legacy event is noncancellable after normal Run return/context restoration. Internal shared Shutdown is only eligible after normal pump return and successful readiness, with owner context still installed; failed/canceled startup is cleanup, not Shutdown. Public lifetime stopping/stopped are not replacements for successful-shutdown/save logic. |
| Form close cancellation | Ordinary `FormClosing` veto keeps the UI alive in all runnable selections. Committed shared `StopAsync`, host stop or run-token stop is not vetoable; never synchronously wait for a stop task on UI. These samples select ordinary close only. |
| Startup exception | Legacy can suppress an ordinary startup failure with `ExitApplication=False` (does not resume startup). Shared terminal `ReportFailure` cannot suppress; original failure propagates after cleanup. No equivalent public exception adapter is invented here. |
| UI/background exceptions | Reuse the existing explicitly selected exception-logging policy owner; do not install competing VB/shared handlers. No arbitrary worker/task/native-fault recovery. Host BackgroundService policy remains host policy. Sample UI callbacks intentionally do not throw test assertions across native callbacks. |
| `IsSingleInstance` / next instance | Internal explicit logical ID + version cohort + current executable path + Windows user/session. Different paths elect independently; elevated/custom-owner/impersonated tokens fail closed. No inferred legacy GUID/MVID identity, global mutex fallback, or cross-session/elevation forwarding. Focus is best effort. |
| Activation result | `NotReady` means no callback queued/executed (including splash gate); `Unknown` means effects might have happened. Do not automatically retry, launch fallback UI, or claim exactly-once. HandlerFailed may have partial effects; Handled does not guarantee foreground focus. |
| Splash | Internal factory after successful Starting, unlike legacy's earlier initialization splash. Default two-second minimum only when selected; exact `/nosplash` or `-nosplash` retains tokens. Gate starts at Show return, not paint. Main visibility waits; synchronous heavy startup still blocks and does not become responsive. Stop/failure skips remaining minimum and cleans up; stop during readiness skips feature Shutdown/save. No second pump/DoEvents workaround. |
| Info/runtime | Internal immutable selected-assembly snapshot, not an automatic `My.Application.Info` replacement. Missing assembly location stays absent; host content root is not its substitute. Retain appropriate VB/BCL services explicitly. |
| Network | Internal borrowed source and owner-UI changed-state projection after root visibility; no artificial initial event, transients can coalesce. Availability is not Internet/DNS/backend reachability. Exercises inject a source; no adapter/firewall changes or remote probes. |
| Settings/autosave | Preserve the existing provider/store/application identity by default. `SaveMySettingsOnExit` alone is not the generated saver. Before opting in a new save owner, disable the old generated autosave through supported project/tooling choices, not generated-source edits. Exactly one save owner. |
| Default instances / `My` / AfterAllFormsClose | Not automatically reproduced by the builder. Keep legacy selection where required; review explicit context policy before migrating. Culture/principal/logging/resource/deployment behavior is not silently translated. |

### Explicit settings migration ownership (no automatic conversion)

This example has **no settings store**, so no data is touched. For an actual application, record
all of the following before selecting save-on-exit:

1. Existing provider and full store location/key; legacy assembly/application identity and version.
2. Chosen existing typed-service **application ID and store path** if changing models. Do not derive
   a new ID from the sample's name or silently change the legacy identity.
3. Import/schema/version mapping, conflict/backup/rollback policy and explicit consent. No implicit
   import/reset/rename/delete. Test with an app-owned temporary copy before touching user data.
4. One selected save owner. Keep legacy store/providers and explicit `My.Settings.Save()` if
   retaining that model; otherwise disable generated autosave before enabling shared save.
5. Internal shutdown snapshot preparation is synchronous on owner STA; returned persistence is
   UI-free and joined after UI teardown, before host stop/provider disposal. No async-void save,
   UI dispatch wait, recursive stop wait, or automatic save after canceled/failed startup.
   Save failures propagate; forced kill cannot guarantee persistence.

## Troubleshooting and remaining gates

- **Missing builder member:** use project references/build output from this branch, not an installed
  Desktop reference. `DisableTransitiveFrameworkReferences` prevents a hosting dependency from
  substituting framework Forms. Internal feature calls failing to compile in normal samples are
  an intentional boundary; do not add friendship, reflection or source copies.
- **Framework not found:** set both DOTNET_ROOT variables above and compare
  `.\.dotnet\dotnet.exe --list-runtimes` with the generated `.runtimeconfig.json`. Use the repository
  SDK/runtime setup; do not “fix” it by switching to arbitrary installed Forms.
- **Missing generated build prerequisite:** preserve existing artifacts; build the named prerequisite
  reported by Arcade rather than broad clean or running stale binaries. See repository
  [build guidance](../../../../CONTRIBUTING.md).
- **Empty argument missing:** use PowerShell 7's standard argument passing; the tests use
  `ProcessStartInfo.ArgumentList`, not a command-line string. JSON escaping does not change tokens.
- **No window:** cancellation intentionally skips UI. Test workers/`--probe` auto-close; omit
  `--probe` for manual use. If the veto is checked, uncheck it before closing.
- **IPC NotRun:** a supported non-impersonated, user-owned token is required. Do not elevate, change
  machine policy or weaken checks. See parity test output for explicit skip reasons.
- **Tests:** executable xUnit v3/Microsoft.Testing.Platform `--filter-class`/`--filter-method`,
  not VSTest `--filter`; `--parallel none` avoids simultaneous sample UI interactions.

See [VALIDATION.md](VALIDATION.md) for exact executed commands/results. Remote CI, interactive VS
generator/designer validation and binary compatibility are not proven by a source baseline.
Full #14979 acceptance remains blocked on reviewed public shared-feature contracts/adapters and
packaging/reference exposure, then normal-consumer paired feature samples and stakeholder review.
Do not treat signed test access as consumer support. Real multi-user/session/RDP/ARM64/device
qualification belongs to **#14980**, benchmarking to **#14981**; neither is started here.
