# WinForms application lifetime measurements

Development-only measurements for
[dotnet/winforms#14946](https://github.com/dotnet/winforms/issues/14946).
The same empty, visible form and posted close run on one caller-owned STA:

| Mode | Ownership cycle |
|---|---|
| `legacy` | Construct form, `Application.Run(form)`, cleanup. |
| `builder` | Construct form, `UseStartupForm(form)`, build, run, dispose. |
| `hosted` | Builder plus a fresh real `HostBuilder` and coordinated shutdown. No user services or logging providers. |

Both loop backends run in separate processes. Host construction and provider disposal are intentional
additional work: do not attribute all hosted overhead to the core builder.

## Run

From the repository root on Windows, without a debugger:

```powershell
.\build.cmd -projects src\test\integration\WinFormsApplicationLifetimePerformance\WinFormsApplicationLifetimePerformance.csproj -configuration Release -verbosity minimal
.\src\test\integration\WinFormsApplicationLifetimePerformance\Measure.ps1
```

The script runs six mode/backend combinations, each with a first-use cycle, five warmup cycles,
and **100 measured cycles**. Forms briefly appear; do not interact with them. Raw JSON and
`summary.csv` go to `artifacts\log\LifetimePerformance\<timestamp>`. Use `-Cycles 1000` to
investigate trends. The ten-minute watchdog detects hangs, not acceptable shutdown latency.

For an individual mode:

```powershell
$env:DOTNET_ROOT = Join-Path $PWD '.dotnet'
& .\artifacts\bin\WinFormsApplicationLifetimePerformance\Release\net11.0\WinFormsApplicationLifetimePerformance.exe --mode builder --cycles 100 --warmup 5
```

Add `--component-manager` for the alternate backend. Stdout starts with `READY`, followed by one
JSON document. Update the script's TFM path if `NetCurrent` changes. Use a matching runtime and
architecture when testing Windows on ARM.

## Measurement boundaries

| Metric | Meaning |
|---|---|
| Launch to first dispatch | Parent stopwatch before `Process.Start` through receipt of flushed `READY` from the first posted UI callback. Includes runtime startup, application configuration, instrumentation, pipe and scheduling overhead. Not an exact rendered-frame timestamp. |
| First-use startup | First construction/build/run through posted UI dispatch in a fresh process, after application configuration. Process-cold, **not** an OS disk-cache flush. |
| Warm startup | Same construction-to-dispatch boundary after first-use and warmup. `ApplicationStarted` and `Shown` alone do not prove working dispatch. |
| Shutdown | Immediately before calling `Close` through return from `Run` and builder disposal. Hosted results include host stop and provider disposal. |
| Total | Construction through completed ownership cleanup. The first cycle includes readiness-signal output between startup and shutdown; warm cycles do not write output. |
| Allocations | Process-wide `GC.GetTotalAllocatedBytes(true)` delta around a cycle, including host worker threads and any runtime activity. |
| Managed heap | `GC.GetTotalMemory(false)` after collect/finalize/collect at checkpoints, outside timed cycles. Arrays and weak references are preallocated before the warmed baseline. |
| Private bytes / working set | Process memory snapshots. Native caches, committed memory and resident pages are not managed leak measurements. |
| Threads / kernel handles | Process-wide snapshots. Runtime and thread-pool resources can remain after host shutdown; compare warmed checkpoints. |
| GDI / USER objects | Process `GetGuiResources` counts, separately from kernel handles. |
| Live forms / applications | Weak references after full collection, including first-use/warmup objects. Nonzero counts are reported for investigation rather than hidden by sleeps or retrying collection. |

Each cycle verifies working dispatch, exactly one form-disposal notification before return,
native form-window destruction, no remaining message loop, and once-only builder events.
Hosted mode also verifies host stopped follows form disposal.

Full collections and process snapshots occur outside timed cycles: after warmup and every 25
measured cycles, plus the final partial batch. They perturb later execution and are intentional
diagnostic checkpoints, not a production workload. No sleeps, artificial delay, or forced
thread-pool settings stabilize results. Legacy teardown is not replaced with builder cleanup.

## Overhead and leak interpretation

Compare absolute median startup/shutdown and allocation differences against the matching legacy
backend. Percentages of tiny durations exaggerate noise. Keep first-use and warmed distributions
separate, examine raw samples, and repeat before setting budgets.

Correctness acceptance is strict for disposed forms, destroyed native form windows, and completed
lifetime ordering. Weak-reference survival is a separate diagnostic. One possible source is a
hosted task's worker-thread epilogue retaining a completed application after synchronous `Run`
returns; full collection does not force that thread to advance. That explanation is not established
by this harness. Inspect later checkpoints for persistent or growing retention rather than treating
one immediate snapshot as proof of a leak.
**No universal acceptable performance budget is asserted.**
Shipping budgets require owner approval and representative workloads; shared CI timing is not
that approval. Evaluate optional hosted operation separately.

Initial-to-warmed growth can include JIT/tiered compilation, WinForms/native graphics initialization,
COM initialization, and Hosting/DI/thread-pool caches. Positive retained bytes alone do not prove a
leak. Inspect 0/25/50/75/100 warmed checkpoints: sustained memory, handle, GDI/USER or thread growth
is an investigation signal even when weak references clear. Extend to 1000 cycles and use
allocation/native-handle tooling before assigning a cause. A stable 100-cycle run is evidence,
not proof that all leaks are absent.

Use an otherwise quiet desktop, record the source revision, and compare identical configurations.
JSON records runtime, OS, architecture, build mode, and Terminal Services status. Modes execute
sequentially, so shared OS caches/order and other machine activity can bias comparisons. Repeat
individual modes in another order if needed. No machine-wide tuning or OS-cache flushing occurs.

## Local measurement evidence

Measured the implementation based on `bc6bf64f9` in Release, x64, Windows build 26200,
`.NET 12.0.0-alpha.1.26519.114`, non-Terminal-Services desktop. Each mode/backend had one first-use
cycle, five warmups, and 100 measured cycles. The command was `.\src\test\integration\WinFormsApplicationLifetimePerformance\Measure.ps1`;
all six processes completed their dispatch, disposal, native-window and lifetime-order checks.
Raw local reports are in `artifacts\log\LifetimePerformance\20261029-120136`.

| Backend | Mode | Launch to dispatch (ms) | First-use startup (ms) | Warm startup median (ms) | Shutdown median (ms) | Allocation median (bytes/cycle) |
|---|---|---:|---:|---:|---:|---:|
| Light | Legacy | 7912.86 | 3021.59 | 79.94 | 42.07 | 6904 |
| Light | Builder | 626.57 | 402.58 | 51.89 | 24.25 | 8896 |
| Light | Hosted | 2019.99 | 1734.15 | 78.30 | 38.73 | 87000 |
| Component manager | Legacy | 1212.11 | 572.47 | 91.38 | 125.71 | 7704 |
| Component manager | Builder | 1781.09 | 929.45 | 82.56 | 130.78 | 9696 |
| Component manager | Hosted | 3405.48 | 2494.63 | 89.41 | 101.65 | 97176 |

Core builder allocation overhead was **1992 bytes/cycle** against legacy in both backends.
Hosted overhead against legacy was 80096 bytes/cycle (light) and 89472 (component manager);
this includes constructing/discarding the host and service provider. Timing varied substantially
between processes, including a much slower first legacy launch. These results do **not** establish
that builder startup is faster or approve a latency budget.

Resource findings from warmed checkpoints:

| Backend | Mode | USER counts at cycles 0 / 25 / 50 / 75 / 100 | Managed-heap delta, 0 to 100 (bytes) | Final live forms / applications |
|---|---|---|---:|---|
| Light | Legacy | 16 / 35 / 35 / 35 / 35 | 9712 | 0 / 0 |
| Light | Builder | 16 / 36 / 36 / 36 / 36 | 9712 | 0 / 0 |
| Light | Hosted | 19 / 39 / 39 / 39 / 39 | 18472 | 0 / 0 |
| Component manager | Legacy | 16 / 41 / 74 / 91 / 116 | 149616 | 0 / 0 |
| Component manager | Builder | 16 / 41 / 66 / 91 / 117 | 149616 | 0 / 0 |
| Component manager | Hosted | 18 / 44 / 69 / 94 / 119 | 147104 | 1 / 1 |

Light-backend USER counts and managed memory plateaued after cycle 25. Component-manager USER
counts grew approximately one per cycle in **all three modes, including legacy**, with continuing
managed-memory growth. This is an **unresolved leak indicator**, not demonstrated builder-specific
overhead or an accepted cache plateau. Native form windows were destroyed and tracked core objects
collected, so those checks do not explain or rule out the observed resource growth.

GDI counts and thread counts were unchanged between warmed baseline and cycle 100 in all modes.
Kernel handles were unchanged except hosted/component-manager (+2, plateaued by cycle 25).
Hosted/component-manager retained one tracked form/application at every checkpoint, without a
growing count; the retaining root has not been identified. Do not equate bounded retention with
confirmed cleanup.

Follow-up acceptance remains open: investigate the component-manager resource trend and hosted
retaining root, repeat timings under representative/quiet conditions, obtain an owner-approved
overhead budget, and collect remote CI/native ARM/Terminal Services evidence. The harness and
local baseline are implemented; these measurements are not leak-free shipping acceptance.

## CI correctness coverage

```powershell
.\build.cmd -projects src\test\integration\UIIntegrationTests\System.Windows.Forms.UI.IntegrationTests.csproj -verbosity minimal
$env:DOTNET_ROOT = Join-Path $PWD '.dotnet'
& .\artifacts\bin\System.Windows.Forms.UI.IntegrationTests\Debug\net11.0-windows7.0\System.Windows.Forms.UI.IntegrationTests.exe --filter-class 'System.Windows.Forms.UITests.WinFormsApplicationPerformanceTests' --no-progress --report-trx
```

Six isolated short tests cover all modes/backends, valid boundaries/checkpoints and collectible
non-hosted ownership, without performance thresholds. Hosted weak counts remain diagnostic because
of worker-thread scheduling. The existing public x64/x86 Debug/Release integration
matrix discovers them; arm64 remains build-only there. The developer comparison, not every CI
test, performs the 100-cycle investigation. Remote CI and native ARM/Terminal Services performance
require external verification.

This harness needs no new benchmark package. Direct process control makes cold launch, native
dispatch, teardown checkpoints and cross-thread allocations explicit. General samples and their
separate debug-output versus stdout test issue are unchanged.
