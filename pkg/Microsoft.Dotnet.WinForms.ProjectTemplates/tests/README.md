# Basic Application Builder template validation

These checks belong to dotnet/winforms#14966. They validate the nonshipping C#/VB prototypes,
not compatibility with an unmodified public SDK. The automated lifecycle entry point below
extends them for dotnet/winforms#14969.
Use Windows x64, PowerShell 7, and an interactive desktop; do not interact with automated test windows.
Run the following commands from the repository root.

The [gated Hybrid scaffold checks](#gated-hybrid-scaffold) below also cover source-only isolation
for dotnet/winforms#14967. They do not enable Hybrid template discovery or browser activation.

For dotnet/winforms#14970, the [exploratory runbook](EXPLORATORY-TESTING.md) prepares
an isolated C#/VB editing workspace and checks the real published apps through UI Automation.
It provides Designer, keyboard/screen-reader, appearance, session and deployment procedures,
with manual results explicitly NotRun until a tester performs them. It does not replace actual
Visual Studio or clean-machine testing with a compilation/parameterless-construction proxy.

## Automated lifecycle matrix

`Test-TemplateLifecycle.ps1` runs creation/package checks, explicit restore, no-restore build,
real default-entry-point execution, separate C#/VB smoke fixtures, and Release publishing.
It uses only a unique test hive and private package cache; it never clears shared caches or
changes the developer's template installation. The existing external prototype imports remain
test-only and are not written into generated projects.

After packing hosting and templates with Arcade, run:

```powershell
$artifacts = Join-Path $env:TEMP 'WinForms14967Artifacts'
.\pkg\Microsoft.Dotnet.WinForms.ProjectTemplates\tests\Test-TemplateLifecycle.ps1 `
    -TemplatePackage "$artifacts\packages\Debug\NonShipping\Microsoft.Dotnet.WinForms.ProjectTemplates.12.0.0-dev.nupkg" `
    -HostingPackageDirectory "$artifacts\packages\Debug\Shipping" `
    -PrototypeWinFormsDirectory "$artifacts\bin\System.Windows.Forms\Debug\net12.0"
```

The script requires Windows, PowerShell 7, an interactive desktop, the matching SDK/Core/Desktop
runtime train, and packed prototype dependencies. Defaults use the same explicit Core version as
`PrototypeConsumer.props`; override `-PrototypeCoreVersion` when updating that train. Use
`-OutputRoot` for a writable evidence directory and `-DotNet` for a different matching SDK.
Every subprocess is bounded; hung processes started by the harness are stopped by their own PID.
Do not interact with its automatically opened windows.

- Creation covers C#/VB and names containing spaces/punctuation, exact file manifests, framework
  rejection, unique settings IDs, unchanged classic payloads, and Hybrid exclusion.
- Package lifecycle checks update availability without applying a remote update, replaces the
  exact same local version with `--force`, and uninstalls in `finally`. It verifies stable discovery,
  unchanged generated source, and removal from discovery. **This is not a two-version upgrade or
  downgrade test**; that requires a separately versioned published artifact.
- Restore covers a deliberate empty-source/empty-cache failure and a subsequent cached restore
  with all feeds cleared. NuGet auditing is disabled only for those explicitly offline checks,
  since advisory lookups would otherwise make them network-dependent.
- All four generated projects build and run with their actual entry points. Native C#/VB
  fixtures cover configuration/settings, invalid startup options, parameterless construction,
  UI-origin stop, and host-origin stop, without modifying template payloads.
- C#/VB publish as framework-dependent and self-contained for `win-x64`, `win-x86`, and
  `win-arm64` by default. Apphost/runtime PE machine types, published manifests/runtime files,
  and unchanged generated source are
  checked. Only the native OS architecture executes; other architecture runs are explicitly
  **NotRun**, never counted as passes. `-RuntimeIdentifiers win-x64` selects a smaller local matrix
  on x64; the selected list must include the native architecture.
- Executed publishes must display the configured real main-window title, accept normal close,
  and exit zero. The test driver does not add shutdown switches to production startup.

Each run retains `results.json`, JUnit `results.xml`, `environment.json`, per-command stdout/stderr,
generated source, and deployment artifacts under a unique output directory. A short, unique
`WFT-<guid>` restore cache under the temporary directory avoids legacy Windows path limits in
ARM64 runtime resource paths; its exact location is recorded in `environment.json`. Failed cases
throw and retain evidence.
Cross-published execution and actual IDE/Designer/clean-machine checks remain separate evidence.

### CI integration and gates

`azure-pipelines-PR.yml` exposes the boolean `templateLifecycleTests` queue parameter. Enabling
it adds a serial lifecycle task after package creation in the x64 Debug job, plus JUnit publication
that fails on failing/missing results. Command logs live below the existing `artifacts\log` upload
root. The task runs the same script and default architecture/deployment matrix as local validation.

The queue parameter is **off by default** while matching prototype runtimes/API packs are not
guaranteed on CI agents. Enabling it requires provisioning the runtime train described below and
an interactive Windows agent; it must not be presented as an already executed remote CI result.
There is no silent shipping-SDK fallback. A fully enabled PR gate, native x86/ARM64 execution,
two-version updates, and runnable Hybrid coverage remain follow-up acceptance gates.

Local validation of the default matrix passed: **34 cases passed, zero failed, eight explicitly
not run** (native x86/ARM64 execution on the x64 machine). Both Arcade package commands passed
with zero warnings/errors. The template package retains the six pre-existing classic-template
metadata messages and readme informational message. Evidence is retained under
`%TEMP%\WinForms14969\402e12d8975a4a7d8151d8fca8010948`; this is local prototype evidence,
not an executed Azure pipeline.

## Build the actual packages

Use the repository's Arcade entry point, not a standalone product-project `dotnet build`:

```powershell
.\build.cmd -projects src\Microsoft.Extensions.WinForms.Hosting\src\Microsoft.Extensions.WinForms.Hosting.csproj -pack -verbosity minimal -binaryLogName BasicTemplatesHosting.binlog
.\build.cmd -projects pkg\Microsoft.Dotnet.WinForms.ProjectTemplates\Microsoft.Dotnet.Winforms.ProjectTemplates.csproj -pack -verbosity minimal -binaryLogName BasicTemplatesPackage.binlog
```

Both commands passed locally. The first builds the current framework dependencies and hosting
package. The template package retains six pre-existing classic-template open-file metadata
messages and the existing package-readme informational message; neither command reported warnings
or errors. Per-culture catalogs generated by the existing localizer currently contain English
fallback text; they are not reviewed translations.

## Isolated creation and content checks

```powershell
.\pkg\Microsoft.Dotnet.WinForms.ProjectTemplates\tests\Test-BasicTemplates.ps1 `
    -TemplatePackage artifacts\packages\Debug\NonShipping\Microsoft.Dotnet.WinForms.ProjectTemplates.12.0.0-dev.nupkg `
    -OutputRoot (Join-Path $env:TEMP 'WinFormsBasicTemplates')
```

The script installs the packed artifact into a unique test-only template-engine hive, creates four
projects, and uninstalls in `finally`. It checks exact generated-file manifests, unique settings
identities, framework restrictions, absence of repository project imports/references, and
byte-for-byte preservation of classic payloads. It does not change the user's installed templates.
All checks passed. Copy the retained generation path printed at the end into `$generated` below.

## Explicit prototype consumer harness

The installed SDK currently predates the in-development WinForms APIs. `PrototypeConsumer.props`
and `.targets` are test-only external imports: they substitute repository-built framework
assemblies and matching dependency packages. They are never emitted into generated applications.
Successful validation with them is not proof of public-SDK or shipping support.

The tested SDK/Desktop train was `12.0.100-alpha.1.26480.102` /
`12.0.0-alpha.1.26480.102`; local product dependencies required Core
`12.0.0-alpha.1.26551.101`. `PrototypeCoreVersion` is overridable when the repository's framework
train changes. Obtain the matching runtime/reference packs through the normal repository setup;
do not treat these historical versions as a general installation recommendation.

Use a fresh private restore cache because the locally packed `12.0.0-dev` package is mutable.
Do not clear or modify shared NuGet caches. Disable central package management on the command line:
setting it after repository imports is too late.

```powershell
$generated = '<retained generation path printed by Test-BasicTemplates.ps1>'
$dotnet = (Resolve-Path '.dotnet\dotnet.exe').Path
$props = (Resolve-Path 'pkg\Microsoft.Dotnet.WinForms.ProjectTemplates\tests\PrototypeConsumer.props').Path
$targets = (Resolve-Path 'pkg\Microsoft.Dotnet.WinForms.ProjectTemplates\tests\PrototypeConsumer.targets').Path
$packages = Join-Path $env:TEMP ('WinFormsBasicPackages-' + [Guid]::NewGuid().ToString('N'))
$common = @(
    "-p:DirectoryBuildPropsPath=$props",
    "-p:DirectoryBuildTargetsPath=$targets",
    '-p:ManagePackageVersionsCentrally=false',
    "-p:RestorePackagesPath=$packages",
    "-p:RestoreConfigFile=$PWD\NuGet.Config",
    "-p:RestoreAdditionalProjectSources=$PWD\artifacts\packages\Debug\Shipping",
    '-warnaserror', '-v', 'minimal'
)
$projects = @(
    'BasicCSharp\BasicCSharp.csproj',
    'BasicVisualBasic\BasicVisualBasic.vbproj',
    'CSharp App.1\CSharp App.1.csproj',
    'VB App.1\VB App.1.vbproj'
)
foreach ($project in $projects) {
    & $dotnet build (Join-Path $generated $project) @common
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
}
```

All four generated-project builds passed with zero warnings/errors. The unusual names exercise
language-native namespace/startup substitution.

## Native runtime scenarios

Build the C# and VB fixtures separately. They link into the corresponding generated project only
when `TemplateSmoke=true`; no test switches or helper projects are added to the template payload.

```powershell
foreach ($project in $projects[0..1]) {
    & $dotnet build (Join-Path $generated $project) @common -p:TemplateSmoke=true
    if ($LASTEXITCODE -ne 0) { throw "Fixture build failed: $project" }
}

function Invoke-TemplateScenario([string] $Executable, [string] $Scenario) {
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.WorkingDirectory = $PWD.Path
    $start.Environment['DOTNET_ROOT'] = (Split-Path $dotnet)
    $start.Environment['DOTNET_ROOT_X64'] = (Split-Path $dotnet)
    [void]$start.Environment.Remove('Application__Title')
    [void]$start.Environment.Remove('DOTNET_CONTENTROOT')
    $start.ArgumentList.Add($Scenario)
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    try {
        if (-not $process.WaitForExit(45000)) { throw "Scenario timed out: $Scenario" }
        $text = $stdout.GetAwaiter().GetResult()
        $errors = $stderr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0 -or $errors.Length -gt 0 -or $text -notmatch 'PASS') {
            throw "Scenario failed: $Scenario`n$text`n$errors"
        }
        $text.TrimEnd()
    }
    finally {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
        $process.Dispose()
    }
}

