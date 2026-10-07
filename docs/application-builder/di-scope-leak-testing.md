# Application Builder DI scope leak stress test

The `WinFormsApplicationLeakTests` fixture compares a plain WinForms baseline
with repeated modeless and modal activations through `WinFormsApplication`.
Each activated form owns a nested Panel/Control tree and a container-owned
Component. The scoped cases also resolve an `IDisposable` probe from the
activation provider.

## Reproduce

Run the focused test from the repository root:

```powershell
$env:DOTNET_ROOT = "$PWD\.dotnet"
$env:PATH = "$PWD\.dotnet;$env:PATH"
dotnet test src\test\unit\System.Windows.Forms\System.Windows.Forms.Tests.csproj --no-restore -- --filter-class "System.Windows.Forms.Tests.WinFormsApplicationLeakTests" --show-live-output on --output Detailed --parallel none
```

The fixture runs in a non-parallel xUnit collection so unrelated UI tests do
not perturb process-wide handle measurements. It performs 20 warm-up
activations, then measures 1,000 baseline Forms, 1,000 scoped modeless Forms,
and 250 scoped modal Forms. It samples after every 100 measured activations.
All activation, close, and disposal operations run on the WinForms UI thread;
the test uses no sleeps or elapsed-time pass/fail threshold.

## Measurements and assertions

- The baseline constructs the same nested controls and component without an
  Application Builder activation scope. The proposed paths use `CreateForm`
  and `ShowDialog` respectively.
- Scope probes count creation and disposal. Controls and Components record
  disposal, including whether their scoped probe is still alive at that point.
- Weak references cover each 100th disposed Form, nested Panel and Control,
  container-owned Component, and scoped probe. The fixture forces collection
  and checks that sampled objects are no longer retained while the application
  and its message loop are still alive. This also detects stale application-
  side form tracking/event subscriptions.
- Each resource sample records retained managed bytes, process handle count,
  and GDI/USER object counts. After warm-up, the test allows at most 16
  additional process handles and 4 additional GDI or USER objects. The
  tolerance accommodates small one-time runtime caches while still detecting
  an increasing resource trend across batches.
- Managed bytes may grow by at most 1 MiB over the post-warm-up baseline.
- Test output includes each sample and throughput for the baseline and both
  scoped paths. Throughput has no machine-dependent pass/fail threshold.

## Recorded local run

The focused stress test passed on Windows 11 Enterprise (build 26200, x64)
with the repository's .NET 12.0.0-alpha.1.26480.102 runtime.

| Scenario | Measured activations | Result |
|---|---:|---|
| Plain WinForms baseline | 1,000 | Disposal and resource-growth assertions passed |
| Scoped modeless Forms | 1,000 | Scopes/probes disposed; sampled objects collectible; resource-growth assertions passed |
| Scoped modal Forms | 250 | Scopes/probes disposed; sampled objects collectible; resource-growth assertions passed |

Across the run, 1,291 scopes and probes were created and disposed, and 2,310
Forms, nested Controls, and Components were disposed. All 105 weak-reference
sentinels were collectible before the startup Form closed. Managed-memory,
process-handle, GDI, and USER samples stayed within the bounds above.

Record the OS/runtime and test output when comparing runs. Throughput and
managed-byte values are machine-specific; the relevant invariant is that
sampled objects are collectible, every activation probe and owned descendant
is disposed, and native-resource counts remain bounded after warm-up.
