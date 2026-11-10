# VB-framework and shared-model measurements

Issue: [dotnet/winforms#14981](https://github.com/dotnet/winforms/issues/14981).
This is a development benchmark, not public API approval or a performance budget.
The [environment qualification](winforms-application-builder-environment-validation.md)
still has real multi-user/session, RDP and hardware acceptance gaps.

## Proposal and simple flow

Measure what adopting the shared model costs compared with the unchanged VB application
framework. A working application can still start slowly or retain resources after repeated use.

```text
Fresh native worker
    -> first-use construction, Run, posted UI dispatch, Close, cleanup
    -> warmup cycles
    -> measured cycles, collected resource checkpoints every 25 cycles
    -> raw report

Fresh primary
    -> posted UI dispatch, READY
    -> launch secondary, send correlated request, observe actual owner callback
    -> repeat, stop primary, await process exit
    -> reopen and repeat
```

For example, opening a second document must reach the first application's dispatcher, not
merely exit successfully. The observer retains both the callback latency and secondary result.
Repeating that sequence checks finite resource trends; it does not prove all leaks are absent.

## Code and workload boundaries

The existing signed native workers are reused; no new friendship or consumer API is added.
`CSharp\BenchmarkMeasurements.cs` supplies measurement storage, checkpoints and correlation.
`CSharp\PerformanceWorkload.cs` composes the shared builder directly in C#.
`VisualBasic\PerformanceWorkload.vb` independently composes both the legacy VB framework and
the shared builder in VB. Neither delegates application composition to the other language.
The two `Program` entry points select the measurement workloads. The existing GUI-resource
reader is linked from the lifetime benchmark rather than copied.

`Measure-Parity.ps1` runs three variants: native VB/legacy, native VB/shared and native C#/shared.
Each runs `empty`, `splash` and `network` in separate processes. There is no optional Generic
Host, settings I/O, logging, visual-styles initialization or user business workload. This
isolates the application-model paths, not all capabilities of a production VB application.
The legacy framework retains its normal initialization and cleanup; it is not rewritten
to resemble the builder.

| Metric | Boundary / qualification |
|---|---|
| Launch to dispatch | Parent stopwatch immediately before `Process.Start` through receipt of READY from a callback posted by the main Form's Shown event. Includes process/runtime startup, instrumentation, stdout and parent scheduling. Not first paint. |
| First-use startup | Worker stopwatch before model construction through that posted callback. Process-cold, not disk-cache-cold. |
| Warm startup / shutdown | Same construction-to-callback boundary after warmup; Close immediately before `Form.Close` through Run return and shared disposal. |
| Allocations | Process-wide `GC.GetTotalAllocatedBytes(true)` delta around construction, Run and cleanup. Includes incidental runtime/background work, not only UI-thread allocations. |
| Managed retention | Full collect/finalize/collect outside timed cycles; preallocated cycle/result/weak-reference storage; checkpoints at warmed 0, each 25 cycles and final partial batch. |
| Process resources | Private bytes, working set, threads, kernel handles, GDI/USER objects, open Forms and weakly live Forms/model objects. No handle-type attribution is inferred from total handles. |
| Splash overhead | Empty versus splash, with minimum display time zero in both implementations. Legacy uses its own background splash pump; shared uses its existing owner-STA implementation. This is not equivalence of responsiveness, paint or minimum-duration semantics. |
| Network subscriptions | Both install real OS availability/address listeners; no adapter changes or synthetic events. Shared uses a counted borrowed source and requires zero subscriptions after each Run. Legacy uses its normal NetworkAvailabilityChanged event and DisconnectListener cleanup; its internal subscriber count is not introspected and is reported as null. |
| Request to callback | Secondary Stopwatch timestamp placed in the correlated payload before model construction/selection to entry in the owner's STA callback. Same-machine monotonic clock. |
| Launch to callback | Parent stopwatch before secondary Process.Start through correlated CALLBACK output. Includes CLR startup, stdout serialization and scheduling. |
| IPC resources | Primary snapshots at ready, after activation warmup, every five measured activations and after disposal, plus repeated fresh primary/secondary process exits. Handles include, but do not identify, pipe handles. Primary allocation totals include controls, diagnostics and teardown, not just IPC. |

The legacy IPC baseline uses the actual `IsSingleInstance` and `StartupNextInstance` implementation.
Its `LegacyReturned` result alone is not delivery proof: the parent separately requires the exact
owner callback. Shared requires both that callback and `Handled`. Bring-to-foreground is disabled
in both, so Windows focus policy is not being benchmarked. Startup-not-ready, failed and ambiguous
delivery are correctness cases in the existing parity/environment tests, not successful latency
samples. No automatic resend, fallback UI or retry-until-pass is added.

The legacy identity is still assembly-derived, unlike the explicit shared identity. Do not run
overlapping copies of this benchmark for the same payload/user: the observer requires a new
owned primary to report READY before sending clients. Identity/token mismatches fail explicitly.
This is not evidence that the two IPC implementations have equal security or isolation guarantees.

## Reproduce

Use an ordinary supported Windows desktop token without a debugger. The windows are test-owned
but briefly visible. Do not change shared machine settings, cache state, process priorities,
network adapters, users or sessions to stabilize results. Only owned processes are terminated.
The current script requires PowerShell 7 (ProcessStartInfo.ArgumentList and process-tree cleanup).

```powershell
$a = Join-Path $env:TEMP 'WinForms14978Artifacts'
$p = 'src\test\integration\UIIntegrationTests\System.Windows.Forms.UI.IntegrationTests.csproj'
.\build.cmd -projects $p -configuration Release -verbosity minimal `
    /p:ArtifactsDir="$a\" -binaryLogName "$a\Parity14981-Release.binlog"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
.\src\test\integration\ApplicationBuilderParity\Measure-Parity.ps1 `
    -ArtifactsDir $a -Configuration Release -Cycles 100 -Warmup 5 -Activations 20 -Launches 3
```

Activation warmup uses the same `-Warmup` count, excluded from latency CSV rows. IPC checkpoint
`CompletedCycles` counts all callbacks, including warmup; subtract that count for the measured
index. Checkpoints run only after the preceding client exited and before launching the next;
they are excluded from request latency but can perturb later samples.

The runner uses the repository-local runtime, an outside-checkout private evidence directory,
60-second signal watchdogs and ten-minute lifetime watchdogs. These are hang guards, not accepted
latency budgets. Each owner is stopped and awaited before the next owner starts. For explicitly
architectured outputs pass `-Platform x64`, `x86` or `arm64` after building that exact architecture.
Run native ARM measurements on actual authorized Windows ARM hardware; emulation is not proof.
The local executable path contains the current TFM; update it if the repository train changes.

Raw per-cycle JSON, checkpoints, callbacks, secondary logs, `lifetimes.csv`, `activations.csv`,
and source/binary hash manifest are retained outside git. Reports carry build mode, runtime,
architecture, OS, remote-session fact and loaded Forms/VB MVIDs. Machine paths can appear in
failure logs: do not commit or upload private raw evidence without approved redaction.

The normal integration graph discovers twelve small correctness cases in
`WinFormsApplicationParityTests.Performance.cs`. They cover all nine lifetime combinations and
three actual IPC combinations, with no speed or memory-delta threshold:

```powershell
$env:DOTNET_ROOT = Join-Path $PWD '.dotnet'
$env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
& "$a\bin\System.Windows.Forms.UI.IntegrationTests\Release\net12.0-windows7.0\System.Windows.Forms.UI.IntegrationTests.exe" `
    --filter-method '*NativePerformance_*' --parallel none --report-trx `
    --results-directory "$a\TestResults\Parity14981"
if ($LASTEXITCODE -ne 0) { throw 'Benchmark correctness failed' }
```

## Interpretation and outstanding acceptance

Compare absolute medians and raw distributions, not percentages of tiny durations. Compare
VB/shared with VB/legacy first; C#/shared checks language projection, not a second legacy baseline.
Feature-on minus empty is an estimate on these finite runs, not an isolated causal measurement.
Models run sequentially, so JIT, OS caches, tiering, shared desktop activity and order bias remain.
Repeat in another order under authorized representative conditions before setting budgets.

Full GC perturbs later samples and does not drain native caches or force worker epilogues to run.
Retained resource-checkpoint report objects themselves contribute small bounded reporting overhead;
the reported deltas are not automatically corrected for that overhead.
Nonzero weak retention or monotonically growing handles/USER objects requires investigation;
never relabel it an accepted cache plateau without evidence. Total handles cannot prove that
pipe handles specifically are leak-free. Shared counted subscription cleanup is a strict check;
legacy subscription internals and either model's real network-event storms need separate tooling.

No product performance optimization is made merely to make this benchmark green. Public API,
protocol/security review, remote CI, native ARM/RDP/slow-device comparisons and owner-approved
compatibility/performance budgets remain external acceptance. Prior environment limitations
are not removed by local timing numbers.

## Local Release x64 evidence

The final run used the commands above on Windows build 26200, process architecture X64,
`.NET 12.0.0-alpha.1.26554.104`, SDK `12.0.100-alpha.1.26531.102`, without a debugger and
outside a remote session. Source base: `75cf30c9a` plus the uncommitted benchmark changes.
The private manifest records exact changed-source and payload SHA256s; those source hashes
were checked against the checkout after measurement.

All nine lifetime workloads completed: one first-use, five warmup and 100 measured cycles each,
**900 measured lifetimes** total. Each of three variants also completed three fresh primary
launch/exit rounds, each with five warmup and 20 measured activations: **180 measured callbacks**
plus 45 warmups. Every secondary result was correlated with the actual owner's callback.

### Startup, shutdown and allocation comparison

Milliseconds below are observations, not accepted budgets. Warm startup, shutdown and allocations
are medians over 100 cycles; launch and first-use are single fresh-process observations.

| Native variant | Workload | Launch to dispatch ms | First-use startup ms | Warm startup ms | Shutdown ms | Allocated bytes/cycle |
|---|---|---:|---:|---:|---:|---:|
| VB legacy | Empty | 1205.02 | 759.58 | 103.28 | 64.93 | 7264 |
| VB legacy | Splash | 756.16 | 479.66 | 100.78 | 21.60 | 13680 |
| VB legacy | Network | 576.45 | 418.10 | 70.24 | 6.05 | 66360 |
| VB shared | Empty | 735.05 | 518.33 | 25.33 | 15.63 | 9296 |
| VB shared | Splash | 581.74 | 419.81 | 73.12 | 17.95 | 12608 |
| VB shared | Network | 1040.23 | 541.99 | 141.30 | 19.73 | 68248 |
| C# shared | Empty | 551.29 | 358.94 | 33.00 | 15.33 | 9296 |
| C# shared | Splash | 547.12 | 385.34 | 51.71 | 12.13 | 12608 |
| C# shared | Network | 653.86 | 479.38 | 151.72 | 37.16 | 68248 |

Against native VB/legacy, native VB/shared allocated **2032 more bytes/cycle** for empty,
**1888 more** for network, and **1072 fewer** for splash. Feature-on minus empty allocation
was 6416 bytes for legacy splash versus 3312 for shared splash; actual network subscription
work added 59096 versus 58952 bytes. These include the harness and different model contracts,
not solely framework implementation allocations. Both shared language projections had identical
allocation medians for corresponding workloads.

Timing order is plainly noisy: even the legacy splash/network startup medians were below the
empty baseline. Therefore do not infer a speedup, negative splash overhead, language penalty
or an approved compatibility cost from these timing differences. A preceding exploratory run
also had substantially different timings; the final warmed-IPC-capable source is the one
identified by the manifest here.

### Activation latency

Each row contains 60 measured requests across three primary processes; warmups are excluded.
P95 is the nearest-rank 57th sorted sample out of 60. These are sequential, ready-primary
requests, not concurrent-burst latency under saturation.

| Native variant | Request-to-callback median / P95 ms | Launch-to-callback median / P95 ms |
|---|---:|---:|
| VB legacy | 132.29 / 314.80 | 309.83 / 673.48 |
| VB shared | 165.77 / 383.02 | 420.54 / 828.09 |
| C# shared | 118.27 / 192.71 | 281.67 / 466.43 |

The VB/shared median was 33.48 ms higher than VB/legacy for request-to-callback in this run.
This is not an isolated pipe cost: selection, token/process validation, UI dispatch and incidental
runtime work differ, and new secondaries start cold. The parent boundary additionally includes
process startup and output observation. No automatic retry hid failed deliveries.

### Resource findings and unresolved indicators

All lifetime checkpoints had zero open/tracked live main Forms and zero tracked live model
objects after collection. All nine lifetime workloads had unchanged thread and GDI counts.
All shared lifetime workloads had unchanged kernel handles; shared USER changes were zero
or one. Counted shared network subscriptions were zero after every Run.

| Lifetime variant | Collected heap delta, warmed 0 to 100 bytes | Kernel handle delta | USER delta |
|---|---:|---:|---:|
| VB legacy empty | 5280 | 0 | 19 |
| VB legacy splash | 11560 | 4 | 56 |
| VB legacy network | 5280 | 0 | 19 |
| VB shared empty | 4720 | 0 | 0 |
| VB shared splash | 4720 | 0 | 1 |
| VB shared network | 4720 | 0 | 1 |
| C# shared empty | 4720 | 0 | 1 |
| C# shared splash | 10864 | 0 | 0 |
| C# shared network | 4720 | 0 | 0 |

Legacy splash USER counts were **20 / 58 / 59 / 76 / 76** at warmed cycles
0 / 25 / 50 / 75 / 100; handles were **355 / 355 / 359 / 359 / 359**.
The repeated step growth is an **unresolved resource indicator**, not demonstrated
shared-builder overhead, confirmed cache behavior, or a leak-free pass. No unrelated legacy
product fix is attempted here.

During IPC each primary had exactly its one live main Form/model at the held checkpoints.
Warmed-to-final-held handle deltas ranged from 0 to 2 in legacy, 2 to 4 in VB/shared and
0 to 2 in C#/shared; some runs also gained one thread. Collected heap increased approximately
35-54 KB over that interval. The first warmed checkpoint precedes first serialization of the
checkpoint shape, so that interval includes instrumentation-cache initialization. Later
checkpoints often leveled but not universally: VB/shared round 1 gained another two handles
and one thread between total callbacks 20 and 25. Roots/handle types are **not established**.

After stop, all nine primaries had zero open/tracked live main Forms. All shared model weak
references cleared. Two of three legacy IPC primaries still had one weakly live model at the
immediate post-Run snapshot; the retaining root is **unresolved**, even though their processes
then exited normally. Process exit is not proof of correct in-process listener teardown.
These observations block resource-stability acceptance and warrant longer runs and retaining-root/
handle diagnostics on an authorized environment, rather than sleeps or collection-until-green.

### Verification and evidence locations

`build.cmd` for the Release integration graph passed with zero warnings/errors. The initial
full parity/environment/migration regression selector passed **58 cases**; after adding the
warmed IPC checkpoints, the final `*NativePerformance_*` selector passed **12 cases**, including
the new checkpoint command. Those twelve are a recheck, not twelve extra distinct cases.
The full regression command was:

```powershell
& "$a\bin\System.Windows.Forms.UI.IntegrationTests\Release\net12.0-windows7.0\System.Windows.Forms.UI.IntegrationTests.exe" `
    --filter-class System.Windows.Forms.UITests.WinFormsApplicationParityTests `
    --filter-class System.Windows.Forms.UITests.WinFormsApplicationMigrationTests `
    --parallel none --report-trx --results-directory "$a\TestResults\Parity14981-Regressions"
```

Final raw evidence:
`%TEMP%\WinForms14981-27ccbc5a5e924a45b4ca6bb13fd77bdc`.
The integration artifact root contains `Parity14981-Release.binlog` and
`TestResults\Parity14981-Regressions` / `TestResults\Parity14981-Final`.
The preliminary run remains private under
`%TEMP%\WinForms14981-5613727c41ed48ea930212ac452b8c5b`.
No raw reports are committed. Existing CI discovers the small tests; **remote CI was not run**.
The local benchmark implementation and evidence are complete; the unresolved resource findings
and external acceptance above mean **the full issue is not certified complete**.
