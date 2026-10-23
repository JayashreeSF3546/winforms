# WinForms Application Builder benchmarks

This standalone harness measures complete STA UI-thread lifecycles for the
legacy Visual Basic Application Framework and the shared WinForms application
builder, single-instance activation, and Microsoft.Extensions infrastructure.

The lifecycle scenarios compare:

- Conventional `Application.Run(Form)`.
- `WindowsFormsApplicationBase` with its generated-application startup path.
- `WindowsFormsApplicationBase` with a splash screen and network availability
  subscription.
- `WinFormsApplicationBuilder` without a host.
- `WinFormsApplicationBuilder` with an empty Generic Host.
- `WinFormsApplicationBuilder` with a splash screen and with the registered
  application services/network availability subscription.

Each run uses a small minimized, off-screen form that closes itself after it is
shown. The harness runs each scenario in a separate child process for cold
launch measurements and for independent warm/resource sessions. This prevents
static initialization from one scenario from contaminating the others and
keeps all WinForms work on an STA thread. The benchmark project references the
WinForms and Visual Basic Forms implementations in this checkout. The VB
baseline subclasses `WindowsFormsApplicationBase`, creates its main form from
`OnCreateMainForm`, and invokes its public `Run` method. It does not compile a
VB-generated `My` application or measure designer-generated startup code.

The splash comparison uses a 50 ms minimum display duration in both models,
rather than the legacy framework's two-second default. This keeps repeated
measurements practical while comparing the two mechanisms with the same
configured minimum. Network scenarios subscribe to the legacy
`NetworkAvailabilityChanged` event or to the registered
`IWinFormsNetworkAvailabilityService`; they measure subscription/startup and
shutdown overhead, not a simulated network transition.

The activation suite launches a primary and a secondary benchmark process for
each sample. It reports the time from secondary launch to the primary activation
callback and to secondary exit for the VB framework and builder models. These
are end-to-end activation measurements, including process and CLR startup; they
are not isolated named-pipe/IPC protocol costs. The primary also verifies that
the secondary's command-line argument arrived. The legacy model uses its
assembly-derived identity, while the builder receives a unique application ID
for each sample.

The infrastructure suite measures JSON configuration loading, options binding,
user-settings load/save and backup recovery, settings persistence resource
stability, logging dispatch and buffered-file flush, Generic Host construction
and hosted-service startup/shutdown, and health-check execution. It uses the
Application Builder settings service implementation in this checkout. The
health-check scenario measures a readiness check and a settings-store check.

## Run

From the repository root in PowerShell:

```powershell
$env:DOTNET_ROOT = "$PWD\.dotnet"
$env:PATH = "$PWD\.dotnet;$env:PATH"
dotnet run --configuration Release --project docs\application-builder\benchmarks\WinFormsApplicationBuilder.Benchmarks.csproj
```

Defaults are five cold process launches, five warmups plus 30 timed warm
cycles, five warmups plus 100 resource-stability cycles per scenario, and five
single-instance activation samples per model.
Iteration counts can be changed with `--cold-iterations`, `--warm-iterations`,
`--resource-iterations`, and `--activation-iterations`, respectively. For
example:

```powershell
dotnet run --configuration Release --project docs\application-builder\benchmarks\WinFormsApplicationBuilder.Benchmarks.csproj -- --cold-iterations 10 --warm-iterations 100 --resource-iterations 500 --activation-iterations 10
```

The infrastructure suite defaults to 30 iterations per operation and can be
changed independently with `--infrastructure-iterations`:

```powershell
dotnet run --configuration Release --project docs\application-builder\benchmarks\WinFormsApplicationBuilder.Benchmarks.csproj -- --cold-iterations 10 --warm-iterations 100 --resource-iterations 500 --activation-iterations 10 --infrastructure-iterations 100
```

## Measurements and interpretation

- **Cold process launch-to-exit** is parent-measured and includes process and
  CLR startup, WinForms initialization, form display, and shutdown. It is not
  an isolated `Run` call measurement.
