#Requires -Version 7.0
param(
    [Parameter(Mandatory = $true)]
    [string] $LifecycleOutput,
    [Parameter(Mandatory = $true)]
    [string] $PrototypeWinFormsDirectory,
    [Parameter(Mandatory = $true)]
    [string] $Driver,
    [string] $OutputRoot = (Join-Path ([IO.Path]::GetTempPath()) 'WinForms14970')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Exploration requires an interactive Windows desktop.' }
$LifecycleOutput = (Resolve-Path -LiteralPath $LifecycleOutput).Path
$PrototypeWinFormsDirectory = (Resolve-Path -LiteralPath $PrototypeWinFormsDirectory).Path
$Driver = (Resolve-Path -LiteralPath $Driver).Path
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$environment = Get-Content (Join-Path $LifecycleOutput 'environment.json') -Raw | ConvertFrom-Json
$generation = Get-Content (Join-Path $LifecycleOutput 'generation.json') -Raw | ConvertFrom-Json
$lifecycleResults = @(Get-Content (Join-Path $LifecycleOutput 'results.json') -Raw | ConvertFrom-Json)
if (@($lifecycleResults | Where-Object Outcome -EQ 'Failed').Count -ne 0) {
    throw 'Use evidence from a successful lifecycle run, not partially generated applications.'
}
foreach ($required in @($environment.DotNet, $environment.PackageCache,
    (Join-Path $PrototypeWinFormsDirectory 'System.Windows.Forms.dll'))) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Missing prototype prerequisite: $required" }
}
$workspace = [IO.Directory]::CreateDirectory((Join-Path $OutputRoot ([Guid]::NewGuid().ToString('N')))).FullName
$logs = [IO.Directory]::CreateDirectory((Join-Path $workspace 'evidence')).FullName
$results = [Collections.Generic.List[object]]::new()
$sourceHashes = @{}
$projects = [Collections.Generic.List[string]]::new()
$escape = { param([string] $Value) [Security.SecurityElement]::Escape($Value) }
$props = & $escape (Join-Path $PSScriptRoot 'PrototypeConsumer.props')
$targets = & $escape (Join-Path $PSScriptRoot 'PrototypeConsumer.targets')
$cache = & $escape $environment.PackageCache
$product = & $escape $PrototypeWinFormsDirectory
$core = & $escape $environment.CoreVersion
$config = & $escape (Join-Path $workspace 'NuGet.Config')

@"
<Project>
  <Import Project="$props" />
  <PropertyGroup>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
    <RestorePackagesPath>$cache</RestorePackagesPath>
    <RestoreConfigFile>$config</RestoreConfigFile>
    <NuGetAudit>false</NuGetAudit>
    <PrototypeWinFormsDirectory>$product</PrototypeWinFormsDirectory>
    <PrototypeCoreVersion>$core</PrototypeCoreVersion>
  </PropertyGroup>
</Project>
"@ | Set-Content (Join-Path $workspace 'Directory.Build.props')
@"
<Project>
  <Import Project="$targets" />
</Project>
"@ | Set-Content (Join-Path $workspace 'Directory.Build.targets')
'<configuration><packageSources><clear /></packageSources></configuration>' |
    Set-Content (Join-Path $workspace 'NuGet.Config')
$sdk = (Get-Content (Join-Path $repository 'global.json') -Raw | ConvertFrom-Json).sdk.version
@{ sdk = @{ version = $sdk; allowPrerelease = $true; rollForward = 'disable';
    paths = @((Split-Path $environment.DotNet)) } } |
    ConvertTo-Json -Depth 4 | Set-Content (Join-Path $workspace 'global.json')

foreach ($language in @('CSharp', 'VisualBasic')) {
    $name = if ($language -eq 'CSharp') { 'BasicCSharp' } else { 'BasicVisualBasic' }
    $sourceProjects = @($generation.Projects | Where-Object { [IO.Path]::GetFileNameWithoutExtension($_) -eq $name })
    if ($sourceProjects.Count -ne 1) { throw "Expected one generated $name project." }
    $source = Split-Path $sourceProjects[0]
    $destination = [IO.Directory]::CreateDirectory((Join-Path $workspace $language)).FullName
    foreach ($file in Get-ChildItem $source -Recurse -File) {
        $relative = [IO.Path]::GetRelativePath($source, $file.FullName)
        if ($relative -match '(^|\\)(bin|obj)(\\|$)') { continue }
        $target = Join-Path $destination $relative
        [void][IO.Directory]::CreateDirectory((Split-Path $target))
        Copy-Item -LiteralPath $file.FullName -Destination $target
        $hash = (Get-FileHash $file.FullName -Algorithm SHA256).Hash
        if ((Get-FileHash $target -Algorithm SHA256).Hash -ne $hash) { throw "Source copy changed: $relative" }
        $sourceHashes[$file.FullName] = $hash
    }
    $projects.Add("$language\$([IO.Path]::GetFileName($sourceProjects[0]))")
}
@"
<Solution>
  <Project Path="$($projects[0])" />
  <Project Path="$($projects[1])" />