foreach ($name in @('BasicCSharp', 'BasicVisualBasic')) {
    $executable = Join-Path $generated "$name\bin\Debug\net12.0-windows\$name.exe"
    foreach ($scenario in @('services', 'invalid-options', 'designer', 'ui', 'host-stop')) {
        Invoke-TemplateScenario $executable $scenario
    }
}
```

Both fixture builds and all ten runtime cases passed.

| Scenario | Expected result |
|---|---|
| `services` | Deployment JSON loads from the executable directory; environment overrides JSON and CLI overrides environment; watchers remain off; settings resolution creates no storage; defaults/save/load work. |
| `invalid-options` | An empty title raises options validation during host startup, before Form creation. |
| `designer` | Public parameterless construction retains the default title and creates no handle. This does not replace actual VS Designer testing. |
| `ui` | Actual generated `Program.Main` displays the configured typed Form; closing it disposes the UI and returns zero. |
| `host-stop` | Host lifetime stop closes/disposes the typed Form and leaves no open Forms. |

The settings scenario requires an unused generated application ID and removes only its newly owned
settings directory after the host is disposed. Never run it against an application identity with
existing user data. The fixture uses synchronous waits on its dedicated test entry point, not
production UI event handlers. Each process has a watchdog; only its own PID is terminated on failure.

## Publish and run the actual default entry point

Explicitly exclude fixtures and publish both languages in each deployment mode:

```powershell
foreach ($mode in @('fdd', 'scd')) {
    foreach ($project in $projects[0..1]) {
        $name = [IO.Path]::GetFileNameWithoutExtension($project)
        $output = Join-Path $generated "publish\$name\$mode"
        $contained = if ($mode -eq 'scd') { 'true' } else { 'false' }
        & $dotnet publish (Join-Path $generated $project) @common `
            -p:TemplateSmoke=false -c Release -r win-x64 --self-contained $contained -o $output
        if ($LASTEXITCODE -ne 0) { throw "Publish failed: $name $mode" }
    }
}
```

All four publishes passed with warnings treated as errors. To check the actual published WinExe
without changing its startup, use a different working directory and close its real main window:

```powershell
foreach ($name in @('BasicCSharp', 'BasicVisualBasic')) {
    foreach ($mode in @('fdd', 'scd')) {
        $title = "$name-$mode-published"
        $start = [Diagnostics.ProcessStartInfo]::new(
            (Join-Path $generated "publish\$name\$mode\$name.exe"))
        $start.UseShellExecute = $false
        $start.WorkingDirectory = $PWD.Path
        $start.Environment['DOTNET_ROOT'] = (Split-Path $dotnet)
        $start.Environment['DOTNET_ROOT_X64'] = (Split-Path $dotnet)
        $start.ArgumentList.Add("--Application:Title=$title")
        $process = [Diagnostics.Process]::Start($start)
        try {
            if (-not $process.WaitForInputIdle(30000)) { throw 'Input-idle timeout.' }
            # An early hidden-window idle does not prove that the main Form is visible.
            $ready = [Threading.SpinWait]::SpinUntil([Func[bool]] {
                $process.Refresh()
                return $process.HasExited -or
                    ($process.MainWindowHandle -ne 0 -and $process.MainWindowTitle -eq $title)
            }, 30000)
            if (-not $ready -or $process.HasExited) { throw 'Configured Form did not appear.' }
            if (-not $process.CloseMainWindow()) { throw 'Main window refused close.' }
            if (-not $process.WaitForExit(30000)) { throw 'Shutdown timeout.' }
            if ($process.ExitCode -ne 0) { throw "Exit code: $($process.ExitCode)" }
            "PASS $name $mode"
        }
        finally {
            if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
            $process.Dispose()
        }
    }
}
```

All four published runs passed: expected title, normal close, exit zero. This demonstrates local
prototype deployment, not clean-machine support or availability of approved redistributable packs.
Keep useful logs; remove only explicitly identified, test-owned temporary artifacts when finished.

## Remaining acceptance gates

Actual Visual Studio discovery, supported IDE/SDK selection, out-of-process Designer edit/save/reopen,
attached-debugger logging inspection, additional localization/architectures, clean-machine/offline
deployment, remote CI, and shipping API/package/framework approval remain unverified. Enabled full CI,
exploratory testing, and performance belong to later children. Hybrid and adoption samples are not
part of these templates.

## Gated Hybrid scaffold

The source-only C# scaffold is outside the package's `content` glob and has pending, non-discoverable
metadata. Its application and runtime-Form factories reject activation before creating a host or
WebView. The checks below deliberately do not start a browser or exercise asynchronous WebView
cleanup. The native Form check is not a Visual Studio Designer acceptance test.

The actual package build and creation checks passed, including absence of scaffold files from the
archive, unchanged discovery after scanning the source scaffold, four C#/VB basic creations,
unsupported-framework rejection, and byte-for-byte classic-payload preservation:

```powershell
$artifacts = Join-Path $env:TEMP 'WinForms14967Artifacts'
.\build.cmd -projects pkg\Microsoft.Dotnet.WinForms.ProjectTemplates\Microsoft.Dotnet.Winforms.ProjectTemplates.csproj `
    -pack -verbosity minimal -binaryLogName "$artifacts\HybridScaffoldPackage.binlog" "/p:ArtifactsDir=$artifacts"
.\pkg\Microsoft.Dotnet.WinForms.ProjectTemplates\tests\Test-BasicTemplates.ps1 `
    -TemplatePackage "$artifacts\packages\Debug\NonShipping\Microsoft.Dotnet.WinForms.ProjectTemplates.12.0.0-dev.nupkg" `
    -OutputRoot (Join-Path $env:TEMP 'HybridTemplateGeneration')
```

Arcade reported zero warnings/errors; the existing six classic-template metadata messages and
package-readme informational message remain. No Hybrid files or WebView dependency enter the package.

The scaffold and its separate smoke entry point also built with warnings treated as errors.
These builds require the same explicit prototype consumer imports as the basic templates, plus
the current product assemblies. They do not prove compatibility with an unmodified SDK.
Use a short private package-cache path: the WebView package's long assembly name exceeded legacy
Windows path limits in the first, longer test-owned cache. Do not clear shared caches.

```powershell
$dotnet = (Resolve-Path '.dotnet\dotnet.exe').Path
$scaffold = (Resolve-Path 'pkg\Microsoft.Dotnet.WinForms.ProjectTemplates\scaffolds\WinFormsApplicationBuilder-Hybrid-CSharp').Path
$props = (Resolve-Path 'pkg\Microsoft.Dotnet.WinForms.ProjectTemplates\tests\PrototypeConsumer.props').Path
$targets = (Resolve-Path 'pkg\Microsoft.Dotnet.WinForms.ProjectTemplates\tests\PrototypeConsumer.targets').Path
$work = Join-Path $env:TEMP ('WFH-' + [Guid]::NewGuid().ToString('N'))
$packages = Join-Path $env:TEMP ('WHp-' + [Guid]::NewGuid().ToString('N'))
New-Item $work -ItemType Directory | Out-Null
Copy-Item $scaffold (Join-Path $work 'source') -Recurse
$project = Join-Path $work 'source\Company.WinFormsHybridApp1.csproj'
$common = @(
    "-p:DirectoryBuildPropsPath=$props",
    "-p:DirectoryBuildTargetsPath=$targets",
    "-p:PrototypeWinFormsDirectory=$artifacts\bin\System.Windows.Forms\Debug\net12.0",
    '-p:ManagePackageVersionsCentrally=false',
    "-p:RestorePackagesPath=$packages",
    "-p:RestoreConfigFile=$PWD\NuGet.Config",
    "-p:RestoreAdditionalProjectSources=$PWD\artifacts\packages\Debug\Shipping",
    '-warnaserror', '-v', 'minimal'
)
& $dotnet build $project @common
if ($LASTEXITCODE -ne 0) { throw 'Scaffold build failed.' }
& $dotnet build $project @common -p:HybridScaffoldSmoke=true "-p:OutputPath=$work\smoke"
if ($LASTEXITCODE -ne 0) { throw 'Scaffold smoke build failed.' }
$env:DOTNET_ROOT = Split-Path $dotnet
$env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
& (Join-Path $work 'smoke\Company.WinFormsHybridApp1.exe')
if ($LASTEXITCODE -ne 0) { throw 'Scaffold smoke failed.' }
```

Both builds passed with zero warnings/errors. Hosting/logging use the ASP.NET Core framework
reference, not duplicate package references. The draft removes the unused WebView2 WPF reference
instead of enabling WPF or suppressing WindowsBase conflicts. No prototype imports are in the
scaffold project itself.

The smoke executable passed under a process-specific watchdog: both activation factories reject
execution, and parameterless Form construction creates native controls without a WebView.
Its entry point is linked only by the external `HybridScaffoldSmoke` test switch, never by the
scaffold project. The ordinary WinExe remains disabled and displays an explanatory error if run.

Logs are retained as `HybridScaffoldPackage-build.log` and `HybridScaffoldPackage.binlog` under `$artifacts` and
`Package-tests.log`, `Scaffold-build.log`, `Scaffold-smoke-build.log`, and `Scaffold-smoke.log`
under the test-owned working directory. Real WebView initialization/disposal, deployment,
Visual Studio, clean-machine acceptance, and remote CI remain pending; dotnet/winforms#14967
is not complete.
