# Application Builder lifetime samples

These C# and Visual Basic applications demonstrate the prototype APIs from
[dotnet/winforms#14945](https://github.com/dotnet/winforms/issues/14945).
They reference this checkout's WinForms implementation, not an installed WindowsDesktop version.
The public API remains subject to upstream approval.

## Scenarios

Both projects accept the same arguments:

| First argument | Behavior |
|---|---|
| `form` (default) | `UseStartupForm<MainForm>()` defers form construction until `Run`. Close the window to stop. |
| `context` | `UseApplicationContext` creates a context on the owning STA. **Continue** replaces its main form before closing the original; closing the second window ends the run. |
| `background` | A real Generic Host `BackgroundService` waits for UI dispatch, performs five background updates, then calls `StopApplication`. Close early to cancel its work. |
| `cancellation` | **Cancel application** cancels the token passed to `Run`. Cancellation is reported only after UI cleanup and stopped notification. |
| `shutdown` | **Stop host** calls the coordinated `Host.StopAsync`. Ordinary window closing is initially vetoed; committed host shutdown still completes cleanup. |

Optional arguments follow the scenario:

| Option | Effect |
|---|---|
| `--dark` | Configure Dark Mode before constructing controls. Without it, use classic light mode. |
| `--component-manager` | Select the component-manager backend instead of the default light message loop. |
| `--smoke` | Dispatch an automatic scenario-specific shutdown, without user input. Background work omits its demonstration delays. |

The entry point is synchronous and marked `STAThread` in both languages. Application configuration
runs before any controls or native handles exist. Visual Basic uses an explicit `Sub Main`, with
`MyType=Empty`, rather than the separate VB application-framework driver.

Lifecycle and cleanup markers are written to standard output. Redirect output to capture a transcript;
the applications are Windows GUI executables and do not allocate a console.

## Build and run

From the repository root on Windows:

```powershell
.\build.cmd -projects src\test\integration\UIIntegrationTests\System.Windows.Forms.UI.IntegrationTests.csproj -verbosity minimal
```

The integration project builds both samples through project references. They are also listed under
the solution's integration folder. For an individual sample, pass its `.csproj` or `.vbproj` path to
`build.cmd -projects`.

Use the repository-local runtime, and replace `net11.0` if `NetCurrent` changes:

```powershell
$env:DOTNET_ROOT = Join-Path $PWD '.dotnet'
& .\artifacts\bin\WinFormsApplicationBuilder.CSharp\Debug\net11.0\WinFormsApplicationBuilder.CSharp.exe form
& .\artifacts\bin\WinFormsApplicationBuilder.VisualBasic\Debug\net11.0\WinFormsApplicationBuilder.VisualBasic.exe context --dark
& .\artifacts\bin\WinFormsApplicationBuilder.CSharp\Debug\net11.0\WinFormsApplicationBuilder.CSharp.exe shutdown --component-manager --smoke | Out-Host
```

`background` needs no form activation through DI: the sample registers only its background service.
The real host is supplied with `UseHost(IHostBuilder)` and remains driven by the application.
Do not also call `Host.RunAsync` or start another message loop.

Never synchronously wait for a stop task in a UI event handler, or capture a continuation to a
dispatcher that is terminating. The shutdown sample stores its task and observes it after `Run`
returns. A canceled `StopAsync` token would cancel only the caller's wait, not underlying shutdown.
The cancellation sample instead cancels the **run** token to demonstrate application cancellation.
Unexpected exceptions are not caught or converted into successful exit.

## Automated coverage

```powershell
$env:DOTNET_ROOT = Join-Path $PWD '.dotnet'
& .\artifacts\bin\System.Windows.Forms.UI.IntegrationTests\Debug\net11.0-windows7.0\System.Windows.Forms.UI.IntegrationTests.exe --filter-class 'System.Windows.Forms.UITests.WinFormsApplicationSampleTests' --no-progress --report-trx
```

The 40 cases cover both languages, all five scenarios, both loop backends, and light/dark settings.
Each launches the actual executable in a separate process, captures errors and its exit code, and
verifies dispatch, once-only lifecycle milestones, UI disposal, cancellation completion, and host
stopped ordering where applicable. Child processes have a failure watchdog; test success does not
depend on elapsed-time thresholds, sleeps, or the background demonstration's one-second intervals.

The existing public x64/x86 Debug/Release CI legs discover these integration tests. Normal builds
also include the sample projects for arm64, but the existing pipeline does not run arm64 tests.
No new pipeline or shipping package is introduced. The samples reuse the repository's existing
development-only `Microsoft.Extensions.Hosting` dependency.

## Environment acceptance

Automated smoke coverage does not establish visual correctness or interactive responsiveness.
For each language and scenario, repeat with and without `--dark` and `--component-manager`:

| Environment | Required checks |
|---|---|
| Local x64 desktop | Move/resize the window during background work; exercise every button and window close; verify no modal errors or leftover windows. |
| Terminal Services session | Run inside the actual session; confirm the transcript reports Terminal Services. Repeat closing, switching forms, cancellation, and host stop; disconnect/reconnect while background work is active. |
| Native Windows on ARM | Build with `/p:TargetArchitecture=arm64`, run on ARM hardware, and confirm the transcript reports Arm64. Cross-compilation or x64 emulation is not native execution evidence. |
| Slow hardware or constrained VM | Repeat without `--smoke`; confirm dispatch and interaction remain responsive during background work and cleanup. Record the machine/session configuration rather than imposing a universal time limit. |
| Light and Dark Mode | Check readable text, action buttons, checkbox state, focus indication, DPI scaling, and disposal of both context windows. High Contrast may intentionally override the requested palette. |

Remote CI, Terminal Services, native ARM, slow-hardware checks, and interactive visual review
require explicit execution evidence; their inclusion here is not a claim that they passed.

Local verification on an x64 local desktop completed with a warning-free/error-free targeted
Arcade build, all 40 sample smoke cases plus six existing UI lifetime regressions passing, and
both languages' normal five-update background scenarios exiting successfully. This does not
replace the remaining environment and visual acceptance checks above.

## Scope decisions

Two executables keep complete C# and VB entry points visible without a shared implementation hiding
language-specific startup. Command-line scenarios avoid five separate project scaffolds per language.
Task gates and posted callbacks establish smoke-test ordering; the background delays exist only to
make manual responsiveness inspection possible.

No product changes, general DI form-activation mechanism, hosted-service abstraction, template
changes, benchmark harness, or dedicated UI-thread wrapper are added. Startup/shutdown performance
measurements remain assigned to dotnet/winforms#14946.
