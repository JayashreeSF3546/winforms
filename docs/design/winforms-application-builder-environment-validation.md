# Multi-instance and environment validation

Issue: [dotnet/winforms#14980](https://github.com/dotnet/winforms/issues/14980),
“Run multi-instance, Terminal Services, architecture, and slow-machine validation.”
Parent: [#14972](https://github.com/dotnet/winforms/issues/14972).

**Status: local executable validation plus operator handoff, not full environment acceptance.**
Real multiple-user/session, Terminal Services/RDP, native non-Apple Windows ARM and slow-device
results are required before closing this issue. A runbook is not evidence that these ran.

## What is proposed? Simple flow

The proposal asks whether launching real applications at the same time works reliably on
different Windows installations and hardware. It does not propose another hosting system.

```text
Launch test-owned copies with the same logical ID
    -> hold at an explicit stdin barrier
    -> release competing selections
    -> one owner starts host/UI; later copies forward their argument tokens
    -> check explicit results, completed UI callbacks and ownership
    -> stop, await disposal, relaunch with the same ID
    -> retain traces and record Passed / Failed / Blocked / NotRun
```

For example, two simultaneous launches at the same path/version must elect one primary.
The other can see `NotReady` during startup (no queued callback), or `Handled` after readiness
(one completed callback). Eight clients released against a ready primary must each receive
`Handled`, with eight distinct immutable payloads observed by that primary, not eight new windows.
An identical application copied to another directory must instead own a separate host/UI.
These are assertions about the tested finite run, **not an exactly-once protocol guarantee**.

## Dependencies and actual prototype restrictions

Read the [inventory](winforms-application-builder-vb-parity.md),
[contracts](winforms-application-builder-vb-csharp-contracts.md),
[lifecycle](winforms-application-builder-shared-lifecycle.md),
[single-instance design](winforms-application-builder-single-instance.md),
[parity evidence](winforms-application-builder-parity-testing.md), and
[migration guide](../../src/test/integration/ApplicationBuilderMigration/README.md).

Selection, dispatch and feature seams remain **internal**. The existing narrowly signed
native C#/VB parity workers exercise their real compiled implementations. Ordinary migration
samples do not expose IPC; successful F5/sample startup is not single-instance qualification.
No new friend access, public contract, legacy VB/default-builder change, fallback UI or retry
policy is introduced here.

The prototype requires a non-impersonated token whose owner is its user. Identity includes
logical ID, explicit version cohort, normalized executable path, real user and Windows session.
The workers normally use cohort `14978`; `ipc-validate-version` uses `14980-other`.
Different C#/VB executable paths intentionally isolate; they do not prove cross-language sharing.
Startup without a usable dispatcher returns `NotReady`. `HandlerFailed` can have partial effects.
`Stopping` means dispatch was rejected/canceled before execution; `Unavailable` is not proof of
a primary; `Rejected` is not successful forwarding. A lost/late acknowledgement after writing can
return `Unknown` even if application code ran. **Never automatically resend `Unknown` or start
fallback UI.** `Handled` does not guarantee foreground focus under Windows focus restrictions.

## Safe local reproduction (repository root)

Use the repo-pinned SDK/runtime, an ordinary supported Windows desktop token and sufficient
space on C:. The commands reuse the previous integration output without cleaning shared caches.
Do not interfere with other applications or alter the machine/session to manufacture a result.

```powershell
$a = Join-Path $env:TEMP 'WinForms14978Artifacts'
$p = 'src\test\integration\UIIntegrationTests\System.Windows.Forms.UI.IntegrationTests.csproj'
$env:DOTNET_ROOT = "$PWD\.dotnet"
$env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
.\build.cmd -projects $p -configuration Debug -verbosity minimal `
    /p:ArtifactsDir="$a\" -binaryLogName "$a\Environment14980-Debug-x64.binlog"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
.\src\test\integration\ApplicationBuilderParity\Invoke-EnvironmentValidation.ps1 `
    -ArtifactsDir $a -Configuration Debug -Platform implicit-x64
```

Explicit architecture outputs add an architecture directory. For Release x64 or native ARM64,
build and run **on the actual target Windows hardware**, substituting the architecture below.
An x64 process emulated on ARM is not a native ARM result. x86 is supplementary, not ARM proof.
The target runtime must already be installed or separately provisioned with authorization.

```powershell
$arch = 'x64' # use arm64 only on provisioned native non-Apple Windows ARM hardware
.\build.cmd -projects $p -configuration Release -verbosity minimal `
    /p:ArtifactsDir="$a\" /p:TargetArchitecture=$arch /p:Platform=$arch `
    -binaryLogName "$a\Environment14980-Release-$arch.binlog"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
.\src\test\integration\ApplicationBuilderParity\Invoke-EnvironmentValidation.ps1 `
    -ArtifactsDir $a -Configuration Release -Platform $arch
```

The runner uses xUnit v3/Microsoft.Testing.Platform, `--parallel none`, TRX and these selectors:

* `--filter-method '*NativeEnvironment_*'`: eight cases, two native languages.
* `--filter-method '*SameExecutable_ReportsActualDeliveryOutcome'` and
  `--filter-method '*DifferentExecutablePaths_AreSeparatePrimaries'`: eight existing IPC cases.
* `--filter-class System.Windows.Forms.UITests.WinFormsApplicationMigrationTests`: six existing
  public-stage/legacy migration regression cases (not IPC proxies).

Each environment case has a 30-second signal watchdog; existing process completion waits are
45 seconds. The runner caps each selector group at six minutes and kills only its owned test
process tree on timeout. No arbitrary scheduling sleep, retry-until-pass loop, machine load,
affinity/priority adjustment or global synchronization primitive is used. Unique test IDs and
redirected stdin isolate coordination. Copied payloads live in an owned temporary directory
with spaces and are deleted only after owned processes exit.

| Automated environment case, per language | Iterations / observation |
|---|---|
| Concurrent election and immediate reopen | Five same-exe pairs, same ID reused only after both exits; exactly one primary per pair; secondary `Handled` or `NotReady` |
| Rapid activation | Three ready primaries, each with eight gated clients; 24 completed, distinct, immutable callbacks |
| Copied install path | Two simultaneously live independent primaries, identical resolved payload copied to another directory |
| Explicit version cohort | Two independent primaries at the same executable path with distinct cohorts |

Primary traces assert one host start/stop/dispose, one UI activation/disposal, owner STA callbacks
and process architecture matching the runner. Secondaries must have only their result trace,
no host/UI events. Loaded Forms/VB assembly MVIDs and payload paths must match the test graph.
The existing regression selector exercises real `NotReady`/`HandlerFailed`/`Handled` responses.
Stopping, lost acknowledgement and malformed/silent transport policies remain covered by the
directly coupled existing single-instance unit tests, not newly claimed hardware scenarios:

```powershell
$u = Join-Path $env:TEMP 'WinForms14977Artifacts'
.\build.cmd -projects src\test\unit\System.Windows.Forms\System.Windows.Forms.Tests.csproj `
    -verbosity minimal /p:ArtifactsDir="$u\" -binaryLogName "$u\Environment14980-Unit.binlog"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
& "$u\bin\System.Windows.Forms.Tests\Debug\net12.0-windows7.0\System.Windows.Forms.Tests.exe" `
    --filter-class System.Windows.Forms.Tests.WinFormsSingleInstanceTests --parallel none `
    --report-trx --results-directory "$a\TestResults\Environment14980-SingleInstance"
if ($LASTEXITCODE -ne 0) { throw 'Regression failed' }
```

## Operator handoff for environments not available locally

Only an authorized operator on an already provisioned environment may run these steps.
Do not create/switch users, elevate, change RDP/Terminal Services settings, lock/disconnect
sessions, change firewall/network/power/DPI or affect unrelated apps as part of this harness.
Do not contact hardware owners or upload results automatically. The hardware-owner handoff is:
provide this guide, exact revision/source hashes, target binaries and runtime manifest through
an approved channel; request native Windows ARM and Surface Go 2 runs; ask for redacted results.
No owner name/account data is needed in reports.

### Holding workers across existing authorized sessions

Use the **same absolute install path**, same payload/revision and same operator-chosen random
logical ID in each already authorized session. Different paths would mask a broken session
boundary. Within that session use a terminal (stdin must stay open):

```powershell
$worker = "$a\bin\System.Windows.Forms.UI.IntegrationTests\Debug\net12.0-windows7.0\Parity\ApplicationBuilderParity.CSharp\ApplicationBuilderParity.CSharp.exe"
$id = '<random-id-shared-only-by-this-authorized-test>'
& $worker ipc-validate $id 'session probe'
# Wait for ARMED, enter go. An independent owner prints READY. Leave it running.
# In another terminal IN THIS SAME SESSION:
& $worker ipc-secondary $id '日本語' '' 'two words'
# Expect a JSON trace with Events=["Handled"], no host/UI events.
# Return to the primary terminal, enter stop; preserve its final JSON.
```

Repeat with the VB executable, not simultaneously as a substitute for same-path isolation.
Redirected worker orchestration can use the existing `ApplicationProcess` pattern, but do
not add normal consumer access to internal APIs. Keep a watchdog and retain only test-owned
PIDs for cleanup. If holding workers manually, close them with `stop`; use PID-scoped cleanup
only for known owned processes if they fail to exit.

| Required environment | Procedure and expected evidence |
|---|---|
| Two real Windows users | Each authorized user runs the held worker at the same path/ID/cohort while the other remains live. Both must print READY; local secondaries must be Handled by only their own primary. Record anonymized A/B boundary, no names/SIDs. |
| Same user, distinct Windows sessions | In already authorized distinct sessions repeat the held-worker procedure concurrently; expect two independent primaries, each handling only its session's secondary. A sequential logon is insufficient. |
| Terminal Services / Remote Desktop | Record actual console/remote-session fact from worker JSON, not a guessed environment variable. Repeat local burst plus overlapping held workers across authorized sessions. If reconnect is separately authorized, capture before/after session facts and new activation outcomes; this harness does not reconnect or disconnect. |
| Different install paths | Automated copied identical payload must show two independent owners; rerun from deployed authorized roots to supplement temp-path evidence. No global installation/configuration change required. |
| x64 and native non-Apple Windows ARM | Run the exact build/runner on each target; require apphost PE `8664` / `AA64`, matching actual process architecture, OS architecture, runtime config, binary hashes and loaded MVIDs. Record the native device category, not serial numbers. |
| Surface Go 2 / genuine slow device | Run the same finite cases on the real device under its ordinary approved configuration. Record device category and observed responsiveness/timeouts. A fast desktop with artificial load, constrained affinity or emulation is not equivalent. |
| Elevation/custom-owner boundary | Unsupported by current identity policy; record Blocked when encountered. Do not trigger UAC or change tokens. A skip is never a successful elevation result. |

For activation during startup, retain `NotReady` and verify no later callback appeared for that
request. During committed stop retain `Stopping`, `Unavailable` or `Unknown` as actually observed;
correlate with the owner's callback trace before interpreting the result. Do not call all these
outcomes success merely because a process exited. Handler failure must report `HandlerFailed`;
lost replies must never be treated as trustworthy completion. For focus tests, record requested
and observed focus separately without disrupting another user's application.

## Evidence and result schema

The runner creates a new `%TEMP%\WinForms14980Evidence-<guid>` outside git, containing
`matrix.json`, raw logs, TRX and runtime manifests. Machine-specific raw logs include assembly
paths; keep them private and redact account/path information before any approved sharing.
`matrix.json` records UTC, source HEAD plus dirty state and changed-source SHA256s, configuration,
SDK, worker/framework hashes and apphost PE machines. Every worker JSON records actual loaded
Forms/VB/entry-module MVIDs, paths, runtime, OS/process architecture, OS version, remote-session
fact, events and individual callback payloads. TRX/logs retain failures, not just successful exits.
Do not infer RDP qualification merely from a boolean in a single-session test run.

Use one row per actual case, with `Status` exactly `Passed`, `Failed`, `Blocked`, or `NotRun`.
`Passed` needs expected assertions and trace; `Failed` means an unexpected observation;
`Blocked` means an attempted prerequisite/policy prevents qualification; `NotRun` means no
execution. Unsupported-token xUnit skips map to `NotRun`, not `Passed`.

```json
{
  "Case": "NativeNonAppleWindowsARM64",
  "Status": "NotRun",
  "Reason": "No authorized target hardware run",
  "Evidence": null
}
```

For operator runs supplement with anonymized boundary labels, real OS/runtime/architecture,
device category, install-root alias, revision and source/binary identity, iteration/client counts,
result and callback counts, cleanup outcome, diagnostic timings and limitations. Do not publish
usernames, SIDs, account paths, session IDs or machine identifiers. The runner intentionally leaves
external rows `NotRun`; do not change them by hand to Passed without separately referenced evidence.

## Decisions and remaining acceptance

* Reuse native workers and test discovery instead of creating another application/IPC framework.
* Accept the documented startup `NotReady` branch rather than adding a startup queue or retry.
* Validate actual same-language copied payloads; a C#/VB path difference alone is weaker evidence.
* Retain a modest fixed stress count, explicit failure and bounded cleanup, rather than retrying
  races until green. Counts and durations are diagnostics, not performance or leak-free acceptance.
* The first rapid-activation run failed in both languages with `Unknown`. The listener wrote
  completion then immediately disconnected. Windows
  [discards unread data at disconnect](https://learn.microsoft.com/windows/win32/api/namedpipeapi/nf-namedpipeapi-disconnectnamedpipe).
  The narrowly coupled runtime correction adds a one-byte consumption receipt after the
  validated correlated reply; the server awaits it under the existing connection deadline/stop
  cancellation before disconnecting. No new election, dispatcher, retry or fallback is added.
  Once a client validated a full result, receipt-write failure cannot undo that knowledge.
  Missing receipts cannot hold shutdown or the listener indefinitely; two raw-client regression
  cases cover deadline and stop release. The native burst regression failed before this fix.
  This internal framing change needs protocol/compatibility review; pair matching payloads and
  explicitly chosen version cohorts in deployment. It does not repair actual crash/lost-reply
  uncertainty or promise exactly-once delivery.
* No public API, old VB behavior, sample F5 setup or pipeline gate changes are required.
  SDK auto-inclusion and the existing integration-test graph discover the new methods.
  Discovery/local execution is **not remote CI success**.
* Unavailable hardware, real cross-user/session/RDP runs and remote CI remain acceptance blockers.
  Future #14981 owns legacy/shared benchmarking; it is not started by this validation.

## Verified local outcome

The resumed Debug x64 run used the commands above, the repository-pinned SDK, and an ordinary
supported desktop token. Both affected graphs were rebuilt with `build.cmd`; neither graph
reported warnings or errors. The CMake discovery banner did not prevent these managed builds;
this is not evidence of native CMake build qualification.

| Check | Actual result |
|---|---|
| Integration graph, `build.cmd -projects $p -configuration Debug` with the artifact/log arguments above | Passed, zero warnings/errors |
| Unit graph, `build.cmd -projects src\test\unit\System.Windows.Forms\System.Windows.Forms.Tests.csproj` with the artifact/log arguments above | Passed, zero warnings/errors |
| `Invoke-EnvironmentValidation.ps1 -ArtifactsDir $a -Configuration Debug -Platform implicit-x64` | 22 passed, zero failed/skipped; seven external rows NotRun |
| Environment selector | Eight passed across native C# and VB, including finite election/reopen and activation bursts |
| Existing native IPC selector | Eight passed |
| Existing migration selector | Six passed |
| `--filter-class System.Windows.Forms.Tests.WinFormsSingleInstanceTests --parallel none` with TRX arguments above | 23 passed, zero failed/skipped, including both missing-receipt cases |

The pre-fix TRX retained failures in `NativeEnvironment_RapidActivation` for both languages:
expected `Handled`, observed `Unknown`. The post-fix runner retained passing traces, and the
resumed verification passed without a retry-until-green loop. Private evidence is outside git:
`%TEMP%\WinForms14980Evidence-fc798f499b4c4bb295808224a3aaa586\matrix.json` and sibling logs/TRX.
The resumed unit TRX is under
`%TEMP%\WinForms14978Artifacts\TestResults\Environment14980-Resumed-SingleInstance`.
Build logs are `Environment14980-Resumed-Debug-x64.binlog` in the integration artifact root and
`Environment14980-Resumed-Unit.binlog` in the unit artifact root.

This completes the available local implementation and validation, **not the full issue**.
No new Release, x86, native ARM, real cross-user/session, RDP, slow-device or remote-CI result
is claimed by this section. Protocol/compatibility review also remains required before shipping.