- **Warm scenario-to-form-shown** measures scenario construction through the
  form's `Shown` event after in-process warmups.
- **Splash timing** is represented by the scenario-to-form-shown timing with
  the same configured 50 ms minimum duration in each model. The report does
  not subtract that configured delay to claim a pure splash rendering cost.
- **Activation callback and secondary exit** include secondary process/CLR
  startup and the complete activation path. The harness does not expose or
  isolate raw IPC serialization, transport, or dispatch costs.
- **Warm form-closed-to-dispose** measures from `FormClosed` through message
  loop exit and builder/host disposal.
- **UI-thread allocated bytes** uses
  `GC.GetAllocatedBytesForCurrentThread`. It deliberately does not claim to
  include allocations on Generic Host worker threads.
- **Resource deltas** report changes in process handle count, thread count,
  private bytes, and post-full-GC managed live bytes relative to the preceding
  checkpoint, with four checkpoints during the default repeated-cycle run.
  One-time runtime, JIT, WinForms, and host caches may account for bounded
  initial growth; inspect whether counters continue increasing across later
  checkpoints and repeat runs before labeling growth a leak.
- **Infrastructure operation timing** reports median and p95 elapsed time over
  the configured iterations, with five warmups. Allocation is process-wide
  allocated bytes divided by iteration count, so it includes measurement and
  runtime work and is not a per-thread allocation profile. Host startup is
  timed separately from `StopAsync` plus disposal. The file logger flush
  includes provider/factory disposal and verifies the expected events reached
  the file.
- **Infrastructure resource deltas** are process-wide snapshots around each
  operation group; persistence stability reports post-GC checkpoints. They
  are diagnostic counters, not a precise per-operation attribution.

Results are comparative diagnostics, not pass/fail thresholds. No acceptable
overhead budget has been set by #14946. Record the commit, SDK/runtime, OS
build, architecture, power mode, iteration counts, and complete output when
comparing runs. Cold timings and private bytes are particularly sensitive to
machine load and should not be compared across different hardware as if they
were controlled.

The VB framework applies its own DPI and visual-style settings during
`Run`/`OnInitialize`; builder and conventional scenarios apply WinForms settings
once in the worker process before their measured cycles. This preserves each
model's normal initialization responsibilities, but means warm per-cycle
timings include more framework initialization work for the VB model. Cold
process timings include process startup for every model.

## Example diagnostic run

The following reduced parity run verified all lifecycle, splash, network
subscription, and activation scenarios on Windows x64. It is a harness smoke
run with only three warm measurements per scenario, not a stable performance
claim.

Command:

```powershell
$env:DOTNET_ROOT = "$PWD\.dotnet"; $env:PATH = "$PWD\.dotnet;$env:PATH"; dotnet run --configuration Release --no-build --project docs\application-builder\benchmarks\WinFormsApplicationBuilder.Benchmarks.csproj -- --cold-iterations 1 --warm-iterations 3 --resource-iterations 4 --activation-iterations 2 --infrastructure-iterations 3
```

Environment: .NET 12.0.0-alpha.1.26519.114, Microsoft Windows 10.0.26200,
x64 process, 8 logical processors. Warm medians:

| Lifecycle scenario | Shown ms | Shutdown ms | UI-thread bytes |
|---|---:|---:|---:|
| Conventional `Application.Run` | 9.42 | 2.31 | 8,256 |
| VB Application Framework | 13.43 | 1.76 | 7,912 |
| VB Application Framework + splash | 65.06 | 3.81 | 10,672 |
| VB Application Framework + network listener | 45.30 | 1.90 | 66,424 |
| Builder without host | 9.84 | 1.68 | 8,256 |
| Builder with empty host | 13.50 | 2.41 | 382,296 |
| Builder + splash | 81.57 | 3.38 | 13,128 |
| Builder + network listener | 44.78 | 2.99 | 440,048 |

The network-service scenarios include Generic Host/service setup in the
builder model, so their allocation values are not a direct comparison of the
legacy event and service implementations alone. Two activation samples per
model produced these medians (p95):

