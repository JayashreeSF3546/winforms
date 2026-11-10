# #14979 local validation and handoff

Date: 2026-11-10. Branch: `Application_Builder_14972_VB_Parity`.
Starting commit: `ab62cb63b` (#14978). **No commit or PR created.**

## Status versus full acceptance

Feasible delivery completed:

- Native VB legacy and public-prototype shared selections reuse the same extracted UI-free
  startup/work logic. Legacy passes the real `StartupEventArgs.CommandLine` to that logic.
- Native C# implements equivalent shared composition, handlers and usable Form.
- All normal selections stay interactive; automation requires explicit `--probe`.
- Guide includes before/extract/select/opt-in/test/rollback flow, exact root-relative commands,
  click steps, settings ownership and source-backed intentional differences.
- Existing signed parity workers provide labelled **test-only** internal-feature exercises;
  they are not promoted to normal consumers.
- Integration graph builds/copies the resolved sample payloads and discovers six focused tests.
  Existing process launch/drain/watchdog/PID cleanup is factored into `ApplicationProcess`;
  the 32 old parity cases still use the same semantics.

**Full #14979 acceptance remains blocked**, not declared done: the shared feature surface and
migration adapters need public/compatibility/package review before normal VB/C# consumers can
demonstrate all requested lifecycle, single-instance, splash and service features. No friendship
expansion, reflection/private access, public API invention, runtime source copy, old VB runtime
edit, second loop, host/provider or settings subsystem was used to bypass that gate.

Existing applications were not modified. The new manual VB source snapshot exercises the old
public framework; it is not proof of existing-binary compatibility or of a VS generator run.

## Environment and evidence

| Item | Observed |
|---|---|
| SDK | `12.0.100-alpha.1.26531.102` (`.\.dotnet\dotnet.exe --version`) |
| Core required by sample runtimeconfig | `12.0.0-alpha.1.26554.104` |
| Desktop installed in repo-local runtime tree | `12.0.0-alpha.1.26531.102` |
| PowerShell | `7.6.9` |
| Target / execution | `net12.0-windows7.0`, Debug, x64, Windows desktop session |
| Artifacts root | `$env:TEMP\WinForms14978Artifacts` on C:, reused to avoid duplicate product graphs on D: |
| Loaded Forms MVID in process probes | `86666ab9-b006-4fba-b315-b0a2e576d48d` |

This is not evidence for x86/Release/ARM64/device/RDP qualification. Samples' runtimeconfig
currently requests Core only; the graph copies repository-built Forms/Drawing dependencies.
Tests compare loaded Forms MVID with their product graph and require sample-local assembly paths.

## Exact final build/test commands (repository root)

```powershell
$env:DOTNET_ROOT = "$PWD\.dotnet"
$env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$a = Join-Path $env:TEMP 'WinForms14978Artifacts'
$p = 'src\test\integration\UIIntegrationTests\System.Windows.Forms.UI.IntegrationTests.csproj'
$env:TestRunnerAdditionalArguments =
    '--filter-class System.Windows.Forms.UITests.WinFormsApplicationMigrationTests --parallel none'
try {
    .\build.cmd -projects $p -integrationTest -verbosity minimal /p:ArtifactsDir="$a\" `
        -binaryLogName "$a\Migration14979-Final-IntegrationEntry.binlog"
    if ($LASTEXITCODE -ne 0) { throw 'Integration entry failed' }
}
finally {
    Remove-Item Env:\TestRunnerAdditionalArguments
}

$test = "$a\bin\System.Windows.Forms.UI.IntegrationTests\Debug\net12.0-windows7.0\System.Windows.Forms.UI.IntegrationTests.exe"
& $test --filter-class System.Windows.Forms.UITests.WinFormsApplicationMigrationTests `
    --parallel none --report-trx --results-directory "$a\TestResults\Migration14979-Final"
if ($LASTEXITCODE -ne 0) { throw 'Migration failed' }
& $test --filter-class System.Windows.Forms.UITests.WinFormsApplicationParityTests `
    --parallel none --report-trx --results-directory "$a\TestResults\Migration14979-Final-InternalParity"
