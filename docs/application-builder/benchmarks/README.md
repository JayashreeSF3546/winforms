# WinForms Application Builder benchmarks

This standalone harness measures both complete STA UI-thread lifecycles and
Microsoft.Extensions infrastructure used by a WinForms application.

The lifecycle scenarios compare:

- Conventional `Application.Run(Form)`.
- `WinFormsApplicationBuilder` without a host.
- `WinFormsApplicationBuilder` with an empty Generic Host.

Each run uses a small minimized, off-screen form that closes itself after it is
shown. The harness runs each scenario in a separate child process for cold
launch measurements and for independent warm/resource sessions. This prevents
static initialization from one scenario from contaminating the others and
keeps all WinForms work on an STA thread. The benchmark project references the
WinForms implementation in this checkout.

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
cycles, and five warmups plus 100 resource-stability cycles per scenario.
Iteration counts can be changed with `--cold-iterations`, `--warm-iterations`,
and `--resource-iterations`, respectively. For example:

```powershell
dotnet run --configuration Release --project docs\application-builder\benchmarks\WinFormsApplicationBuilder.Benchmarks.csproj -- --cold-iterations 10 --warm-iterations 100 --resource-iterations 500
```

The infrastructure suite defaults to 30 iterations per operation and can be
changed independently with `--infrastructure-iterations`:

```powershell
dotnet run --configuration Release --project docs\application-builder\benchmarks\WinFormsApplicationBuilder.Benchmarks.csproj -- --cold-iterations 10 --warm-iterations 100 --resource-iterations 500 --infrastructure-iterations 100
```

## Measurements and interpretation

- **Cold process launch-to-exit** is parent-measured and includes process and
  CLR startup, WinForms initialization, form display, and shutdown. It is not
  an isolated `Run` call measurement.
- **Warm scenario-to-form-shown** measures scenario construction through the
  form's `Shown` event after in-process warmups.
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

## Example diagnostic run

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