| Single-instance model | Launch to primary callback ms | Secondary launch to exit ms |
|---|---:|---:|
| Builder | 364.13 (386.96) | 391.00 (412.51) |
| VB Application Framework | 196.82 (198.37) | 196.87 (198.45) |

Cold process measurements used one launch per scenario and are not included as
comparative results. In an additional isolated 100-cycle resource run after
five warmups, handle counts plateaued at +3 for builder splash, +5 for VB
splash, and +1 for the builder without splash; the VB splash run also gained
one thread by its second checkpoint. Managed live bytes rose by about 1–12 KB
and then nearly plateaued across checkpoints. These short diagnostics cannot
establish a leak or a memory-efficiency claim; working-set/private-byte deltas
varied with process state.

A reduced run on Windows x64 with .NET 12.0.0-alpha.1.26480.102 and 8 logical
processors (`--cold-iterations 2 --warm-iterations 5
--resource-iterations 10 --infrastructure-iterations 30`) produced the
following infrastructure medians:

| Operation | Median |
|---|---:|
| Configuration JSON load | 501.20 us/op |
| Options bind and first resolution | 79.50 us/op |
| User settings load | 1,130.10 us/op |
| User settings atomic save | 16,451.20 us/op |
| Settings fault injection and backup recovery | 17,866.60 us/op |
| Logging through `NullLogger` (1,000 events) | 46.00 us/batch |
| Logging provider dispatch (1,000 events) | 174.50 us/batch |
| Buffered file logger flush (100 events) | 3,776.80 us/op |
| Generic Host build | 4,247.70 us/op |
| Generic Host `StartAsync` with validation | 610.00 us/op |
| Generic Host `StopAsync` plus disposal | 311.30 us/op |
| Health-check execution | 1,127.10 us/op |

These are short-run, machine-specific diagnostic measurements, not a stable
performance claim or acceptance threshold. The harness also prints p95,
allocation, and process-resource data; capture the complete output when
comparing runs.

## Decisions and limits

- A dedicated harness is used instead of BenchmarkDotNet because the measured
  unit of work owns the calling STA thread and runs a real WinForms message
  loop; each cold-start trial also needs a fresh process.
- The baseline uses the same form and automatic close behavior, while the two
  builder scenarios isolate the builder's own cost from adding Generic Host.
- `WindowsFormsApplicationBase` is measured through a C# subclass so the
  benchmark can share form creation, process orchestration, and reporting.
  Language-specific generated `My` startup code, `ApplicationEvents.vb`,
  settings integration, and unrelated `My` services are not measured.
- This benchmark measures lifecycle/resource costs, not full compatibility.
  The legacy framework's generated `My` startup/event plumbing and the
  builder's explicit service and application-ID configuration remain distinct
  migration surfaces; the benchmark does not establish behavioral parity for
  those unmeasured APIs.
- Splash timing uses an explicit 50 ms minimum in both APIs. It does not
  represent the VB framework's default two-second splash configuration.
- Network measurements cover listener subscription lifecycle only; they do not
  trigger network changes. Service startup is measured with a real system
  network monitor, so environment/network state can affect results.
- Single-instance timings compare public application-level behavior. The
  builder's IPC cost cannot be isolated from a public API benchmark without
  measuring internal implementation details; launch-to-callback and
  launch-to-exit are reported instead.
- The lifecycle benchmark's Generic Host is empty. The infrastructure
  benchmarks separately exercise a no-op hosted service and representative
  configuration, options, settings, logging, and health-check work; they do
  not measure application-specific UI construction or open modal dialogs.
- Infrastructure operations do not have separate traditional-WinForms
  baseline implementations. The conventional `Application.Run` scenario is
  the baseline for UI lifecycle overhead only.
- Resource snapshots are coarse process counters. They can reveal monotonic
  growth but do not identify an allocation site or replace a profiler.
- CI execution is intentionally not configured: timing and process-resource
  numbers are environment-sensitive and are not stable correctness assertions.
