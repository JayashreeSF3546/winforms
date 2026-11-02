# Infrastructure performance and resource probe

This exploratory probe supports [dotnet/winforms#14963](https://github.com/dotnet/winforms/issues/14963).
It measures equivalent infrastructure workloads, not a shipping performance promise.
No benchmark package, timing budget, durable logging provider, or product dependency is added.

## Comparisons

- `hostless`: the existing `Application.Run(form)` path without host or infrastructure.
  This is a minimal ownership reference, **not** an equivalent full-service baseline.
- `conventional`: a normal Generic Host, explicitly started off the UI thread, followed by
  `Application.Run(form)`, then explicit host stop and disposal after the UI is disposed.
- `integrated`: an unbuilt host builder passed to Application Builder, which owns host startup,
  the UI loop, coordinated shutdown, and disposal.

The two hosted modes register the same services, use the same JSON settings implementation,
payload, source-generated metadata, files, logging filters/provider, worker, and health check.
Configuration uses the same standard providers and precedence, including application-name JSON
files; the integrated mode exercises the actual desktop configuration extension.
Only the integrated mode adds the coordination and health-diagnostics adapters being measured.
This comparison does not claim that a new persistence algorithm outperforms legacy settings.

Each cycle creates a fresh host/provider, Form, and application where applicable. The Form has a
real native handle and posts work after `Shown`. Nonempty workloads run in a tracked background
task; its completion posts the final close, and both tasks are observed. Every successful cycle
requires dispatch, one Form disposal, a destroyed native window, and an exited message loop.
Failures terminate the process unsuccessfully; failed cycles are never discarded.

## Workloads

- `empty`: host construction/start/stop with no additional infrastructure. The hostless mode
  accepts only this profile.
- `configuration`: eager options validation, 100 options-monitor reads, and explicit reload.
  The script compares configuration with watchers disabled and enabled in separate processes.
  It does not measure asynchronous file-change delivery latency.
- `settings`: two saves, load/verification, malformed-primary rejection, explicit backup recovery,
  schema upgrade, reset, and completion of all five commit notifications. Each lifetime repeats
  the failure/recovery cycle. Corruption archives are removed within the owned temporary store
  so that accumulated disk files do not masquerade as resource retention.
- `logging`: 100 disabled and 100 enabled structured records through a cached `LoggerMessage`
  delegate. An explicitly buffered in-memory provider flushes at hosted stop, after UI disposal.
  This is formatting/buffering/flush-call overhead, **not** file I/O, durability, arbitrary
  third-party provider behavior, or exception-adapter/terminating-exception performance.
- `health`: one explicit offline health invocation with a real asynchronously disposed scoped
  check, plus a cooperative background worker that drains at host stop. The conventional path
  links the host lifetime token and owns an asynchronous scope; the integrated path invokes the
  public diagnostics adapter.
- `all`: all preceding infrastructure operations in one complete lifetime.

Missing optional configuration files remain optional. Watchers are opt-in. The host stop timeout
is explicitly infinite because this cooperative probe must join its worker; this is not a
recommendation for noncooperative production tasks or a claim that arbitrary shutdown cannot hang.
Only uniquely created temporary stores are modified. No user preferences or machine configuration
are changed.

## Run from the repository root

Build with the repository's authoritative Arcade entry point:

```powershell
.\build.cmd -projects src\test\integration\WinFormsInfrastructurePerformance\WinFormsInfrastructurePerformance.csproj -configuration Release -verbosity minimal
```

Run serial comparisons after builds/tests have finished:

```powershell
.\src\test\integration\WinFormsInfrastructurePerformance\Measure.ps1 -Cycles 100 -Warmup 5 -Repeats 3
```

For a short script/correctness check:

```powershell
.\src\test\integration\WinFormsInfrastructurePerformance\Measure.ps1 -Cycles 3 -Warmup 1 -Repeats 1 -Workloads configuration,all
```

To explore the other message-loop backend, pass `-Backends component-manager`, or pass both
`light,component-manager`. The script defaults to Release, the lightweight backend, all six
profiles, three process repeats, and an additional watchers-on configuration pair. It reverses
the case order on even repeats and never runs measurement processes in parallel.

The runner discovers the built target framework and selects the repository's `.dotnet` runtime.
For a direct run, set `DOTNET_ROOT` to that directory, then run the built executable with
`--mode integrated --workload all --cycles 100 --warmup 5`. Add `--watchers` only for
`configuration`/`all`, or `--component-manager` for that backend. Debug output is marked as such;
do not mix it into Release comparisons.

Correctness coverage is in `WinFormsInfrastructurePerformanceTests`. Build its existing project
graph with `build.cmd`, then invoke the compiled integration-test executable using
`--filter-class '*WinFormsInfrastructurePerformanceTests'`, not a VSTest `--filter`.
The tests assert equivalent completed work and ownership, never machine-specific time budgets.

## Reports and boundaries

Artifacts are written beneath `artifacts\log\InfrastructurePerformance\<timestamp>`:

- `manifest.json`: source revision/worktree status, selected profiles/configuration/repeats,
  CPU description/count, stopwatch frequency, runtime switches/configuration, and actual probe,
  framework, companion and infrastructure binary hashes.
- Per-process stdout/stderr and JSON: process-cold first use, warmed cycles, operation counts,
  runtime/architecture/OS/backend/Terminal Services status, and resource checkpoints.
- `summary.csv`: first-use and warmed per-process medians, lifetime allocation medians, resource
  deltas, and final weak-reference survival.
- `operations.csv`: batch-normalized operation median/p95 time, allocation bytes per operation,
  and reciprocal median throughput. A 100-record batch is not 100 independent latency samples.
- `comparisons.csv`: integrated minus matching conventional medians, paired by repeat, profile,
  watchers and backend. Negative deltas do not establish an improvement on other machines.

Configure/build is nested within the complete lifetime. Startup ends at the first posted UI
dispatch; shutdown begins immediately before final close and ends after host/provider disposal.
The complete lifetime includes workload execution and harness dispatch/observation overhead.
Elapsed time and process-wide `GC.GetTotalAllocatedBytes(true)` bracket the same operation action;
flush allocation covers the exact flush action, not the whole stop sequence. Nested operation
measurements must not be added to the enclosing lifetime. Process-wide allocations can include
runtime/background activity and are not per-thread allocations.

First use is process-cold, not OS-cache-cold. The externally measured launch-to-dispatch value
also includes process launch/runtime initialization. Warmups are separate and unreported.
Cycle/report storage is preallocated before the warmed resource baseline. Checkpoints after
warmup and every 25/final measured cycles collect/finalize/collect, then report managed heap,
private bytes, working set, threads, kernel handles, GDI/USER objects, and weak references to
Forms, application/host owners and root service providers. Collection/checkpoint work is outside
the timed lifetime but can perturb subsequent timings. Weak survival is evidence to investigate,
not by itself a proven leak or permission to omit a retaining root.

Every successful lifetime also exclusively opens each owned file after host/provider disposal.
This detects outstanding incompatible file ownership, but is **not** an enumeration of all file
handles. `KernelHandles` is the total process handle count; it must not be labeled a file-handle
count. Native resource trends and exclusive-open checks are complementary evidence.

## Acceptance limits

Measurements are local exploratory evidence, not CI timing assertions, representative-hardware
certification, or owner-approved startup/memory/resource budgets. Record failed attempts and
environmental interference separately; do not retry failed cycles inside the probe.
Remote CI, native ARM64, Terminal Services, deployment/storage classes, durable providers and
owner-agreed budgets remain external acceptance work.

The [earlier lifetime probe](../WinFormsApplicationLifetimePerformance/README.md) records separate
component-manager resource growth and retaining-root observations. This probe does not erase
those findings or establish general leak freedom.

The [services implementation record](../../../../docs/design/winforms-application-builder-services.md#local-release-measurements-and-remaining-blockers)
contains actual baseline/proposed numbers and run commands. Long non-persistence comparisons and
short persistence comparisons completed locally. Long persistence stability remains blocked by a
native replacement error reproduced without any host/settings service; component-manager runs
also reproduced backend-wide USER-object growth. Neither finding is counted as a passing budget.
