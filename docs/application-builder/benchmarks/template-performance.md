# Application Builder template performance

## Scope and method

The generated-template benchmark covers all nine supported templates. It
records warm-cache NuGet restore time, a clean initial build, clean publish
time, and publish-directory size. The five executable templates are published
as framework-dependent (FDD) and self-contained (SCD) `win-x64` applications;
the four library templates are published as libraries. Publish size is the
sum of file lengths in the output directory, in MiB (1 MiB = 1,048,576 bytes).
It does not include compression, installer overhead, or framework files
provided by an installed runtime for FDD deployments.

The runtime lifecycle harness uses the same small, hidden form for three
startup paths: conventional `Application.Run`, Application Builder without a
host, and Application Builder with an empty Generic Host. It reports cold
process-to-exit and warm form-shown timings, UI-thread allocated bytes, and
post-full-GC process snapshots (handles, threads, working set, private bytes,
managed live bytes, and loaded managed assembly count). It measures framework
startup paths, not the complete generated forms or Blazor's first rendered
frame.

Builder templates compile and publish against this checkout's reference
assemblies with `Private=false`, following the template validation setup.
Their deployment sizes therefore represent app-local payload and exclude the
WinForms framework assemblies supplied by the installed framework. The
in-development Builder APIs are not available in the installed runtime, so
the generated Builder applications were not launched.

## Environment and results

The template matrix ran on Windows 10.0.26200, x64, with 8 logical processors,
using SDK `12.0.100-alpha.1.26480.102`. NuGet packages were already present in
the shared package cache. Each operation was measured once; results are
diagnostic samples rather than statistically stable benchmarks.

| Template | Language | Deployment | Restore / RID restore (s) | Build (s) | Publish (s) | Output (MiB) |
|---|---|---:|---:|---:|---:|---:|
| `winforms` | C# | FDD | 0.49 / 0.53 | 1.58 | 1.04 | 0.09 |
| `winforms` | C# | SCD | 0.49 / 0.48 | 1.58 | 2.44 | 123.89 |
| `winforms` | VB | FDD | 0.70 / 0.69 | 3.41 | 0.98 | 0.09 |
| `winforms` | VB | SCD | 0.70 / 0.62 | 3.41 | 2.50 | 123.89 |
| `winforms-builder` | C# | FDD | 0.94 / 0.74 | 1.59 | 1.12 | 0.85 |
| `winforms-builder` | C# | SCD | 0.94 / 0.60 | 1.59 | 2.46 | 124.65 |
| `winforms-builder` | VB | FDD | 0.97 / 0.67 | 2.43 | 1.15 | 0.85 |
| `winforms-builder` | VB | SCD | 0.97 / 0.59 | 2.43 | 2.62 | 124.65 |
| `winforms-blazor` | C# | FDD | 2.18 / 0.72 | 3.41 | 1.47 | 4.06 |
| `winforms-blazor` | C# | SCD | 2.18 / 0.75 | 3.41 | 2.86 | 127.86 |
| `winformslib` | C# | Library | 0.58 / n/a | 1.01 | 0.82 | 0.02 |
| `winformslib` | VB | Library | 0.58 / n/a | 1.00 | 0.88 | 0.02 |
| `winformscontrollib` | C# | Library | 0.53 / n/a | 0.81 | 0.76 | 0.02 |
| `winformscontrollib` | VB | Library | 0.52 / n/a | 0.79 | 0.95 | 0.02 |

FDD/SCD application rows share the first restore and build values; the second
restore value is the RID-specific restore for that deployment mode. Library
rows have no RID-specific restore.

The lifecycle measurements used 5 cold launches, 5 warm iterations, and 100
resource cycles per scenario on the same machine. The benchmark output required
runtime `12.0.0-alpha.1.26513.107`, which was not installed. For this local
diagnostic run, a temporary copy of the benchmark output had its runtime
configuration pinned to the installed `12.0.0-alpha.1.26480.102`; the
repository build output was not modified. This patch-level runtime difference
and the short sample are limitations of the measurements.

| Startup path | Cold launch-to-exit median (ms) | Cold p95 (ms) | Warm form-shown median (ms) | UI-thread allocation median (B) | End handles | End threads | End working set (MiB) | End private bytes (MiB) | End managed live bytes (B) | Assemblies |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `Application.Run` | 453.43 | 2,817.31 | 14.55 | 6,144 | 312 | 13 | 41.83 | 12.52 | 302,800 | 35 |
| Builder, no host | 472.23 | 516.71 | 14.35 | 7,184 | 312 | 13 | 41.46 | 11.64 | 320,352 | 35 |
| Builder with empty Generic Host | 621.34 | 997.11 | 22.21 | 285,088 | 375 | 20 | 53.61 | 17.85 | 376,744 | 66 |

The host scenario's handle count rose during the initial resource checkpoint
and remained stable through cycles 50, 75, and 100. Managed live bytes and
assembly counts also stayed nearly constant after initial warmup. These short
runs are not leak certification and should be repeated under controlled
conditions before drawing conclusions about small timing or memory differences.

## Interpretation and follow-up

The generated classic templates have a very small FDD payload, while their
SCD output is dominated by the included runtime. Builder and Hybrid FDD output
is larger than classic output because the templates carry additional
application files and dependencies. The Blazor Hybrid output is the largest
because it includes the WebView integration and web assets. These sizes exclude
framework assemblies supplied by an installed runtime; they are not directly
comparable with packages that deploy those assemblies app-locally.

The isolated lifecycle benchmark measures the baseline and Builder startup
patterns but does not measure the complete generated Builder UI or the Blazor
WebView render path. Runtime measurements of those generated applications
should be repeated when the Application Builder runtime is available as an
installable framework/package. No performance budget is defined, and timing,
working-set, and private-byte values remain environment-sensitive diagnostics.
