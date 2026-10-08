# WinForms Application Builder samples

These small applications demonstrate the current in-repository Application
Builder API together with Generic Host. Each sample includes a default
startup-form path, an alternate custom `ApplicationContext` path, and a
cancellable `BackgroundService`. Closing the form asks the runtime to stop the
host; the host's stopping token cancels the background service before the
message loop unwinds.

The sample projects reference `src\System.Windows.Forms\System.Windows.Forms.csproj`
so they can be built against the implementation in this checkout. They target
the repository's current .NET preview and are examples, not installed SDK
templates.

## Run

Build the samples from the repository root:

```powershell
dotnet build docs\application-builder\samples\CSharp\WinFormsApplicationBuilder.CSharp.csproj
dotnet build docs\application-builder\samples\VisualBasic\WinFormsApplicationBuilder.VisualBasic.vbproj
```

Run either executable without arguments to select a startup form by type.
Pass `--custom-context` to select the sample's `ApplicationContext` instead.
The background service writes heartbeat messages to the debugger output;
closing the form requests coordinated host shutdown.

## Exploratory validation matrix

The matrix records manual validation dimensions requested by #14945. Both
languages and startup modes were launched in the available local console
session. Other environment-specific GUI runs remain unverified and must not be
inferred from successful builds.

| Environment | Architecture | Theme | Hardware | Result |
|---|---|---|---|---|
| Local Console session | x64 | Light | Current machine | C# and VB; both startup modes started and shut down with exit code 0 |
| Local Console session | x64 | Dark | Current machine | Not run |
| Terminal Services / Remote Desktop session | x64 | Light and Dark | Remote session | Not run |
| Windows on ARM | ARM64 | Light and Dark | ARM device | Not run |
| Local or remote Windows session | x64 / ARM64 | Light and Dark | Slow or constrained hardware | Not run |

For each manual run, verify both startup modes, confirm the heartbeat ceases
after closing the window, check that the process exits, and note whether the
window remains responsive during host shutdown. Record OS build, architecture,
session type, theme, hardware, and observed result when updating this matrix.

## DI and scopes sample (#14953)

The DI/scopes sample has separate C# and Visual Basic projects. Build and run
either from the repository root:

```powershell
dotnet build docs\application-builder\samples\DiScopes\CSharp\WinFormsApplicationBuilder.DiScopes.CSharp.csproj
dotnet run --project docs\application-builder\samples\DiScopes\CSharp\WinFormsApplicationBuilder.DiScopes.CSharp.csproj
dotnet build docs\application-builder\samples\DiScopes\VisualBasic\WinFormsApplicationBuilder.DiScopes.VisualBasic.vbproj
dotnet run --project docs\application-builder\samples\DiScopes\VisualBasic\WinFormsApplicationBuilder.DiScopes.VisualBasic.vbproj
```

The startup Form and each activated modeless Form or modal dialog receive a
distinct scoped `SessionService` through constructor activation. The startup
Form explicitly assigns its service to a Designer-created UserControl and
component; those descendants do not own scopes. Choose **Open modeless Form**
to see independent Form-scope identities. **Review navigation** opens a
modal dialog: **Continue** opens the modeless Form, while **Cancel** leaves
the current screen unchanged. Creation, component disposal, scope disposal,
and hosted-service cancellation messages are written to the debugger output.
Close each window to observe its scope cleanup.

The following exploratory compatibility checks are not implied by a successful
build. Record the IDE/OS version and actual result after running each scenario:

| Scenario | Environment | Result |
|---|---|---|
| Designer load, save, reload, rename, delete, undo/redo, inherited Forms and controls | Visual Studio Designer on Windows | Not run |
| Classic, Light, and Dark themes | Windows x64 | Not run |
| Classic, Light, and Dark themes | Terminal Services / Remote Desktop, x64 | Not run |
| Classic, Light, and Dark themes | Windows on ARM, ARM64 | Not run |
| Layout and close responsiveness | Slow or constrained Windows hardware | Not run |

The repeatable scope/resource stress-test methodology and its measurements
are documented in [`../di-scope-leak-testing.md`](../di-scope-leak-testing.md).

## Infrastructure sample (#14962)

The infrastructure sample has separate C# and Visual Basic projects. Build and
run them from the repository root:

```powershell
dotnet build docs\application-builder\samples\Infrastructure\CSharp\WinFormsApplicationBuilder.Infrastructure.CSharp.csproj
dotnet run --project docs\application-builder\samples\Infrastructure\CSharp\WinFormsApplicationBuilder.Infrastructure.CSharp.csproj
dotnet build docs\application-builder\samples\Infrastructure\VisualBasic\WinFormsApplicationBuilder.Infrastructure.VisualBasic.vbproj
dotnet run --project docs\application-builder\samples\Infrastructure\VisualBasic\WinFormsApplicationBuilder.Infrastructure.VisualBasic.vbproj
```

