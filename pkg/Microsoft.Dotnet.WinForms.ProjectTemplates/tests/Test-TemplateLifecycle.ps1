#Requires -Version 7.0
param(
    [Parameter(Mandatory = $true)]
    [string] $TemplatePackage,
    [Parameter(Mandatory = $true)]
    [string] $HostingPackageDirectory,
    [Parameter(Mandatory = $true)]
    [string] $PrototypeWinFormsDirectory,
    [string] $DotNet,
    [string] $OutputRoot = (Join-Path ([IO.Path]::GetTempPath()) 'WinFormsTemplateLifecycle'),
    [string] $PrototypeCoreVersion = '12.0.0-alpha.1.26551.101',
    [ValidateSet('win-x64', 'win-x86', 'win-arm64')]
    [string[]] $RuntimeIdentifiers = @('win-x64', 'win-x86', 'win-arm64'),
    [ValidateRange(1, 1800)]
    [int] $ProcessTimeoutSeconds = 600
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) {
    throw 'Template lifecycle validation requires Windows and an interactive desktop.'
}
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
if (-not $DotNet) {
    $DotNet = Join-Path $repository '.dotnet\dotnet.exe'
}
$DotNet = (Resolve-Path -LiteralPath $DotNet).Path
$TemplatePackage = (Resolve-Path -LiteralPath $TemplatePackage).Path
$HostingPackageDirectory = (Resolve-Path -LiteralPath $HostingPackageDirectory).Path
$PrototypeWinFormsDirectory = (Resolve-Path -LiteralPath $PrototypeWinFormsDirectory).Path
if (-not (Test-Path -LiteralPath (Join-Path $PrototypeWinFormsDirectory 'System.Windows.Forms.dll'))) {
    throw 'Build the repository WinForms prototype before running lifecycle validation.'
}
if (@(Get-ChildItem $HostingPackageDirectory -Filter 'Microsoft.Extensions.WinForms.Hosting.*.nupkg').Count -eq 0) {
    throw 'Pack Microsoft.Extensions.WinForms.Hosting before running lifecycle validation.'
}
$output = [IO.Directory]::CreateDirectory((Join-Path $OutputRoot ([Guid]::NewGuid().ToString('N')))).FullName
$logs = [IO.Directory]::CreateDirectory((Join-Path $output 'logs')).FullName
$working = [IO.Directory]::CreateDirectory((Join-Path $output 'unrelated-working-directory')).FullName
$packageCache = [IO.Directory]::CreateDirectory(
    (Join-Path ([IO.Path]::GetTempPath()) ('WFT-' + [Guid]::NewGuid().ToString('N')))).FullName
$results = [Collections.Generic.List[object]]::new()
$nativeRid = 'win-' + [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
if ($RuntimeIdentifiers -notcontains $nativeRid) {
    throw "Include the native runtime identifier $nativeRid so published applications are actually executed."
}

function Assert-Condition {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) {
        throw $Message
    }
}

