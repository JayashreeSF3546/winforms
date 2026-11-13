# Generated-project exploratory testing

This is the hands-on acceptance runbook for
[dotnet/winforms#14970](https://github.com/dotnet/winforms/issues/14970).
Use C# and VB basic templates. Hybrid remains activation-gated and cannot be
included in browser/runtime acceptance until its public lifecycle dependencies
are available. These checks do not change template payloads or shipping APIs.

## Prepare an isolated workspace

First run the [lifecycle matrix](README.md#automated-lifecycle-matrix). Use its
successful retained directory as `$lifecycle`; do not reuse an incomplete run.
This keeps prototype package/runtime provenance and avoids another independent
restore environment with different dependencies.

From the repository root:

```powershell
$artifacts = Join-Path $env:TEMP 'WinForms14967Artifacts'
$lifecycle = Join-Path $env:TEMP 'WinForms14969\402e12d8975a4a7d8151d8fca8010948'
.\build.cmd -projects pkg\Microsoft.Dotnet.WinForms.ProjectTemplates\tests\ExplorationDriver\TemplateExplorationDriver.csproj `
    -verbosity minimal -binaryLogName "$artifacts\TemplateExploration-Driver.binlog" "/p:ArtifactsDir=$artifacts" `
    /p:MicrosoftNETCoreAppRefPackageVersion=12.0.0-alpha.1.26480.102

.\pkg\Microsoft.Dotnet.WinForms.ProjectTemplates\tests\New-TemplateExploration.ps1 `
    -LifecycleOutput $lifecycle `
    -PrototypeWinFormsDirectory "$artifacts\bin\System.Windows.Forms\Debug\net12.0" `
    -Driver "$artifacts\bin\TemplateExplorationDriver\Debug\net12.0-windows\TemplateExplorationDriver.exe"
```

The old retained path is an example, not a prerequisite: replace it with a new
successful lifecycle run if it no longer exists. Copy the workspace path printed
by the preparation command. The UI Automation driver has no product project
references and is explicitly built against the installed SDK/Desktop baseline
above; the launched prototype applications retain their own newer Core runtime
requirement. This avoids installing or changing machine-wide runtimes. Update
that explicit version when moving the SDK/Desktop train. The workspace contains:

- `TemplateExploration.slnx`: C#/VB copies for editing, without copied `bin`/`obj`.
- Workspace-level `Directory.Build.props` / `.targets`: explicit imports of the
  external prototype harness. Original generated project files are unchanged.
  This is **not** normal consumer packaging or proof that an unmodified SDK
  supports these APIs.
- `global.json`: the repository's exact SDK, resolved from the local installation.
  It does not install an SDK, change global SDK selection, or override VS support.
- `NuGet.Config`: no feeds. The workspace shares the successful run's private
  package cache. NuGet auditing is explicitly off here to avoid network-dependent
  advisory requests; this is a test workspace, not a production security policy.
- `Open-PowerShell.cmd`: double-click to open PowerShell in this workspace with
  the required runtime root and SDK path set.
- `evidence\automated-results.json` and command logs: actual local accessibility
  observations; `provenance.json` records the originating run and source hashes.
- `evidence\manual-results.csv`: all manual rows start as **NotRun**, never Passed.

The preparation command opens and closes only its own test applications. It
does not launch or attach to Visual Studio, install templates into the developer's
catalog, change Windows themes, change DPI/display settings, disconnect sessions,
modify firewall policy, or request elevation. Do not interact with its temporary
test windows. Original lifecycle source is hash-checked after the probes.

## What the automated probes actually establish

For each language, the driver uses the native framework-dependent published
application with its **real entry point**, not a linked replacement startup.

- **Window:** a UI Automation client finds the configured window name belonging
  to the launched PID, observes a visible/enabled Window, records its actual DPI,
  closes through `WindowPattern`, and verifies exit zero.
- **Startup error:** an empty title activates the real startup error dialog.
  The client checks that validation text is accessible and its acknowledgment
  button has a name, is enabled/visible/keyboard-focusable and supports
  `InvokePattern`. Invoking it must dismiss the dialog and produce exit one.

Reports record native/process architecture, remote-session state and high
contrast state without changing them. These are **observations of this run**.
UI Automation properties are not proof that Narrator works, that real keyboard
input works, that a theme looks correct, or that a per-monitor DPI transition is
correct. The generated default Form is empty; it has no meaningful tab order or
business commands to test until controls are added in the Designer exercise.
The driver uses WPF only for Microsoft's UI Automation client assemblies; this
does not add WPF to generated WinForms applications.

## Record real manual results

For every applicable row in `evidence\manual-results.csv`, use `Passed`,
`Failed`, `Blocked`, or `NotRun`. Supply an evidence-file path and concise
observations: machine/OS/SDK/VS versions, language, application commit, deployment
mode, DPI/theme/session and steps. Record only non-sensitive test data.
Do not mark a whole category Passed based on one smaller automated probe.
For categories with multiple conditions, attach a checklist showing each result;
mark the category Passed only if all required conditions passed.

Keep screenshots, screen-reader observations, build/debug output and diffs in
the evidence directory. A missing SDK, unsupported Designer, inaccessible test
machine or unavailable installer is **Blocked**, not Passed. A crash, wrong
announcement, clipped UI, duplicate event handler, leaked process or failed
shutdown is **Failed**. Preserve logs and reproduction steps; do not hide the
failure by disabling the host, rewriting startup or removing configuration.

## Visual Studio discovery and creation

CLI custom hives do not populate Visual Studio's New Project catalog.
Opening the prepared solution is a different check from IDE template discovery.

Use a **disposable Windows profile/VM** for installation and discovery. Do not
modify the normal developer's template installation just to make this row pass.
Use the VS-supported template extension/registration procedure appropriate to
that VS version; if only the CLI NuGet artifact is available and no supported VS
registration exists, record discovery **Blocked** with that reason.

In the provisioned environment:

1. Launch the matching supported VS version; record its full version and SDK
   preview settings. Verify it supports the selected .NET train.
2. In **Create a new project**, select the Application Builder template for C#,
   then repeat for VB. Verify the classic WinForms template is still available.
3. Generate to a new path with spaces. Check selected framework, language, file
   names, references, explicit VB Main and distinct settings identity.
4. Restore/build using the documented prerequisites. Debug, close the Form, and
   verify debugger exit and no remaining application process.
5. After supported removal/update, check catalog behavior without changing
   already-created application source. CLI-only lifecycle results do not close
   these IDE rows.

## Designer edit, save and reopen

On the current prototype, start with the **prepared workspace**, which makes
the external harness visible to IDE/MSBuild. This proves only that explicit
prototype arrangement, not clean SDK/package support.

1. Double-click `Open-PowerShell.cmd`. Run `dotnet --version` and confirm the
   pinned SDK. Build the copied solution:

   ```powershell
   dotnet restore TemplateExploration.slnx
   dotnet build TemplateExploration.slnx --no-restore -warnaserror
   ```

2. From this prepared shell, launch a **new VS instance** with the solution:

   ```powershell
   & 'C:\Program Files\Microsoft Visual Studio\18\Professional\Common7\IDE\devenv.exe' `
       "$PWD\TemplateExploration.slnx"
   ```

   Use your installed path. Do not replace an already-open development solution.
   Record any unsupported-SDK, restore or out-of-process Designer error verbatim.
3. Select the C# project as startup project. Open `Form1` in the Designer. Confirm
   there is no running host, settings file creation or browser activation just
   from opening the Designer.
4. Add a Label, TextBox, Apply Button and Close Button using the Toolbox. Set
   meaningful `Name`, `AccessibleName`, `Text`, `TabIndex` and layout/anchoring.
   Give Apply/Close distinct access keys (`&Apply`, `&Close`).
5. Set the Form's `AcceptButton` to Apply and `CancelButton` to Close. Use the
   Designer to create each Click handler. Have Apply display the input in a
   Label, and Close call `Close()`. Do not create a service provider in the Form.
6. Save, close the document, reopen the Designer, resize controls and save again.
   Close VS, reopen the solution and Designer. Verify control properties,
   anchoring, event hookups and TabIndex persist without duplicate handlers.
7. Build and debug. Confirm the configured runtime title still comes from typed
   options, Apply runs exactly once per activation, and Close stops the app.
8. Inspect the edited `.Designer`, code-behind, resource and project diffs.
   Expected edits must not rewrite `Program`, lose constructors, discard
   configuration, change the settings identity, or add duplicate startup paths.
9. Repeat with VB as startup project, retaining explicit Main and native VB
   Designer/code-behind. Do not enable the VB Application Framework to bypass
   a failure; that is separate parity work.

## Keyboard, accessibility and appearance

Use the Designer-edited applications for interactive checks in both languages.

1. Without the mouse, use Tab and Shift+Tab through input and commands. Confirm
   logical order, visible focus, no trap, and no focus on decorative controls.
2. Use access keys, Enter and Escape. Confirm the labeled input is reachable,
   Apply reads the current value once, and Close exits normally.
3. With Narrator or the chosen supported screen reader, verify window title,
   control names/roles, input value, command names and focus changes. Verify
   the real invalid-configuration dialog is announced and dismissible by keyboard.
   Retain tool/version and the actual announcements, not only UIA properties.
4. In a **dedicated test environment**, exercise supported light, Dark and
   Classic configurations. Define which OS/app setting was used; do not treat
   high contrast as a synonym for Classic. Check text/background/focus contrast,
   disabled state, message dialogs, title bars and unexpected color overrides.
5. At 100%, 150% and 200% scaling, resize the Form and move it between monitors
   with different DPI. Check clipping, overlapping controls, focus cues, fonts,
   anchoring, hit targets and Designer reopen. Also record keyboard/font scaling.
   A single recorded launch DPI does not establish this transition coverage.

Do not change the shared workstation's accessibility, theme or display settings
for automation. Use approved test machines/VMs and restore their prior settings.

## Session, hardware and deployment environments

Use approved test machines, never change the current shared workstation's
network/privilege/session policy to simulate these environments.

- **Terminal Services:** run C#/VB in a real remote session, resize/reconnect
  and exercise keyboard, close and host stop. Record session type and behavior;
  printing `RemoteSession=false` locally is not a remote-session pass.
- **Windows on ARM:** build/run on native ARM64 Windows with matching packs,
  repeat Designer and interaction checks where supported, and record process
  architecture. Cross-publishing ARM64 on x64 is not native execution.
- **Slow hardware:** use an actual constrained test VM/device with recorded
  CPU/RAM/storage constraints. Check startup responsiveness, Designer load and
  shutdown during pending work. Do not invent a performance acceptance budget;
  quantified budgets belong to dotnet/winforms#14971.
- **Unpackaged:** deploy both FDD and SCD output to a clean matching machine.
  Launch from an unrelated working directory and a read-only application
  directory as a standard user. Verify configuration discovery and that writable
  state belongs in the user profile, not the deployment directory.
- **Packaged:** use an approved MSIX/installer arrangement and signing identity
  on a disposable machine. Verify install, launch, close, upgrade, uninstall and
  declared user-data retention. Record manifest/package versions and permissions.
  No installer is supplied by these templates; without one, record Blocked,
  rather than equating a NuGet template package with packaged app deployment.
- **Restricted/offline:** after intentionally provisioning the SDK/packages,
  build and run without external feed access in a test VM. Separately test an
  empty cache: restore must give a clear failure. Run published apps as a normal
  user without write access to installation files; inspect startup errors/logs.
  Cleared NuGet sources on an online developer machine do not prove network
  isolation, WDAC/AppLocker compatibility, clean-machine or restricted-user support.

## Acceptance boundary

Automated observations, copied-workspace CLI builds, actual VS Designer results,
screen-reader interaction and environment/deployment results are independent
evidence. The preparation script deliberately leaves all hands-on rows NotRun.
Existing prototype shipping/runtime reviews, Hybrid cleanup dependencies,
unprovisioned environments and unexecuted remote CI remain open. No result here
authorizes claiming the full upstream issue complete before those rows are
executed or explicitly dispositioned by the owner.

## Local observations

The documented Arcade driver build passed with zero warnings/errors. Preparation
passed all four real-process probes: C#/VB accessible main-window close and real
startup-error acknowledgment. The observed environment was local x64, 144 DPI,
non-remote and non-high-contrast. No light/Dark/Classic or DPI-transition result
is inferred from those properties.

The copied C#/VB solution restored with cleared package sources, built with
warnings treated as errors, and both copied applications ran and closed through
UI Automation. Original lifecycle source hashes remained unchanged. This is a
cached offline restore on this developer machine, not a clean offline machine.

Evidence and the editable solution are retained at
`%TEMP%\WinForms14970\35977c21040c412e820d28ae434b95ff`.
The 20 manual language/category rows remain **NotRun**. Visual Studio was not
launched or attached to; an existing development instance was left untouched.
No remote CI, actual Designer round trip, screen-reader/real-keyboard test,
appearance transition, native ARM64, restricted-user or installer acceptance is
claimed.
