# Application Builder adoption samples

These are focused, runnable examples for
[dotnet/winforms#14968](https://github.com/dotnet/winforms/issues/14968), not new
`dotnet new` templates or a framework applications must adopt.

## What each sample teaches

- **Migration:** use the same existing Form, Designer code, and button handler
  with classic startup, the builder alone, and then the coordinated host.
  The C# and VB projects each retain their own native-language `LegacyForm`.
  The example does not rewrite Designer files, regenerate applications, migrate
  stored settings, or enable the VB Application Framework.
- **Enterprise:** separate the presentation project from the UI-free services
  project. A singleton query creates an operation scope, invokes scoped business
  services/repositories, materializes detached results, and disposes that scope
  before returning. Validated configuration selects a department; logging uses
  the host provider. The in-memory repository stands in for a database session,
  not a prescribed persistence technology or production enterprise framework.
- **Tray:** a real custom `ApplicationContext` has **no `MainForm`**. It owns
  the icon, menu and optional window. Closing the window does not exit the
  application. Both the Exit command and host-origin stop release all owned UI.
- **Backend:** an ordinary `BackgroundService` polls an offline source by
  default. The worker publishes UI-free snapshots; a Form-owned timer reads
  them on the UI thread. Optional HTTP HEAD requests demonstrate a transport
  boundary without downloading data or enrolling in a cloud service. Expected
  HTTP failures are visible and do not disable the UI.

All eight applications have explicit C#/VB STA entry points. **Enterprise,
tray and backend share C# composition and code-built C# UI**, as well as C#
services; their VB projects are mixed-language entry-point examples, not
all-VB implementations. Migration has both native C# and native VB Forms.
VB Application Framework event/single-instance parity remains
[dotnet/winforms#14972](https://github.com/dotnet/winforms/issues/14972).

## Files and ownership

- `Migration`, `Enterprise`, `Tray`, `Backend`: separate C# and VB executable
  projects and their entry points; applicable sample-local configuration.
- `EntryPoint.props`: common executable properties, reference to presentation,
  explicit VB startup options, and executable-directory configuration copying.
- `Presentation\*Sample.cs`: four separate composition examples, not a generic
  mode-switching lifetime engine. Hosting is selected here, not in the Forms.
- `Presentation\SampleEntryPoint.cs`: common native UI initialization and
  recoverable startup/run error reporting. It is not a native-callback recovery
  policy; do not throw arbitrary exceptions from Form events.
- `Presentation\SampleProbe.cs`: opt-in process-test commands and observable
  lifecycle diagnostics. With no `Sample:Scenario`, applications wait for
  ordinary user interaction.
- `Services\EnterpriseServices.cs`: scoped repository/business services,
  detached results, options, logging and ownership counters.
- `Services\BackendServices.cs`: offline/HTTP adapters, validated options,
  thread-safe snapshots and cancellation-aware polling. This project targets
  plain .NET and does not reference WinForms or WindowsDesktop.
- `..\UIIntegrationTests\WinFormsAdoptionSampleTests.cs`: real-process checks,
  including original event handlers, scope disposal, tray window independence,
  pending-read cancellation, invalid startup configuration, and a test-owned
  loopback server returning HTTP 200 or 503.

There is one root provider and one UI loop per application, no `Host.Run`,
Form-local provider, internal lifetime API, or Hybrid dependency. Background
work never captures a Form, Control, or UI synchronization context. Host shutdown
joins cooperative backend work before provider disposal. This example explicitly
uses an infinite host shutdown timeout: a replacement backend **must honor
cancellation**, or shutdown can wait indefinitely. It is not a general solution
for non-cooperative transports.

Configuration loads from the executable directory, not the current directory;
command-line settings override JSON. Configuration watchers are disabled for
these examples. Existing legacy settings need their own reviewed migration
policy; none of these examples resets or moves user settings.

## Build and run

Use the repository's `build.cmd`, SDK and preview runtime. The external artifact
directory below also works when the checkout drive has little free space.

```powershell
$artifacts = Join-Path $env:TEMP 'WinForms14967Artifacts'
.\build.cmd -projects src\test\integration\UIIntegrationTests\System.Windows.Forms.UI.IntegrationTests.csproj `
    -verbosity minimal -binaryLogName "$artifacts\AdoptionSamples.binlog" "/p:ArtifactsDir=$artifacts"
$env:DOTNET_ROOT = Join-Path $PWD '.dotnet'
$env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
$bin = Join-Path $artifacts 'bin'
$tfm = 'net12.0-windows7.0' # Use the repository train's actual output TFM.

# Change CSharp to VisualBasic to run the corresponding VB entry point.
& "$bin\ApplicationBuilderAdoption.Migration.CSharp\Debug\$tfm\ApplicationBuilderAdoption.Migration.CSharp.exe" --Migration:Stage=builder
& "$bin\ApplicationBuilderAdoption.Enterprise.CSharp\Debug\$tfm\ApplicationBuilderAdoption.Enterprise.CSharp.exe"
& "$bin\ApplicationBuilderAdoption.Tray.CSharp\Debug\$tfm\ApplicationBuilderAdoption.Tray.CSharp.exe"
& "$bin\ApplicationBuilderAdoption.Backend.CSharp\Debug\$tfm\ApplicationBuilderAdoption.Backend.CSharp.exe"
```

Applications intentionally use `Exe`, so console diagnostics accompany the
native UI. Run each command separately and exit it before starting the next.
In Visual Studio, launch using `start-vs.cmd`, select an
`ApplicationBuilderAdoption.*.CSharp` or `.VisualBasic` executable under the
existing integration solution folder, and **Set as Startup Project**. Add the
arguments below to that project's debug command-line arguments. Do not select
`Presentation` or `Services` as the startup project.

## Manual GUI checks

Do not pass `Sample:Scenario` for these checks: that option automates interaction.
Repeat with both language entry points.

1. **Migration:** run with `--Migration:Stage=classic`. Click Increment twice;
   verify `Count: 2`, then close the Form. Repeat with `builder` and `hosted`.
   The same native-language Form and handler must work in all three stages.
   Only hosted startup prints `HOST STARTED` / `HOST STOPPED`; the UI is disposed
   before the host stops.
2. **Enterprise:** click **Load open orders**. Operations shows two open orders,
   not the closed scanner order. Click again: no duplicate rows. Close and
   restart with `--Enterprise:Department=Finance`; one supplier invoice appears.
   An unknown department must report a startup failure without showing a Form.
3. **Tray:** find **Application Builder tray sample** in the notification area
   (including Windows' hidden-icons overflow). Double-click or right-click
   **Open window**. Close its X: the icon and process remain active. Reopen it,
   then choose **Exit**: icon, window and process disappear. Restart and repeat
   with **Stop host**, including while the optional window remains open.
4. **Backend:** verify `Offline snapshot ...` updates and the click counter
   remains responsive. Close normally. Restart with
   `--Backend:OfflineDelayMilliseconds=30000`: while it says Waiting, click the
   counter, then close; it must cancel promptly rather than wait 30 seconds.
   Repeat using **Stop host** instead of X. Diagnostics must show worker stop
   before worker disposal, with no background update of a disposed Form.
5. **Optional network boundary:** only if wanted, set `--Backend:Offline=false`
   and `--Backend:Endpoint=https://your-controlled-host`. A successful HEAD
   produces an HTTP status; unavailable/unsuccessful endpoints leave the click
   counter usable and display an unavailable/timeout status. HTTP is allowed
   only for loopback development endpoints. URLs with credentials, query
   strings or fragments are rejected; never place secrets in sample files.
   No public endpoint, account, API key, cloud SDK or network is required for
   the default sample or offline tests.

## Automated checks and remaining gates

```powershell
& "$bin\System.Windows.Forms.UI.IntegrationTests\Debug\$tfm\System.Windows.Forms.UI.IntegrationTests.exe" `
    --filter-class '*WinFormsAdoptionSampleTests' --parallel none --report-trx `
    --results-directory "$artifacts\TestResults\AdoptionSamples"
```

Local prototype verification: the targeted Arcade build above passed with zero
warnings/errors, and all 32 process cases passed. These include stop requested
during startup, before Form activation, as well as cancellation of an active
offline read. Build logs, binary log and TRX results are retained in the external
artifact directory used by these commands.

The test-project references include all eight applications in the existing
integration build graph. Tests have process watchdogs, remove ambient sample
configuration overrides, and run from an unrelated working directory. HTTP
failure checks use only their own temporary loopback listener.

These tests do not establish remote CI, actual VS Designer editing, taskbar
shell behavior on every Windows version, real-cloud authentication, database
deployment, ARM64/DPI/accessibility qualification, or published/package-consumer
support. Broader IDE/deployment acceptance remains
[dotnet/winforms#14970](https://github.com/dotnet/winforms/issues/14970);
template install/create/publish automation is separately
[dotnet/winforms#14969](https://github.com/dotnet/winforms/issues/14969).

The deliberate alternatives were additional sample executables rather than
maintained template families; a small UI-free service project rather than a
prescriptive architecture framework; short operation scopes rather than
requiring pending scoped-startup APIs; and offline adapters rather than default
cloud enrollment. These samples use public repository prototype APIs. They
neither change public API nor claim the prototype packages have shipped.