</Solution>
"@ | Set-Content (Join-Path $workspace 'TemplateExploration.slnx')
@{ LifecycleOutput = $LifecycleOutput; DotNet = $environment.DotNet; Prototype = $true;
    OfflineSources = $true; SharedPrivateCache = $environment.PackageCache; SourceHashes = $sourceHashes } |
    ConvertTo-Json -Depth 4 | Set-Content (Join-Path $logs 'provenance.json')
@'
@echo off
setlocal
'@ | Set-Content (Join-Path $workspace 'Open-PowerShell.cmd') -Encoding ascii
@"
set "DOTNET_ROOT=$(Split-Path $environment.DotNet)"
set "DOTNET_ROOT_X64=$(Split-Path $environment.DotNet)"
set "PATH=$(Split-Path $environment.DotNet);%PATH%"
cd /d "%~dp0"
pwsh -NoExit
"@ | Add-Content (Join-Path $workspace 'Open-PowerShell.cmd') -Encoding ascii

try {
    foreach ($language in @('CSharp', 'VisualBasic')) {
        $name = if ($language -eq 'CSharp') { 'BasicCSharp' } else { 'BasicVisualBasic' }
        $rid = 'win-' + [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
        $application = Join-Path $LifecycleOutput "publish\$name\$rid\fdd\$name.exe"
        if (-not (Test-Path -LiteralPath $application)) { throw "Missing native published application: $application" }
        foreach ($scenario in @('window', 'startup-error')) {
            $id = "$language-$scenario"
            $start = [Diagnostics.ProcessStartInfo]::new($Driver)
            $start.UseShellExecute = $false
            $start.RedirectStandardOutput = $true
            $start.RedirectStandardError = $true
            $start.WorkingDirectory = $workspace
            $start.Environment['DOTNET_ROOT'] = Split-Path $environment.DotNet
            $start.Environment['DOTNET_ROOT_X64'] = Split-Path $environment.DotNet
            $start.ArgumentList.Add($application)
            $start.ArgumentList.Add($scenario)
            $process = [Diagnostics.Process]::Start($start)
            $stdout = $process.StandardOutput.ReadToEndAsync()
            $stderr = $process.StandardError.ReadToEndAsync()
            try {
                if (-not $process.WaitForExit(60000)) { throw "Accessible UI check timed out: $id" }
                $text = $stdout.GetAwaiter().GetResult()
                $errors = $stderr.GetAwaiter().GetResult()
                $text | Set-Content (Join-Path $logs "$id.stdout.log")
                $errors | Set-Content (Join-Path $logs "$id.stderr.log")
                if ($process.ExitCode -ne 0 -or $errors.Length -ne 0) {
                    throw "Accessible UI check failed: $id`n$text`n$errors"
                }
                $result = $text | ConvertFrom-Json
                if ($result.Outcome -ne 'Passed' -or $result.Scenario -ne $scenario) {
                    throw "Unexpected accessible UI report: $id"
                }
                $results.Add([pscustomobject]@{ Case = $id; Outcome = 'Passed'; Evidence = $result })
                Write-Output "PASS $id"
            }
            catch {
                $results.Add([pscustomobject]@{ Case = $id; Outcome = 'Failed'; Evidence = $_.ToString() })
                throw
            }
            finally {
                if (-not $process.HasExited) {
                    foreach ($child in Get-CimInstance Win32_Process -Filter "ParentProcessId = $($process.Id)") {
                        Stop-Process -Id $child.ProcessId -Force
                    }
                    Stop-Process -Id $process.Id -Force
                    $process.WaitForExit()
                }
                if (-not (Test-Path (Join-Path $logs "$id.stdout.log"))) {
                    $stdout.GetAwaiter().GetResult() | Set-Content (Join-Path $logs "$id.stdout.log")
                    $stderr.GetAwaiter().GetResult() | Set-Content (Join-Path $logs "$id.stderr.log")
                }
                $process.Dispose()
            }
        }
    }
    foreach ($file in $sourceHashes.Keys) {
        if ((Get-FileHash $file -Algorithm SHA256).Hash -ne $sourceHashes[$file]) {
            throw "Exploration modified original lifecycle source: $file"
        }
    }
}
finally {
    ConvertTo-Json -InputObject $results.ToArray() -Depth 5 | Set-Content (Join-Path $logs 'automated-results.json')
    $manual = foreach ($language in @('CSharp', 'VisualBasic')) {
        foreach ($case in @('vs-discovery-create', 'designer-roundtrip', 'keyboard-screen-reader',
            'light-dark-classic', 'dpi-multimonitor', 'terminal-services', 'native-arm64',
            'slow-hardware', 'packaged-deployment', 'restricted-offline-machine')) {
            [pscustomobject]@{ Case = $case; Language = $language; Outcome = 'NotRun'; Evidence = ''; Notes = '' }
        }
    }
    $manual | Export-Csv (Join-Path $logs 'manual-results.csv') -NoTypeInformation
    Write-Output "Isolated editing workspace and evidence: $workspace"
    Write-Output 'Manual results are NotRun until a tester performs the documented checks.'
}
