# Generated application startup and publish footprint

This development-only comparison supports
[dotnet/winforms#14971](https://github.com/dotnet/winforms/issues/14971).
It runs actual, unmodified generated entry points, not the lifecycle smoke entry points.
No benchmark package, product dependency, template default, or machine policy is changed.

## Comparisons and prerequisites

The runner creates current SDK `winforms` C#/VB apps in an isolated template hive and copies
the basic Application Builder C#/VB sources from a successful lifecycle run. It publishes all
four apps in Release for the native Windows architecture, both framework-dependent (FDD) and
self-contained (SCD). The same explicit prototype consumer imports, framework DLLs, Core
version, RID and build properties apply to both sides. Published prototype DLL hashes must match.

The installed SDK's current templates default to `net11.0-windows`; the harness overrides
`TargetFramework=net12.0-windows` for **both** variants without editing their source. This removes
a runtime-train confounder, but makes this a current-template **payload** comparison on the
prototype stack, not a shipping .NET 11 versus .NET 12 benchmark. The SDK VB payload retains its
generated startup conventions; the builder payload retains its explicit STA entry point.
The framework and hosting package must be built in Release. A fresh, short private package cache
avoids accidentally reusing the Debug hosting package with the same development version.

Basic defaults include the host, configuration/options, Debug logging provider, and lazy typed
settings registration. JSON change watchers are disabled by the generated entry point. An
additional `basic-watchers` measurement uses the **same published app** with the public
`--hostBuilder:reloadConfigOnChange=true` option. It measures opt-in watcher startup/resource and
shutdown cost, not file-change delivery latency. It does not duplicate publish files.

Registered settings are not loaded or saved by this empty app. No debugger is attached, and the
Debug provider is not a durable logging sink. The basic-minus-current delta is the combined cost
of these actual defaults, not separately attributed DI/configuration/logging/settings cost.
For explicitly exercised optional services, use the separate
[infrastructure probe](../../../src/test/integration/WinFormsInfrastructurePerformance/README.md).
Those workload measurements are not substituted for generated-app measurements here.

## Run

Use an otherwise quiet, interactive Windows desktop without a debugger. Do not interact with
automatically opened forms. Do not run builds or other benchmarks concurrently with measurements.
From the repository root:

```powershell
$artifacts = Join-Path $env:TEMP 'WinForms14967Artifacts'
.\build.cmd -projects src\Microsoft.Extensions.WinForms.Hosting\src\Microsoft.Extensions.WinForms.Hosting.csproj `
    -configuration Release -pack -verbosity minimal "/p:ArtifactsDir=$artifacts"
.\build.cmd -projects pkg\Microsoft.Dotnet.WinForms.ProjectTemplates\tests\PerformanceDriver\TemplatePerformanceDriver.csproj `
    -configuration Release -verbosity minimal "/p:ArtifactsDir=$artifacts"

# Supply the result directory from a successful Test-TemplateLifecycle.ps1 run.
.\pkg\Microsoft.Dotnet.WinForms.ProjectTemplates\tests\Measure-TemplatePerformance.ps1 `
    -LifecycleOutput (Join-Path $env:TEMP 'WinForms14969\402e12d8975a4a7d8151d8fca8010948') `
    -PrototypeWinFormsDirectory "$artifacts\bin\System.Windows.Forms\Release\net12.0" `
    -HostingPackageDirectory "$artifacts\packages\Release\Shipping" `
    -Driver "$artifacts\bin\TemplatePerformanceDriver\Release\net12.0\TemplatePerformanceDriver.exe"
```

The lifecycle output path above is existing local evidence, not a portable prerequisite. On
another checkout, run the [lifecycle procedure](README.md#automated-lifecycle-matrix) first and
substitute its reported result path. The lifecycle-generated basic source is copied byte-for-byte;
its original files and hashes are checked again after measurement.

The default is one unreported warmup process per case and five measured processes per case,
with a one-second observation interval. All launches are serial, with reversed case order on even
rounds. `-Repeats 2 -Warmup 1` is a short correctness check; increase repeats for a timing study.
`-IdleMilliseconds` controls the observation interval, not a claim of true steady state.
The watchdog bounds failures, not acceptable startup/shutdown latency.

## Boundaries and evidence

An independent console observer uses operating-system process/window APIs. It does not reference
the repository WinForms product, inject code, replace `Main`, force collections, attach a debugger,
or use UI Automation during timed startup.

| Metric | Boundary |
|---|---|
| Startup | Parent timestamp immediately before process launch through a visible, expected-title main window answering synchronous `WM_NULL`. Includes runtime startup, host construction, OS scheduling and 10 ms polling. Not first paint, first posted UI dispatch, or a rendered-frame timestamp. |
| Shutdown | Immediately before posting normal `WM_CLOSE` through process exit zero. Includes actual UI disposal, host stop/provider disposal, and runtime/process termination. |
| Private bytes / working set | External process snapshots at readiness and after the observation interval. Not managed heap, allocation rate, peak usage, or a leak proof. No forced GC. |
| Kernel handles / threads | Process-wide snapshots, not specifically file handles. |
| GDI / USER objects | `GetGuiResources` for the measured process. |
| Loaded modules | OS process-module enumeration, **not** a count of all CLR-loaded assemblies. |
| Published file/assembly counts | Recursive inventory; managed assemblies identified from PE metadata, including satellites. No assembly is loaded to inspect it. |
| Publish bytes | Sum of all published file lengths, including PDBs. Not compressed download size, filesystem allocation, installed shared frameworks, or a signed installer size. |

Each measurement is process-cold; caches are not flushed. Warmup populates shared caches, so these
are **not OS-cache-cold** timings. Snapshot/module enumeration happens after the startup timestamp
and before shutdown timing. It perturbs observation, not the measured startup boundary. Windows
scheduling, runtime tiering, security scanning, shared caches and other machine activity can bias
the comparison. Repeat on representative hardware before drawing conclusions.

The unique output directory retains:

- `manifest.json`: revision/dirty state, SDK/runtime/prototype inputs, CPU/OS/RID, driver/source
  hashes, settings, and explicit Hybrid blocker.
- `logs`: every creation/restore/publish/observation command's stdout/stderr. Inventory JSON
  includes every file path, byte length, hash, and managed assembly identity/version.
- `samples.json`: all raw readiness/settled resource samples and durations, including warmups
  marked separately.
- `summary.csv`: per-case measured medians, startup min/max, footprint and resource counts.
- `comparisons.csv`: basic/default and basic/watchers minus the matching current-template medians.
- `optional-comparisons.csv`: watchers minus basic defaults for the same language/deployment.
- `result.json`: overall command outcome; failures throw and retain partial samples/logs.
- `sources` and `publish`: generated sources and the exact deployment artifacts measured.

There are no machine-specific pass/fail timing or memory thresholds. Failed startup, unexpected
window identity, missing resources, failed close, nonzero exit, missing samples, mismatched
prototype DLLs or malformed reports fail the run rather than being dropped from its median.
Do not treat negative deltas from a few samples as demonstrated improvements.

## CI and acceptance boundaries

The Azure PR queue parameter `templatePerformanceTests` is off by default. When provisioned with
the matching Core/Desktop runtime train and an interactive desktop, it builds the independent
observer and runs the native x64 Release lifecycle preparation plus a short comparison. Logs and
reports are retained under the existing `artifacts\log\Release\TemplatePerformance` artifact root.
It checks executable correctness/report production, not performance budgets on shared agents.
This wiring is not evidence that remote CI executed.

Runnable Hybrid remains **Blocked** by public coordinated-lifetime/awaitable WebView cleanup
prerequisites. A disabled scaffold's activation failure or compile result is not a Hybrid startup
measurement. Actual Hybrid overhead, WebView child-process memory/resources, assets and shutdown
need a separate process-tree study once runnable.

Native ARM64/x86 repetition, remote-session/slow-hardware measurements, true cold-cache studies,
managed-heap/allocation profiling, independently attributed optional service workloads, shipping
framework/package support, and owner-approved budgets remain separate acceptance work. Earlier
component-manager resource-growth indicators remain open; fresh single-lifetime process snapshots
do not resolve repeated-lifetime findings.

## Local observations

The local implementation based on `9308d6309` was measured in Release on Windows build 26200,
native x64, Intel Core i5-8265U (4 cores/8 logical processors), DPI 144, without a remote session.
The SDK was `12.0.100-alpha.1.26480.102`; Core was `12.0.0-alpha.1.26551.101`, with the SDK's
`12.0.0-alpha.1.26480.102` Desktop train and the same freshly built Release framework/hosting
binaries for both sides. The listed GC/tiering/ReadyToRun/roll-forward environment overrides were
unset. No exclusive-machine or OS-cache-cold claim is made.

The two Arcade build commands above passed with zero warnings/errors. The comparison command
above passed with its default options: eight real publishes, 12 execution cases, 12 separate
warmup processes, and **60 measured processes**, all with normal window close and exit zero.
Publish mode, apphost/runtime PE architecture, matching prototype DLL hashes, source preservation,
raw sample/summary dimensions and comparisons were checked. Separate failure probes confirmed
that a missing application, missing inventory directory, and wrong-window startup timeout fail
explicitly; the timed-out observer removed its own application process. Both edited pipeline
YAML files and the new inline PowerShell step parsed locally; remote CI was **NotRun**.

Final raw evidence:
`%TEMP%\WinForms14971\8f9d5a7943614302bf0cb0e98b8decbf`.
An earlier eight-case comparison is retained separately; the final matrix includes the optional
watcher cases and is the source of the following five-process medians:

| Language / deployment | Current startup ms | Basic startup ms | Current shutdown ms | Basic shutdown ms | Extra private MiB | Extra working set MiB |
|---|---:|---:|---:|---:|---:|---:|
| C# / FDD | 390.76 | 548.00 | 80.54 | 88.02 | 2.68 | 10.63 |
| C# / SCD | 382.42 | 561.16 | 77.70 | 89.86 | 2.80 | 10.72 |
| VB / FDD | 408.81 | 643.27 | 76.10 | 84.65 | 2.84 | 11.45 |
| VB / SCD | 422.30 | 583.28 | 84.30 | 88.42 | 2.90 | 11.59 |

Basic adds **36 published files / 35 managed assembly files** in both languages/deployments.
The total incremental footprint is 1,051,932 bytes for C# and 1,040,033 bytes for VB. FDD counts
are current 50 files/41 assemblies versus basic 86/76; SCD counts are 284/261 versus 320/296.
The absolute prototype footprint includes private framework copies and symbols: approximately
31.55 MB current versus 32.60 MB basic C# FDD, and 127.45 MB versus 128.51 MB SCD. These are
decimal MB, not shipping shared-framework application sizes or compressed downloads.

Basic's observed kernel-handle medians add 78 (C#) or 81 (VB) over current. All default-case GDI
and USER medians are respectively 19 and 23; those snapshots are not a repeated-lifetime
resource plateau or leak investigation.

Opting into watchers adds six kernel handles, one thread and three mapped modules in every
matched comparison. Private-byte median deltas range from 102,400 to 270,336 bytes; working-set
deltas range from 319,488 to 483,328 bytes. It adds **zero publish bytes/files/assemblies**, because
the same binaries are used. Startup deltas range from -70.42 to +22.42 ms, illustrating timing
noise rather than evidence that watchers improve startup. These observations support retaining
the already-disabled watcher default, not removing supported configuration or changing defaults
based on unapproved latency budgets.

No product or generated payload was optimized or stripped to obtain these results. Trimming,
NativeAOT, durable settings/logging workloads, Hybrid and external acceptance remain outside
this local comparison.