The Generic Host supplies the default `appsettings.json`,
`appsettings.{Environment}.json`, Development User Secrets, environment
variables, and command-line configuration providers. The sample validates
`Sample` options before the window is shown, enables structured host logging
and opt-in WinForms/CLR exception logging, reloads options when JSON
configuration changes, and runs a cancellable `BackgroundService`. **Save user
settings** persists preferences in the current Windows user's local
application data directory; settings are projected read-only under the
`UserSettings` configuration section. Schema version 2 includes a migration
from a version 1 `themeName` preference. **Run health checks** checks whether
that user's settings store is readable. Closing the window requests coordinated
host shutdown and cancels the background worker.

The project copies both appsettings files to build and publish output. It does
not write configuration or user data into the installation directory. The
development User Secrets ID is sample-only; secrets are not logged. The default
host logging providers are used, with no automatic file or network logging.
The sample opts into WinForms exception logging, which handles UI-thread
exceptions after logging them instead of showing the default WinForms exception
dialog; applications needing user-facing recovery UI should provide their own
exception handler.
To verify Development User Secrets without displaying or logging the value,
run with `DOTNET_ENVIRONMENT=Development` and set
`Sample:DiagnosticToken` for the project; the form reports only whether a
value is configured. For example, from PowerShell:

```powershell
$env:DOTNET_ENVIRONMENT = "Development"
dotnet user-secrets set "Sample:DiagnosticToken" "local-test-value" --project docs\application-builder\samples\Infrastructure\CSharp\WinFormsApplicationBuilder.Infrastructure.CSharp.csproj
dotnet run --project docs\application-builder\samples\Infrastructure\CSharp\WinFormsApplicationBuilder.Infrastructure.CSharp.csproj
```

### Exploratory deployment matrix

The following cases are deployment checks, not automated test claims. Build
results are recorded separately below; GUI, multi-account, and remote-session
checks require running the sample in the stated environment.

Representative C# publish commands:

```powershell
dotnet publish docs\application-builder\samples\Infrastructure\CSharp\WinFormsApplicationBuilder.Infrastructure.CSharp.csproj -r win-x64 --self-contained false
dotnet publish docs\application-builder\samples\Infrastructure\CSharp\WinFormsApplicationBuilder.Infrastructure.CSharp.csproj -r win-x64 --self-contained true
```

| Scenario | Repeatable check | Result in this environment |
|---|---|---|
| Unpackaged C# and VB | Build and launch each project from its output directory; save settings and close the form. | Both projects build with 0 warnings and 0 errors; GUI run not performed. |
| Framework-dependent publish | Publish for `win-x64` with `--self-contained false`, then launch from the publish directory. | C# publish succeeded; GUI launch not performed. |
| Self-contained publish | Publish for `win-x64` with `--self-contained true`, then launch from the publish directory. | C# publish succeeded; GUI launch not performed. |
| Multiple Windows users | Run under two Windows accounts; save different themes and compare each account's local-app-data settings file. | Not run; a second account was unavailable. |
| Offline operation | Disconnect network access, launch, save preferences, and run health checks. The sample has no network-dependent service or health check. | Not run in a disconnected session. |
| Read-only installation directory | Deny writes to the unpacked/publish directory, then launch, save settings, and confirm the settings remain in local application data. | Not run; the sample is designed not to write to the installation directory. |
| Terminal Services / Remote Desktop | Launch in a remote session, save settings, close the window, and confirm the worker stops and the session remains responsive. | Not run; no remote session was available. |
| Configuration reload | Edit `appsettings.json` in the output directory; verify the displayed configuration and structured worker log change without restarting. | Not run interactively. |
| Settings upgrade | Replace the per-user file with schema version 1 containing `themeName`; launch and verify schema version 2 is saved and a `.bak` is retained. | Not run interactively. |

For a settings-upgrade check, use
`%LOCALAPPDATA%\WinForms\WinFormsApplicationBuilder.InfrastructureSample\user-settings.json`
for C# or
`%LOCALAPPDATA%\WinForms\WinFormsApplicationBuilder.InfrastructureSample.VisualBasic\user-settings.json`
for Visual Basic, and preserve a copy before editing it. The migration renames
`themeName` to `theme` and supplies a default for `showTips`. Do not edit
settings while another sample instance is running.

## Decisions and scope

- The samples use the existing `IHost` integration and do not add a
  hosted-service abstraction to WinForms or change the runtime API.
- The default startup-form mode is the normal path. The `--custom-context`
  switch keeps the context example runnable without maintaining a second
  application entry point.
- Cancellation is demonstrated through the `BackgroundService` stopping token;
  stopping remains coordinated by the runtime rather than by a second message
  loop or a synchronous wait on the UI thread.
- Terminal Services, Windows on ARM, theme, and slow-hardware validation
  require environments not available to the local build and unit-test run.