function Invoke-CheckedProcess {
    param(
        [string] $FileName,
        [string[]] $Arguments,
        [string] $Name,
        [switch] $ExpectFailure,
        [string] $WindowTitle
    )
    $start = [Diagnostics.ProcessStartInfo]::new($FileName)
    $start.UseShellExecute = $false
    $start.WorkingDirectory = $working
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Environment['DOTNET_ROOT'] = Split-Path $DotNet
    $start.Environment['DOTNET_ROOT_X64'] = Split-Path $DotNet
    $start.Environment['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'
    $start.Environment['DOTNET_SKIP_FIRST_TIME_EXPERIENCE'] = '1'
    $start.Environment['NO_COLOR'] = '1'
    foreach ($key in @($start.Environment.Keys)) {
        if ($key -match '^(Application|Logging|hostBuilder)(__|:)|^DOTNET_(CONTENTROOT|ENVIRONMENT)$') {
            [void]$start.Environment.Remove($key)
        }
    }
    foreach ($argument in $Arguments) {
        $start.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $timer = [Diagnostics.Stopwatch]::StartNew()
    try {
        if ($WindowTitle) {
            $ready = $false
            while ($timer.Elapsed.TotalSeconds -lt 45) {
                $process.Refresh()
                if ($process.HasExited) { break }
                if ($process.MainWindowHandle -ne 0 -and $process.MainWindowTitle -ceq $WindowTitle) {
                    $ready = $true
                    break
                }
                Start-Sleep -Milliseconds 50
            }
            Assert-Condition $ready "The real application window did not appear: $Name"
            Assert-Condition ($process.CloseMainWindow()) "The real application refused close: $Name"
        }
        $remaining = [Math]::Max(1, $ProcessTimeoutSeconds * 1000 - [int]$timer.Elapsed.TotalMilliseconds)
        Assert-Condition ($process.WaitForExit($remaining)) "Process timed out: $Name"
        $text = $stdout.GetAwaiter().GetResult()
        $errors = $stderr.GetAwaiter().GetResult()
        $text | Set-Content (Join-Path $logs "$Name.stdout.log")
        $errors | Set-Content (Join-Path $logs "$Name.stderr.log")
        if ($ExpectFailure) {
            Assert-Condition ($process.ExitCode -ne 0) "Expected a failing command: $Name"
        }
        else {
            Assert-Condition ($process.ExitCode -eq 0) "Process failed: $Name (exit $($process.ExitCode))`n$text`n$errors"
        }

        return [pscustomobject]@{ Text = $text; Errors = $errors; ExitCode = $process.ExitCode }
    }
    finally {
        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id -Force
            $process.WaitForExit()
        }
        if (-not (Test-Path (Join-Path $logs "$Name.stdout.log"))) {
            $stdout.GetAwaiter().GetResult() | Set-Content (Join-Path $logs "$Name.stdout.log")
            $stderr.GetAwaiter().GetResult() | Set-Content (Join-Path $logs "$Name.stderr.log")
        }
        $process.Dispose()
    }
}

function Invoke-Case {
    param([string] $CaseName, [scriptblock] $CaseAction)
    $caseTimer = [Diagnostics.Stopwatch]::StartNew()
    try {
        & $CaseAction
        $results.Add([pscustomobject]@{ Name = $CaseName; Outcome = 'Passed'; Seconds = $caseTimer.Elapsed.TotalSeconds; Error = '' })
        Write-Output "PASS $CaseName"
    }
    catch {
        $results.Add([pscustomobject]@{ Name = $CaseName; Outcome = 'Failed'; Seconds = $caseTimer.Elapsed.TotalSeconds; Error = $_.ToString() })
        throw
    }
}

function Get-SourceSnapshot {
    param([string] $Directory)
    $snapshot = @{}
    foreach ($file in Get-ChildItem $Directory -Recurse -File) {
        $relative = [IO.Path]::GetRelativePath($Directory, $file.FullName)
        if ($relative -notmatch '(^|\\)(bin|obj)(\\|$)') {
            $snapshot[$relative] = (Get-FileHash $file.FullName -Algorithm SHA256).Hash
        }
    }

    return $snapshot
}

function Assert-SourceUnchanged {
    param([string] $Directory, [hashtable] $Before)
    $after = Get-SourceSnapshot $Directory
    Assert-Condition ($after.Count -eq $Before.Count) "Generated source file count changed: $Directory"
    foreach ($key in $Before.Keys) {
        Assert-Condition ($after.ContainsKey($key) -and $after[$key] -eq $Before[$key]) "Generated source changed: $key"
    }
}

function Assert-PublishedArchitecture {
    param([string] $File, [string] $RuntimeIdentifier)
    $reader = [IO.BinaryReader]::new([IO.File]::OpenRead($File))
    try {
        [void]$reader.BaseStream.Seek(0x3c, [IO.SeekOrigin]::Begin)
        $header = $reader.ReadInt32()
        [void]$reader.BaseStream.Seek($header, [IO.SeekOrigin]::Begin)
        Assert-Condition ($reader.ReadUInt32() -eq 0x4550) "Invalid PE header: $File"
        $expected = switch ($RuntimeIdentifier) {
            'win-x64' { 0x8664 }
            'win-x86' { 0x014c }
            'win-arm64' { 0xaa64 }
        }
        Assert-Condition ($reader.ReadUInt16() -eq $expected) "Incorrect executable architecture: $RuntimeIdentifier / $File"
    }
    finally {
        $reader.Dispose()
    }
}

$common = @(
    "-p:DirectoryBuildPropsPath=$PSScriptRoot\PrototypeConsumer.props",
    "-p:DirectoryBuildTargetsPath=$PSScriptRoot\PrototypeConsumer.targets",
    '-p:ManagePackageVersionsCentrally=false',
    "-p:RestorePackagesPath=$packageCache",
    "-p:RestoreConfigFile=$repository\NuGet.Config",
    "-p:RestoreAdditionalProjectSources=$HostingPackageDirectory",
    "-p:PrototypeWinFormsDirectory=$PrototypeWinFormsDirectory",
    "-p:PrototypeCoreVersion=$PrototypeCoreVersion",
    '-warnaserror', '-v', 'minimal'
)
$success = $false
try {
    $generationResult = Join-Path $output 'generation.json'
    Invoke-Case 'creation-package-lifecycle' {
        $null = Invoke-CheckedProcess (Join-Path $PSHOME 'pwsh.exe') @(
            '-NoProfile', '-File', (Join-Path $PSScriptRoot 'Test-BasicTemplates.ps1'),
            '-TemplatePackage', $TemplatePackage, '-DotNet', $DotNet,
            '-OutputRoot', (Join-Path $output 'generated'), '-ResultPath', $generationResult) 'creation'
    }
    $generated = Get-Content $generationResult -Raw | ConvertFrom-Json
    $projects = @($generated.Projects)
    $offlineConfig = Join-Path $output 'offline.nuget.config'
    [IO.File]::WriteAllText($offlineConfig, '<configuration><packageSources><clear /></packageSources></configuration>')
    $offlineCommon = @($common | Where-Object { $_ -notmatch '^-p:Restore(ConfigFile|AdditionalProjectSources|PackagesPath)=' })
    $offlineCommon += @("-p:RestoreConfigFile=$offlineConfig", '-p:NuGetAudit=false')
    Invoke-Case 'missing-packages-explicit-failure' {
        $result = Invoke-CheckedProcess $DotNet (@('restore', $projects[0],
            "-p:RestorePackagesPath=$output\empty-packages") + $offlineCommon) 'missing-packages' -ExpectFailure
        Assert-Condition (($result.Text + $result.Errors) -match 'NU110[01]') `
            'The empty-cache check must fail with an explicit package-resolution error.'
    }
    foreach ($project in $projects) {
        $name = [IO.Path]::GetFileNameWithoutExtension($project)
        $directory = Split-Path $project
        $before = Get-SourceSnapshot $directory
        Invoke-Case "$name-restore-build" {
            $null = Invoke-CheckedProcess $DotNet (@('restore', $project) + $common) "$name-restore"
            $null = Invoke-CheckedProcess $DotNet (@('restore', $project,
                "-p:RestorePackagesPath=$packageCache") + $offlineCommon) "$name-cached-offline-restore"
            $null = Invoke-CheckedProcess $DotNet (@('build', $project, '--no-restore') + $common) "$name-build"
            $application = Join-Path $directory "bin\Debug\net12.0-windows\$name.exe"
            $title = "$name-default"
            $null = Invoke-CheckedProcess $application @("--Application:Title=$title") "$name-default-run" -WindowTitle $title
            Assert-SourceUnchanged $directory $before
        }
    }
    foreach ($project in $projects[0..1]) {
        $name = [IO.Path]::GetFileNameWithoutExtension($project)
        $directory = Split-Path $project
        $before = Get-SourceSnapshot $directory
        $fixture = Join-Path $output "fixtures\$name"
        Invoke-Case "$name-fixture-build" {
            $null = Invoke-CheckedProcess $DotNet (@('build', $project, '--no-restore', '-p:TemplateSmoke=true',
                "-p:OutputPath=$fixture") + $common) "$name-fixture-build"
        }
        foreach ($scenario in @('services', 'invalid-options', 'designer', 'ui', 'host-stop')) {
            Invoke-Case "$name-$scenario" {
                $result = Invoke-CheckedProcess (Join-Path $fixture "$name.exe") @($scenario) "$name-$scenario"
                Assert-Condition ($result.Errors.Length -eq 0 -and $result.Text -match "PASS (C#|VB): $scenario") `
                    "Fixture did not report the expected scenario: $name-$scenario"
            }
        }
        foreach ($rid in $RuntimeIdentifiers) {
            foreach ($mode in @('fdd', 'scd')) {
                $contained = if ($mode -eq 'scd') { 'true' } else { 'false' }
                $publish = Join-Path $output "publish\$name\$rid\$mode"
                $id = "$name-$rid-$mode"
                $publishArguments = @('-p:Configuration=Release', "-p:RuntimeIdentifier=$rid",
                    "-p:SelfContained=$contained", '-p:TemplateSmoke=false')
                Invoke-Case "$id-publish" {
                    $null = Invoke-CheckedProcess $DotNet (@('restore', $project) + $publishArguments + $common) "$id-restore"
                    $null = Invoke-CheckedProcess $DotNet (@('publish', $project, '--no-restore', '-o', $publish) +
                        $publishArguments + $common) "$id-publish"
                    foreach ($expected in @("$name.exe", "$name.dll", "$name.runtimeconfig.json",
                        'appsettings.json', 'Microsoft.Extensions.WinForms.Hosting.dll')) {
                        Assert-Condition (Test-Path -LiteralPath (Join-Path $publish $expected)) "Missing published file: $id/$expected"
                    }
                    $runtimeConfig = Get-Content (Join-Path $publish "$name.runtimeconfig.json") -Raw | ConvertFrom-Json
                    $frameworkProperty = if ($mode -eq 'scd') { 'includedFrameworks' } else { 'frameworks' }
                    Assert-Condition ($null -ne $runtimeConfig.runtimeOptions.PSObject.Properties[$frameworkProperty]) `
                        "Publish mode mismatch: $id"
                    Assert-Condition ((Test-Path -LiteralPath (Join-Path $publish 'coreclr.dll')) -eq ($mode -eq 'scd')) `
                        "Unexpected runtime files for deployment mode: $id"
                    Assert-PublishedArchitecture (Join-Path $publish "$name.exe") $rid
                    if ($mode -eq 'scd') {
                        Assert-PublishedArchitecture (Join-Path $publish 'coreclr.dll') $rid
                    }
                    Assert-SourceUnchanged $directory $before
                }
                if ($rid -eq $nativeRid) {
                    Invoke-Case "$id-published-run" {
                        $title = "$id-published"
                        $result = Invoke-CheckedProcess (Join-Path $publish "$name.exe") @("--Application:Title=$title") `
                            "$id-published-run" -WindowTitle $title
                        Assert-Condition ($result.Errors.Length -eq 0) "Published application wrote errors: $id"
                    }
                }
                else {
                    $results.Add([pscustomobject]@{ Name = "$id-published-run"; Outcome = 'NotRun'; Seconds = 0;
                        Error = "Cross-published artifact; execution requires a native $rid runner." })
                    Write-Output "NOT RUN $id-published-run: requires native $rid runner"
                }
            }
        }
    }
    $success = $true
}
finally {
    @{ Prototype = $true; DotNet = $DotNet; CoreVersion = $PrototypeCoreVersion;
        PackageCache = $packageCache; NativeRid = $nativeRid; RuntimeIdentifiers = $RuntimeIdentifiers } |
        ConvertTo-Json -Depth 3 | Set-Content (Join-Path $output 'environment.json')
    ConvertTo-Json -InputObject $results.ToArray() -Depth 4 | Set-Content (Join-Path $output 'results.json')
    $settings = [Xml.XmlWriterSettings]::new()
    $settings.Indent = $true
    $writer = [Xml.XmlWriter]::Create((Join-Path $output 'results.xml'), $settings)
    try {
        $writer.WriteStartElement('testsuite')
        $writer.WriteAttributeString('name', 'WinFormsTemplateLifecycle')
        $writer.WriteAttributeString('tests', [string]$results.Count)
        $writer.WriteAttributeString('failures', [string]@($results | Where-Object Outcome -EQ 'Failed').Count)
        $writer.WriteAttributeString('skipped', [string]@($results | Where-Object Outcome -EQ 'NotRun').Count)
        foreach ($result in $results) {
            $writer.WriteStartElement('testcase')
            $writer.WriteAttributeString('name', $result.Name)
            $writer.WriteAttributeString('time', $result.Seconds.ToString('F3', [Globalization.CultureInfo]::InvariantCulture))
            if ($result.Outcome -eq 'Failed') {
                $writer.WriteElementString('failure', [regex]::Replace($result.Error, '[\x00-\x08\x0B\x0C\x0E-\x1F]', ''))
            }
            elseif ($result.Outcome -eq 'NotRun') {
                $writer.WriteElementString('skipped', $result.Error)
            }
            $writer.WriteEndElement()
        }
        $writer.WriteEndElement()
    }
    finally {
        $writer.Dispose()
    }
    Write-Output "Lifecycle evidence retained at $output"
    Write-Output "Private restore cache retained at $packageCache"
}
Assert-Condition $success 'Lifecycle validation did not complete.'
