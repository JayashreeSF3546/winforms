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