if ($LASTEXITCODE -ne 0) { throw 'Parity failed' }
git --no-pager diff --check
```

Final Arcade integration entry: **Build succeeded, 0 warnings, 0 errors**, filtered tests succeeded.
Final direct executable verification:

| Suite | Passed | Failed | Skipped | Meaning |
|---|---:|---:|---:|---|
| `WinFormsApplicationMigrationTests` | 6 | 0 | 0 | VB legacy, VB shared, C# shared; normal/cancel each |
| Existing `WinFormsApplicationParityTests` | 32 | 0 | 0 | Internal friend-only feature/IPC regressions after helper reuse |

Six migration cases check original Unicode/empty/spaces/literal-quotes/trailing-backslash tokens,
real Shown/button work, retained counter, close veto/accepted close, once-only disposal, final
driver trace and local Forms identity. Canceled startup must produce no activation/Shutdown/UI
or work. Shared preflight cancellation intentionally has no builder lifetime events.

TRX files:

- `$a\TestResults\Migration14979-Final\System.Windows.Forms.UI.IntegrationTests_net12.0-windows7.0_x64.trx`
- `$a\TestResults\Migration14979-Final-InternalParity\System.Windows.Forms.UI.IntegrationTests_net12.0-windows7.0_x64.trx`
- Arcade integration-entry logs/results under `$a\log` / `$a\TestResults`.

The 6 cases were also run through Arcade, not just direct executable discovery; repeated runs
do not increase the unique-case count. No product implementation changed, so the broader unit
suite was not rerun. No full solution build, benchmark or device/session qualification is claimed.
Existing graph dependencies can build performance projects; no performance workload was executed.

## Normal interactive process probes

In addition to `--probe` tests, launched the following **without automation flags**, using
`ProcessStartInfo.ArgumentList` to preserve tokens and redirected stdout/stderr:

```powershell
# $vb / $cs are defined in README.md; run one at a time and use its own Form controls.
& $vb legacy -- '日本語' '' 'two words' 'literal "quotes"' 'C:\space path\'
& $vb shared -- '日本語' '' 'two words' 'literal "quotes"' 'C:\space path\'
& $cs shared -- '日本語' '' 'two words' 'literal "quotes"' 'C:\space path\'
```

An owned-HWND PowerShell probe enumerated only children of each created process's main window.
It read the trace (`WM_GETTEXT`), clicked Process item twice, checked the veto, posted Close,
waited for `close-vetoed`, unchecked the veto and posted Close again. It required exit 0,
empty stderr, count 2, the five original arguments including the empty token, and final disposal.
Readiness/exit waits had 30-second watchdogs; poll delays were readiness backoff, not elapsed-time
assertions. A finally terminated only the exact created Process if still running.

Observed successful default-launch evidence:

| Selection | Owned PID | Window handle | Counter / close |
|---|---:|---:|---|
| VB legacy | 13472 | 6751748 | 2; veto retained window; accepted close/drain |
| VB shared | 22896 | 6817284 | 2; veto retained window; accepted close/drain |
| C# shared | 2832 | 6882820 | 2; veto retained window; accepted close/drain |

The Form code was unchanged between these probes and the final build; the final build also
made legacy startup consume the real event argument collection and moved metadata reporting
out of the UI-free logic. Final automated process tests reran all three selections.

Final default-launch traces from the probe:

```text
VB legacy:
startup accepted activate shown processed processed close-vetoed closing
form-disposed shutdown returned

VB shared / C# shared:
startup accepted activate started shown processed processed close-vetoed closing
stopping form-disposed stopped returned
```

These are real GUI/control-process checks, **not human visual/accessibility/VS acceptance**.
No screenshots, themes, display settings, network adapters, privileges or user sessions changed.

### Failed attempts / resolved implementation checks

- First build: five missing-namespace compiler errors; fixed native
  `Microsoft.Extensions.WinForms` imports.
- Next build: one VSTHRD003 analyzer error when passing the test-owned stderr read to the reused
  completion helper. Added one local justification: redirected pipe read has no UI/JTF dependency.
- One finalization build caught two BC42314 diagnostics from documenting both VB partial
  declarations. Kept XML type documentation in `MyApplication.vb`, with a normal cross-reference
  comment in `ApplicationEvents.vb`. Final strict build passed.
- Bootstrap prints a nonfatal “cmake not found” advisory. The selected managed graph still built
  with **0 MSBuild warnings/errors**. No tools were installed or machine settings changed.
- `WaitForInputIdle` is not reliable for these console-subsystem GUI examples; initial probe
  failed. Used owned main-window readiness instead. A subsequent initial window-readiness
  attempt also exited before observation; this was not counted as successful evidence.
- A synchronous cross-process `SendMessage(BM_CLICK)` that closed the VB legacy Form hit a
  product Debug assertion: `UiaDisconnectProvider failed with 0x8001010D`
  (`RPC_E_CANTCALLOUT_ININPUTSYNCCALL`) during control destruction. The process terminated;
  that attempt **failed**, not silently passed. Posting the close click outside the synchronous
  cross-process send succeeded for all three selections. No product assertion/exception policy
  was disabled. Review this separately if synchronous external-control automation is required;
  no old VB or unrelated accessibility runtime fix is included in #14979.

## Static checks and untouched boundaries

- Native PowerShell checks passed for **11 affected C#/VB files** (BOM/CRLF/final newline),
  whitespace in all new files, **14 relative Markdown links**, and **4 project/targets XML
  documents**. No Python installation was needed; the initial Python check could not run because
  Python was unavailable and was replaced by native PowerShell checks.
- Focused Arcade graph successfully exported/copied both payloads.
- `git diff --check` passed. No `Winforms.sln` reorder or pipeline YAML change.
- No product source, `InternalsVisibleTo`, public approval list, legacy VB implementation,
  generated Designer file or existing sample was modified.
- No settings/IPC files introduced by these public samples; internal IPC regression tests retain
  unique IDs and owned-process cleanup. No broad process-name termination.
- Remote CI: **NotRun**. Local integration-entry success is graph execution evidence, not CI success.
- VS project generation/designer, existing-binary compatibility, API/stakeholder approval,
  external packaging/reference exposure: **NotRun / pending review**.

## Follow-up and stop boundary

Before calling #14979 fully accepted, obtain reviewed public contracts/adapters, add equivalent
normal-consumer feature samples against that approved surface, and validate their migration/tooling
behavior and remote CI. Do not solve that gate with broader friendship or private access.

Suggested next scheduled issue: **#14980**, only after explicit instruction and with its
prerequisites/gates acknowledged. Real multiple-user/session/Terminal Services/RDP/ARM64/x64/
Surface Go 2 qualification is not started. **#14981 benchmarking is not started.**
