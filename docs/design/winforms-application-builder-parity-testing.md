# Cross-language parity and compatibility tests

Issue: [#14978](https://github.com/dotnet/winforms/issues/14978),
“Create cross-language parity and compatibility tests and run them in CI.”
Parent: [#14972](https://github.com/dotnet/winforms/issues/14972).

Status: **executable local coverage and existing CI-graph integration; not complete CI,
API-consumption, binary-compatibility, or multi-environment acceptance**.

## What is proposed? A beginner's flow

VB already has application startup/shutdown events and other desktop services. The shared
Application Builder should offer the agreed capabilities to both VB and C#, without removing
the old VB framework. This issue adds executable evidence, not another application framework.

```text
Test runner chooses a scenario and its expected contract
    -> starts a dedicated native VB or C# STA executable
    -> executable composes the compiled legacy or shared framework
    -> callbacks record order, owner thread, arguments and cleanup
    -> runner checks evidence against the scenario's contract
    -> xUnit/Microsoft.Testing.Platform reports results
    -> the existing Windows integration CI leg discovers the same tests
```

For activation, the runner starts an owned primary, waits for an explicit READY signal,
starts a secondary, verifies the result, then releases the primary through its private stdin.
A successful exit alone is not proof of parity.

## Decisions and access boundaries

The [inventory](winforms-application-builder-vb-parity.md),
[contracts](winforms-application-builder-vb-csharp-contracts.md),
[lifecycle](winforms-application-builder-shared-lifecycle.md),
[single-instance](winforms-application-builder-single-instance.md), and
[splash/services](winforms-application-builder-splash-services.md) documents define the contracts.
Tests compare only equivalent guarantees, not identical legacy/shared traces.

- Both workers have their own native `RunShared`, feature delegates, host registration,
  lifetime handlers, and `RunIpc`. VB is not an entry point forwarding application behavior
  into a C# implementation. The C# executable also exports **test-only** trace, clock,
  injected network, and host-stimulus helpers reused by VB.
- Two signed, specifically named `InternalsVisibleTo` entries in System.Windows.Forms
  permit these test workers to compose the existing internal prototypes. No product
  public API, VB implementation, existing VB delegate, or public approval file changes.
  Friend access is not supported consumer availability. Public VB/C# adoption remains
  gated on API review, packaging/reference-assembly availability, and compatibility review.
- VB's `ApplicationEvents.vb` uses real `Handles Me.Startup`, `Shutdown`, and
  `UnhandledException` wiring in a partial `WindowsFormsApplicationBase` subclass.
  `MyApplication.Designer.vb` is a checked-in generated-shape fixture: this is **not**
  evidence of running the VS generator/designer. C# independently consumes the same
  unchanged public VB framework as a baseline.
- Each worker reports the loaded Forms and VB Forms MVIDs and assembly paths. The runner
  requires the same MVIDs as its compiled project-reference graph and worker-local paths.
  `DisableTransitiveFrameworkReferences` prevents the hosting project's Desktop reference
  from substituting an installed Forms reference for the in-development assembly.
- Reflection-driven application composition, copied product source, new public prototype
  contracts, broader sample friendship, and a second lifecycle engine were rejected.
  Sharing only test stimuli avoids duplicating the runtime while keeping native composition.

## Scenario matrix

`NativeLanguage_ObeysItsModelContract` has 12 scenarios per language.
The activation methods add eight cases: **32 unique runner cases**, not a count of child
processes. They use 40 worker invocations per full execution; secondary workers are not
additional tests.

| Model / scenario | Cases | Required evidence / intentional difference |
|---|---:|---|
| Legacy normal | 2 | Startup → main activation → Shown → Shutdown → return, owner STA; native VB Handles wiring and independent C# events |
| Legacy cancel | 2 | Startup Cancel skips main activation and Shutdown |
| Legacy startup failure | 2 | Original exception reaches VB UnhandledException; ExitApplication=False suppresses it, without resuming startup or delivering Shutdown |
| Shared normal | 2 | Host start → pre-pump Starting → deferred activation → Started; eligible Shutdown → prepare → UI-free async save → host stop → Stopped → provider disposal |
| Shared cancel | 2 | No root activation/window, Started, feature Shutdown or save; host stops/disposes exactly once |
| Shared startup failure | 2 | Original reference reported as Startup on owner STA, host drained/disposed, same failure rethrown after cleanup |
| Shared settings failure | 2 | Prepare on UI, save only after Form disposal, failed save skips “saved”; host still stops/disposes and original failure escapes |
| Shared UI exception | 2 | Managed `Application.OnThreadException` stimulus routes original reference to native-language policy through existing `WinFormsExceptionLogging`; normal shutdown remains eligible |
| Shared background failure | 2 | Controlled BackgroundService failure after Shown uses Generic Host's StopHost behavior, drains normally, is not converted into a UI exception or a lifecycle MessageLoop report |
| Shared network | 2 | Injected source invalidates from Task.Run; availability callback executes on owner STA; subscription count returns to zero |
| Shared splash gate | 2 | Splash visible/main hidden after Started; one virtual second does not release a two-second minimum; second invalidation releases it; splash disposed before main Shown, timer disposed on cleanup |
| Shared splash stop | 2 | Stop while gated: main never Shown, no eligible feature Shutdown/save; host and splash/timer still cleaned up |
| Same-path activation | 6 | In each language, primary/secondary processes yield Handled, HandlerFailed, or explicit NotReady; immutable forwarded tokens verified for delivered requests |
| Different-path boundary | 2 | VB→C# and C#→VB elect separate primaries under the same logical ID; this is intentional executable-path isolation, not a cross-language activation failure |

Every lifecycle case verifies Unicode, an empty token, spaces, literal quotes, and a trailing
backslash through `ProcessStartInfo.ArgumentList`. Event collections reject mutation. Shared
cases additionally mutate the original array after Build and require the original snapshot.
Expected traces/partial orders live in test metadata, rather than treating VB's legacy exception
suppression as the required shared-model behavior.

Shared Shutdown keeps the owner synchronization context. Legacy Shutdown's earlier context
restoration is specified in the lifecycle document; no assertion forces those models to match.
Shutdown has no new cancellation contract. Existing close-veto/token-stop tests remain regressions.
Stopping before splash readiness does not become an eligible settings-save shutdown merely
because ApplicationStarted already fired.

### Existing regression evidence, not duplicated native suites

The selected existing classes remain responsible for their deeper branch and failure coverage:

| Existing suite | Responsibility |
|---|---|
| `WinFormsApplicationFeaturesTests` | Final Cancel, token/close-veto ordering, sender/context, async settings joining and typed-settings integration, primary failures |
| `WinFormsDesktopServicesTests` | Splash early close/failure/cancel, virtual clock invalidations, information services, network coalescing/reentrancy/unsubscription |
| `WinFormsSingleInstanceTests` | Protocol limits/authentication guards, synthetic SID/session key separation, rapid election, rejected/stopping/unavailable/Unknown outcomes, real owned child-process handoff |
| `WinFormsExceptionLoggingTests` | UI policy/logger/fallback failures, originating-thread global diagnostics, no automatic task observation or recovery |

The new UI scenario is a safe managed routing test, **not** deliberate propagation of a CLR
exception across a native callback. Owner-callback assertions are contained by `Probe.Guard`;
their failure is reported after cleanup rather than opening a native error dialog.
Arbitrary background exceptions do not become recoverable UI events. Fail-fast, native faults,
debugger behavior, and terminating CLR notifications are not tested as recoverable failures.

Settings tests exercise explicit borrowed persistence callbacks, not a new settings subsystem.
Legacy `My.Settings` generation and SaveMySettingsOnExit wiring are not equivalent to those
callbacks and are not claimed as automatically migrated. No shared settings file is used here.

## CI registration and provisioning

The existing `System.Windows.Forms.UI.IntegrationTests` project is already in `Winforms.sln`;
evaluated `IsTestProject=true`, `IsIntegrationTestProject=true`, and signing key
`MicrosoftShared` were verified. New source is discovered by SDK compile globs.
Two `ReferenceOutputAssembly=false` worker references build the native executables.
`GetParityPayload`/`CopyParityWorkers` copy the resolved runtime payloads under
`$(TargetDir)Parity/<project>/`, including dependency assemblies and runtime configuration.
They do not infer sibling output paths, TFM, architecture or configuration.

No pipeline YAML changes or new workload gate are necessary for the existing integration graph.
`eng/pipelines/build-PR.yml` already runs `eng/cibuild.cmd -integrationTest` for public
Windows x64/x86 Debug/Release legs when skipTests is false, with `/m:1` and its existing
test-filter parameters. ARM64 and internal test execution remain excluded by that existing
pipeline's conditions; this work does not silently enable them. Build graph evaluation,
actual payload copying and local executable discovery are not remote pipeline execution.

Requirements: Windows desktop-capable test agent, pinned preview SDK/Core runtime for the
requested architecture, project-built Forms payload, and local named pipes. IPC tests require
a non-impersonated user-owned token. Unsupported token ownership/elevation is explicitly
reported as **NotRun via dynamic skip**, not silently passed; provision a normal user.
No accounts, sessions, privileges, network adapters, machine policy, theme, or firewall are changed.
Tests are collection-serialized, use unique logical IPC IDs, and have readiness/exit watchdogs.
Cleanup terminates only Process objects created by the runner, never by executable name.
Clock advances/signals establish timing/order; timeouts only bound hangs.

## Local execution and results

Artifacts are task-owned under `%TEMP%\WinForms14978Artifacts`, on C:, preserving the existing
D-drive graph. The existing `%TEMP%\WinForms14977Artifacts` unit graph is reused and rebuilt
for regression verification to preserve its AxHosts/UnsupportedTypes prerequisites.
SDK: `12.0.100-alpha.1.26531.102`; Core x64/x86: `12.0.0-alpha.1.26554.104`.

The initial 28-case run passed 24 cases. Four incorrect **test expectations** were corrected
in both languages: terminal startup failure disposes the host before rethrow; splash-gated
stop skips feature Shutdown/save. Neither change altered runtime behavior. The subsequent
expanded Debug x64 run passed **32/32**, including UI/background exception policy.
Build iterations also corrected nullable/analyzer diagnostics, partial VB XML comments,
payload metadata, and the hosting reference's platform/installed-reference selection.

Final local results (2026-11-10; affected graphs built through `build.cmd`, each with
**0 warnings / 0 errors**):

| Selection | Passed | Failed / skipped | Evidence below `WinForms14978Artifacts` |
|---|---:|---:|---|
| Parity Debug x64 | 32 | 0 / 0 | `TestResults/Parity-Final-Debug-x64` |
| Parity Release x64 | 32 | 0 / 0 | `TestResults/Parity-Final-Release-x64` |
| Parity Debug x86 | 32 | 0 / 0 | `TestResults/Parity-Final-Debug-x86` |
| Parity Release x86 | 32 | 0 / 0 | `TestResults/Parity-Release-x86` |
| Local Arcade `-integrationTest`, filtered registered class | 32 | 0 / 0 | `TestResults/Debug`, `Parity14978-IntegrationEntry.binlog` |
| Existing lifecycle features regression | 33 | 0 / 0 | `TestResults/Regression-WinFormsApplicationFeaturesTests` |
| Existing desktop services regression | 30 | 0 / 0 | `TestResults/Regression-WinFormsDesktopServicesTests` |
| Existing single-instance regression | 21 | 0 / 0 | `TestResults/Regression-WinFormsSingleInstanceTests` |
| Existing exception logging regression | 19 | 0 / 0 | `TestResults/Regression-WinFormsExceptionLoggingTests` |

These are **32 distinct new cases** (16 per primary language: 3 legacy + 9 shared +
3 same-path IPC + 1 distinct-path boundary), exercised in four configurations, and
**103 existing regression cases**. The registration rerun and initial/rerun results are
not additional unique tests. In particular, two one-test child-worker summaries printed
inside the existing IPC suite are not added to its 21 parent results.
All eight worker apphost PE headers were checked: x64=`8664`, x86=`014C` for both
languages/configurations. Per-case loaded assembly MVID/path assertions also passed.
Changed MSBuild XML parsed successfully; source whitespace verification passed with
`dotnet format whitespace --folder --include <the six new C#/VB files> --verify-no-changes`.
New C#/VB files were checked for UTF-8 BOM and CRLF, and `git diff --check` passed.

Reproduction commands (repository root; explicit platform adds an architecture directory):

```powershell
$a = Join-Path $env:TEMP 'WinForms14978Artifacts'
$p = 'src\test\integration\UIIntegrationTests\System.Windows.Forms.UI.IntegrationTests.csproj'
$env:DOTNET_ROOT = "$PWD\.dotnet"
$env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
$env:DOTNET_ROOT_X86 = "$PWD\.dotnet\x86"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
foreach ($arch in 'x64','x86') {
    foreach ($config in 'Debug','Release') {
        .\build.cmd -projects $p -configuration $config -verbosity minimal `
            /p:ArtifactsDir="$a\" /p:TargetArchitecture=$arch /p:Platform=$arch `
            -binaryLogName "$a\Parity14978-$config-$arch.binlog"
        if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
        $exe = "$a\bin\System.Windows.Forms.UI.IntegrationTests\$arch\$config\net12.0-windows7.0\System.Windows.Forms.UI.IntegrationTests.exe"
        & $exe --filter-class System.Windows.Forms.UITests.WinFormsApplicationParityTests `
            --parallel none --report-trx --results-directory "$a\TestResults\Parity-$config-$arch"
        if ($LASTEXITCODE -ne 0) { throw 'Parity failed' }
    }
}
```

The earlier implicit-platform Debug x64 output omits `\x64` in the path. Do not copy that
assumption into explicit-platform commands. Build logs print the resolved path.
Final Debug x64 used implicit platform; final Release x64 and both x86 legs used explicit
`TargetArchitecture`/`Platform`. Release x86 used `Parity14978-Release-x86.binlog`; the
other final legs used `Parity14978-Final-<configuration>-<architecture>.binlog`.

The actual additional registration/regression commands were:

```powershell
# Uses the existing Arcade integration target, not a second custom test runner.
# An environment property avoids nested cmd.exe/PowerShell quoting of a spaced value.
$env:TestRunnerAdditionalArguments =
    '--filter-class System.Windows.Forms.UITests.WinFormsApplicationParityTests --parallel none'
.\build.cmd -projects $p -integrationTest -verbosity minimal /p:ArtifactsDir="$a\" `
    -binaryLogName "$a\Parity14978-IntegrationEntry.binlog"
Remove-Item Env:\TestRunnerAdditionalArguments

$u = Join-Path $env:TEMP 'WinForms14977Artifacts'
.\build.cmd -projects src\test\unit\System.Windows.Forms\System.Windows.Forms.Tests.csproj `
    -verbosity minimal /p:ArtifactsDir="$u\" -binaryLogName "$u\Parity14978-Regressions.binlog"
$unit = "$u\bin\System.Windows.Forms.Tests\Debug\net12.0-windows7.0\System.Windows.Forms.Tests.exe"
foreach ($class in 'WinFormsApplicationFeaturesTests','WinFormsDesktopServicesTests',
    'WinFormsSingleInstanceTests','WinFormsExceptionLoggingTests') {
    & $unit --filter-class "System.Windows.Forms.Tests.$class" --parallel none --report-trx `
        --results-directory "$a\TestResults\Regression-$class"
    if ($LASTEXITCODE -ne 0) { throw 'Regression failed' }
}
git --no-pager diff --check
```

An initial local registration command lost quoting around a spaced `/p:` argument
and failed with MSB1008 before test execution; setting the same property through the
environment corrected the invocation. No pipeline parameter syntax was changed.
Arcade reports a pre-existing “cmake not found on path” native-tool discovery message;
the affected managed graphs do not require CMake. No SDK/tool/cache reset was performed.

## Unexecuted acceptance and review gates

| Gate | Actual status |
|---|---|
| Real same-user/same-session IPC, readiness/failure and distinct executable paths | Executed locally, not multi-session certification |
| Synthetic user/session boundary keys | Existing regression selection; not real identity authentication qualification |
| Different real users, simultaneous Windows sessions, RDP/Terminal Services, elevation boundaries | **NotRun**; requires provisioned isolated environments and #14980 qualification |
| ARM64 / Surface Go 2 | **NotRun**, no matching hardware qualification |
| Previously compiled consumer against old VB framework binaries, VS generated tooling/designer | **NotRun**; old source and public contracts are untouched, but current-source/current-framework tests are not binary-backcompat proof |
| Supported non-friend VB/C# public feature consumption | Blocked on API/reference/package review; internal prototypes remain internal |
| Remote CI | **Not executed** in this session; no authenticated run/green CI evidence claimed |
| Full solution/all UI tests, native exception crashes, benchmarks | Not executed; outside focused verification |

Review requirements: VB/WinForms owners confirm the expected differences, API reviewers confirm
consumer availability, and CI owners verify provisioned agents and actual remote results before
closing all #14978 acceptance. #14979 migration samples are the next stream issue only after
explicit instruction and with those API limitations acknowledged. No samples, environment
qualification infrastructure, benchmarks, commits, or PRs are introduced by this work.
