# Multi-instance validation report

**Scope:** issue #14980.  
**Run date:** 2026-10-23.  
**Result:** local Windows x64 launch and activation checks passed. Cross-user,
multi-session, Remote Desktop, ARM64, and Surface Go 2 checks were unavailable
and remain unverified.

## Environment

| OS | Process / OS architecture | Session | Runtime |
|---|---|---|---|
| Windows 11 Enterprise, version 10.0.26200, build 26200 | x64 / x64 | Console, session 1 | .NET 12.0.0-alpha.1.26519.114 for test executables |

## Checks performed

| Scenario | Result | Evidence and boundaries |
|---|---|---|
| Startup activation race | **Passed, 10/10** | Repeated `Run_SingleInstance_QueuesActivationDuringApplicationStarting`. Each run starts a secondary application during primary startup and verifies forwarded arguments are delivered on the primary UI thread. |
| Single-instance identity and IPC | **Passed, 4/4 C# tests** | `*SingleInstance*` filter covers identity scoping, malformed/oversized frame handling, argument forwarding, and startup-race activation. The identity test uses synthetic paths, SIDs, versions, and session IDs; it is not a cross-account process test. |
| VB activation forwarding | **Passed, 1/1** | `WinFormsApplicationBuilderParityTests.Run_SingleInstance_ForwardsActivationArguments` passed on x64. |
| Concurrent launches and different install paths | **Passed for both sample languages** | For each language, launched one primary from the build output and five secondary processes rapidly from a temporary copy at a different path. Every secondary exited with code 0 and the primary remained active. The sample's visible argument text was not machine-read during this process check; exact argument forwarding is covered by the automated tests above. |
| x64 | **Passed** | The Windows OS, process, and test runner were x64. |
| Temporary-process cleanup | **Passed** | Both primary processes exited after close, and the temporary copied-output directories were removed. |

The cross-path launch check uses the samples' explicit application IDs. The
existing identity test separately verifies that the default identity isolates
different install paths, while an explicit identity can intentionally share
across paths. It also verifies that changing the user SID or session ID
produces a different identity.

## Reproduction

From the repository root, use the repository-local runtime when running the
compiled test executables:

```powershell
$env:DOTNET_ROOT = "$PWD\.dotnet"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"

$testExe = "artifacts\bin\System.Windows.Forms.Tests\Debug\net11.0-windows7.0\System.Windows.Forms.Tests.exe"
for ($run = 1; $run -le 10; $run++) {
    & $testExe --filter-method "System.Windows.Forms.Tests.WinFormsApplicationBuilderTests.Run_SingleInstance_QueuesActivationDuringApplicationStarting" --parallel none
    if ($LASTEXITCODE -ne 0) { throw "Startup race run $run failed." }
}

& "artifacts\bin\System.Windows.Forms.Tests\Debug\net11.0-windows7.0\System.Windows.Forms.Tests.exe" --filter-method "*SingleInstance*" --parallel none
if ($LASTEXITCODE -ne 0) { throw "C# single-instance tests failed." }
& "artifacts\bin\Microsoft.VisualBasic.Forms.Tests\Debug\net11.0-windows7.0\Microsoft.VisualBasic.Forms.Tests.exe" --filter-method "Microsoft.VisualBasic.Forms.Tests.WinFormsApplicationBuilderParityTests.Run_SingleInstance_ForwardsActivationArguments" --parallel none
if ($LASTEXITCODE -ne 0) { throw "VB single-instance parity test failed." }
```

For the process check, build the paired samples using the commands in the
[migration guide](vb-application-framework-migration.md). Start the primary
executable and wait until its main form is shown. Copy that executable's
complete output directory to a unique temporary directory, then start five
secondaries from the copied path without waiting between launches. Confirm
each secondary exits successfully while the primary remains alive, close the
primary normally, and remove the temporary copy. Repeat with the other
language's executable; do not run the C# and VB samples together because their
sample IDs are intentionally distinct.

## Unavailable environment checks

| Scenario | Result | Limitation |
|---|---|---|
| Multiple Windows users | **Not run** | No second Windows account was available for a cross-user launch. |
| Multiple Windows sessions | **Not run** | The available process was in Console session 1; no second interactive session was available. |
| Terminal Services / Remote Desktop | **Not run** | No Remote Desktop session or test host was available. |
| Windows on ARM64 (non-Apple) | **Not run** | No ARM64 Windows device was available. |
| Surface Go 2 / constrained hardware | **Not run** | No Surface Go 2 or comparable low-resource device was available. |
| Elevation boundary | **Not run** | No standard-user/elevated pair was available; this report makes no elevation-isolation claim. |

## Findings, risks, and follow-up

- Default identity is path-specific; an explicit application ID allows
  intentional path sharing. Both behaviors are covered by identity tests, and
  explicit path sharing was exercised with copied sample outputs locally.
- Identity partitioning by SID and session ID was verified with deterministic
  unit-test inputs only. Actual isolation between Windows users and sessions
  still needs execution under separate accounts and interactive sessions.
- The pipe uses `PipeOptions.CurrentUserOnly` and is local-only. It is not an
  elevation boundary; applications that require distinct elevation identities
  should use distinct explicit application IDs. No elevated-process test was
  performed.
- The rapid process check verified secondary exit and primary survival, but
  did not measure activation latency or read the forwarded text from the UI.
  Do not infer performance characteristics from these runs; benchmarking is
  separate work.
- Follow up on a Windows ARM64 device, a Surface Go 2 or documented constrained
  device, and a Remote Desktop host with at least two standard user accounts.
  Record OS/build, runtime/process architecture, session type, launch path,
  result, and any reconnect or rapid-reopen failure for each run.

No runtime changes were made for this exploratory issue. The remaining
environment matrix is not claimed as complete by this local report.
