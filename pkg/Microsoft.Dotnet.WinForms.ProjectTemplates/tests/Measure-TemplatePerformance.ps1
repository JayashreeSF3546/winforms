#Requires -Version 7.0
param(
    [Parameter(Mandatory = $true)]
    [string] $LifecycleOutput,
    [Parameter(Mandatory = $true)]
    [string] $PrototypeWinFormsDirectory,
    [Parameter(Mandatory = $true)]
    [string] $HostingPackageDirectory,
    [Parameter(Mandatory = $true)]
    [string] $Driver,
    [string] $OutputRoot = (Join-Path ([IO.Path]::GetTempPath()) 'WinForms14971'),
    [ValidateRange(1, 100)]
    [int] $Repeats = 5,
    [ValidateRange(0, 10)]
    [int] $Warmup = 1,
    [ValidateRange(0, 30000)]
    [int] $IdleMilliseconds = 1000
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Template measurements require an interactive Windows desktop.' }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$LifecycleOutput = (Resolve-Path -LiteralPath $LifecycleOutput).Path
$PrototypeWinFormsDirectory = (Resolve-Path -LiteralPath $PrototypeWinFormsDirectory).Path
$HostingPackageDirectory = (Resolve-Path -LiteralPath $HostingPackageDirectory).Path
$Driver = (Resolve-Path -LiteralPath $Driver).Path
$environment = Get-Content (Join-Path $LifecycleOutput 'environment.json') -Raw | ConvertFrom-Json
$generation = Get-Content (Join-Path $LifecycleOutput 'generation.json') -Raw | ConvertFrom-Json
$previous = @(Get-Content (Join-Path $LifecycleOutput 'results.json') -Raw | ConvertFrom-Json)
if ($previous.Count -eq 0 -or @($previous | Where-Object Outcome -EQ 'Failed').Count -ne 0) {
    throw 'Use sources from a successful template lifecycle run.'
}
if (@(Get-ChildItem $HostingPackageDirectory -Filter 'Microsoft.Extensions.WinForms.Hosting.*.nupkg').Count -ne 1) {
    throw 'Supply exactly one Release-built hosting package.'
}
if (-not (Test-Path (Join-Path $PrototypeWinFormsDirectory 'System.Windows.Forms.dll'))) {
    throw 'Supply the matching Release-built prototype framework.'
}
$dotnet = (Resolve-Path -LiteralPath $environment.DotNet).Path
$output = [IO.Directory]::CreateDirectory((Join-Path $OutputRoot ([Guid]::NewGuid().ToString('N')))).FullName
$logs = [IO.Directory]::CreateDirectory((Join-Path $output 'logs')).FullName
$working = [IO.Directory]::CreateDirectory((Join-Path $output 'unrelated-working-directory')).FullName
$cache = [IO.Directory]::CreateDirectory((Join-Path ([IO.Path]::GetTempPath()) ('WFP-' + [Guid]::NewGuid().ToString('N')))).FullName
$hive = Join-Path $output 'sdk-template-hive'
$rid = 'win-' + [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
$cases = [Collections.Generic.List[object]]::new()
$samples = [Collections.Generic.List[object]]::new()
$sourceHashes = @{}
$completed = $false
$failure = ''

function Invoke-CommandChecked {
    param([string] $Executable, [string[]] $Arguments, [string] $Name, [int] $TimeoutSeconds = 600)
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute = $false
    $start.WorkingDirectory = $working
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Environment['DOTNET_ROOT'] = Split-Path $dotnet
    $start.Environment['DOTNET_ROOT_X64'] = Split-Path $dotnet
    $start.Environment['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'
    $start.Environment['NO_COLOR'] = '1'
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    try {
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) { throw "Command timed out: $Name" }
        $text = $stdout.GetAwaiter().GetResult()
        $errors = $stderr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw "Command failed: $Name (exit $($process.ExitCode))`n$text`n$errors" }
        return $text
    }
    finally {
        if (-not $process.HasExited) {
            foreach ($child in Get-CimInstance Win32_Process -Filter "ParentProcessId = $($process.Id)") {
                Stop-Process -Id $child.ProcessId -Force
            }
            Stop-Process -Id $process.Id -Force
            $process.WaitForExit()
        }
        $stdout.GetAwaiter().GetResult() | Set-Content (Join-Path $logs "$Name.stdout.log")
        $stderr.GetAwaiter().GetResult() | Set-Content (Join-Path $logs "$Name.stderr.log")
        @{ Executable = $Executable; Arguments = $Arguments; WorkingDirectory = $working
            ElapsedMilliseconds = $timer.Elapsed.TotalMilliseconds; ExitCode = $process.ExitCode } |
            ConvertTo-Json -Depth 4 | Set-Content (Join-Path $logs "$Name.command.json")
        $process.Dispose()
    }
}

function Get-Median {
    param([double[]] $Values)
    $ordered = @($Values | Sort-Object)
    if ($ordered.Count -eq 0) { throw 'Cannot summarize an empty measurement set.' }
    $middle = [int][Math]::Floor($ordered.Count / 2)
    if ($ordered.Count % 2 -eq 1) { return $ordered[$middle] }
    return ($ordered[$middle - 1] + $ordered[$middle]) / 2
}

$common = @(
    "-p:DirectoryBuildPropsPath=$PSScriptRoot\PrototypeConsumer.props",
    "-p:DirectoryBuildTargetsPath=$PSScriptRoot\PrototypeConsumer.targets",
    '-p:ManagePackageVersionsCentrally=false',
    "-p:RestorePackagesPath=$cache",
    "-p:RestoreConfigFile=$repository\NuGet.Config",
    "-p:RestoreAdditionalProjectSources=$HostingPackageDirectory",
    "-p:PrototypeWinFormsDirectory=$PrototypeWinFormsDirectory",
    "-p:PrototypeCoreVersion=$($environment.CoreVersion)",
    '-p:TargetFramework=net12.0-windows',
    '-p:Configuration=Release', "-p:RuntimeIdentifier=$rid",
    '-warnaserror', '-v', 'minimal'
)

try {
    $null = Invoke-CommandChecked $dotnet @('--info') 'dotnet-info'
    foreach ($language in @('CSharp', 'VisualBasic')) {
        $basic = if ($language -eq 'CSharp') { 'BasicCSharp' } else { 'BasicVisualBasic' }
        $extension = if ($language -eq 'CSharp') { 'csproj' } else { 'vbproj' }
        $sdkLanguage = if ($language -eq 'CSharp') { 'C#' } else { 'VB' }
        $sources = @($generation.Projects | Where-Object { [IO.Path]::GetFileNameWithoutExtension($_) -eq $basic })
        if ($sources.Count -ne 1) { throw "Expected one generated $basic project." }
        $source = Split-Path $sources[0]
        $copy = [IO.Directory]::CreateDirectory((Join-Path $output "sources\$basic")).FullName
        foreach ($file in Get-ChildItem $source -Recurse -File) {
            $relative = [IO.Path]::GetRelativePath($source, $file.FullName)
            if ($relative -match '(^|\\)(bin|obj)(\\|$)') { continue }
            $target = Join-Path $copy $relative
            [void][IO.Directory]::CreateDirectory((Split-Path $target))
            Copy-Item -LiteralPath $file.FullName -Destination $target
            $hash = (Get-FileHash $file.FullName -Algorithm SHA256).Hash
            if ((Get-FileHash $target -Algorithm SHA256).Hash -ne $hash) { throw "Copy changed: $file" }
            $sourceHashes[$file.FullName] = $hash
        }
        $classic = "Current$language"
        $classicDirectory = Join-Path $output "sources\$classic"
        $null = Invoke-CommandChecked $dotnet @('new', 'winforms', '--language', $sdkLanguage, '--name', $classic,
            '--output', $classicDirectory, '--no-restore', '--no-update-check', '--debug:custom-hive', $hive) "$classic-create"
        foreach ($variant in @('current', 'basic')) {
            $name = if ($variant -eq 'current') { $classic } else { $basic }
            $project = Join-Path $output "sources\$name\$name.$extension"
            foreach ($deployment in @('fdd', 'scd')) {
                $id = "$language-$variant-$deployment"
                $publish = Join-Path $output "publish\$id"
                $selfContained = if ($deployment -eq 'scd') { 'true' } else { 'false' }
                $properties = @($common) + "-p:SelfContained=$selfContained"
                $null = Invoke-CommandChecked $dotnet (@('restore', $project) + $properties) "$id-restore"
                $null = Invoke-CommandChecked $dotnet (@('publish', $project, '--no-restore', '-o', $publish) + $properties) "$id-publish"
                $inventory = Invoke-CommandChecked $Driver @('inventory', $publish) "$id-inventory" 60 | ConvertFrom-Json
                if (-not (Test-Path (Join-Path $publish "$name.exe"))) { throw "Missing published apphost: $id" }
                $runtimeConfig = Get-Content (Join-Path $publish "$name.runtimeconfig.json") -Raw | ConvertFrom-Json
                $frameworkProperty = if ($deployment -eq 'scd') { 'includedFrameworks' } else { 'frameworks' }
                if ($null -eq $runtimeConfig.runtimeOptions.PSObject.Properties[$frameworkProperty] -or
                    (Test-Path (Join-Path $publish 'coreclr.dll')) -ne ($deployment -eq 'scd')) {
                    throw "Publish deployment mode mismatch: $id"
                }
                $machine = switch ($rid) { 'win-x64' { 'Amd64' }; 'win-x86' { 'I386' }; 'win-arm64' { 'Arm64' } }
                $apphost = @($inventory.Files | Where-Object Path -CEQ "$name.exe")
                if ($apphost.Count -ne 1 -or $apphost[0].Machine -ne $machine) { throw "Apphost architecture mismatch: $id" }
                if ($deployment -eq 'scd') {
                    $runtime = @($inventory.Files | Where-Object Path -CEQ 'coreclr.dll')
                    if ($runtime.Count -ne 1 -or $runtime[0].Machine -ne $machine) { throw "Runtime architecture mismatch: $id" }
                }
                foreach ($dll in Get-ChildItem $PrototypeWinFormsDirectory -Filter '*.dll') {
                    $published = Join-Path $publish $dll.Name
                    if (-not (Test-Path $published) -or
                        (Get-FileHash $published).Hash -ne (Get-FileHash $dll.FullName).Hash) {
                        throw "The comparison did not publish the same prototype framework: $id / $($dll.Name)"
                    }
                }
                $cases.Add([pscustomobject]@{
                    Case = $id; Language = $language; Variant = $variant; Deployment = $deployment
                    Executable = (Join-Path $publish "$name.exe"); Inventory = $inventory
                    RuntimeConfig = $runtimeConfig
                })
                if ($variant -eq 'basic') {
                    $cases.Add([pscustomobject]@{
                        Case = "$language-basic-watchers-$deployment"; Language = $language
                        Variant = 'basic-watchers'; Deployment = $deployment
                        Executable = (Join-Path $publish "$name.exe"); Inventory = $inventory
                        RuntimeConfig = $runtimeConfig
                    })
                }
                Write-Output "PUBLISHED $id"
            }
        }
    }
    $revision = & git -C $repository rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Cannot record source revision.' }
    $worktree = @(& git -C $repository status --porcelain)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot record worktree state.' }
    $manifest = @{
        Revision = $revision; Worktree = $worktree
        LifecycleOutput = $LifecycleOutput; DotNet = $dotnet; CoreVersion = $environment.CoreVersion
        Prototype = $true; PrototypeWinFormsDirectory = $PrototypeWinFormsDirectory
        HostingPackageDirectory = $HostingPackageDirectory; PackageCache = $cache
        RuntimeIdentifier = $rid; Configuration = 'Release'; EffectiveTargetFramework = 'net12.0-windows'
        Repeats = $Repeats; Warmup = $Warmup; IdleMilliseconds = $IdleMilliseconds
        CPU = @(Get-CimInstance Win32_Processor | Select-Object Name, NumberOfCores, NumberOfLogicalProcessors)
        OS = [Runtime.InteropServices.RuntimeInformation]::OSDescription
        DriverHash = (Get-FileHash $Driver).Hash
        DriverAssemblyHash = (Get-FileHash ([IO.Path]::ChangeExtension($Driver, '.dll'))).Hash
        ScriptHash = (Get-FileHash $PSCommandPath).Hash; SourceHashes = $sourceHashes
        StartedUtc = [DateTime]::UtcNow.ToString('O'); OptionalWatchers = $true
        RuntimeSwitches = @{
            DOTNET_gcServer = $env:DOTNET_gcServer; COMPlus_gcServer = $env:COMPlus_gcServer
            DOTNET_TieredCompilation = $env:DOTNET_TieredCompilation; COMPlus_TieredCompilation = $env:COMPlus_TieredCompilation
            DOTNET_ReadyToRun = $env:DOTNET_ReadyToRun; COMPlus_ReadyToRun = $env:COMPlus_ReadyToRun
            DOTNET_ROLL_FORWARD = $env:DOTNET_ROLL_FORWARD
        }
        Hybrid = @{ Outcome = 'Blocked'; Reason = 'Runnable lifecycle/WebView cleanup prerequisite is unavailable.' }
        Applications = @($cases | Select-Object Case, Executable, RuntimeConfig)
    }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $output 'manifest.json')
    for ($round = 1 - $Warmup; $round -le $Repeats; $round++) {
        $ordered = @($cases.ToArray())
        if ($round % 2 -eq 0) { [Array]::Reverse($ordered) }
        foreach ($case in $ordered) {
            $title = if ($case.Variant -eq 'current') { 'Form1' } else { "Template performance $([Guid]::NewGuid().ToString('N'))" }
            $profile = if ($case.Variant -eq 'basic-watchers') { 'watchers' } else { 'default' }
            $result = Invoke-CommandChecked $Driver @('measure', $case.Executable, $title, "$IdleMilliseconds", $profile) "$($case.Case)-round$round" 90 |
                ConvertFrom-Json
            if ($result.Outcome -ne 'Passed' -or $result.ExitCode -ne 0 -or $result.StartupMilliseconds -le 0 -or
                $result.ShutdownMilliseconds -le 0 -or $result.Settled.PrivateBytes -le 0 -or $result.Settled.WorkingSetBytes -le 0 -or
                $result.WatchersOverride -ne ($case.Variant -eq 'basic-watchers')) {
                throw "Invalid measurement: $($case.Case)"
            }
            $samples.Add([pscustomobject]@{
                Case = $case.Case; Round = $round; Warmup = ($round -le 0); Measurement = $result
            })
            Write-Output "MEASURED $($case.Case) round $round"
        }
    }
    $summary = foreach ($case in $cases) {
        $measurements = @($samples | Where-Object { $_.Case -eq $case.Case -and -not $_.Warmup } |
            ForEach-Object Measurement)
        if ($measurements.Count -ne $Repeats) { throw "Missing measured rounds: $($case.Case)" }
        [pscustomobject]@{
            Case = $case.Case; Language = $case.Language; Variant = $case.Variant; Deployment = $case.Deployment
            Samples = $measurements.Count
            StartupMedianMs = (Get-Median $measurements.StartupMilliseconds)
            StartupMinMs = ($measurements.StartupMilliseconds | Measure-Object -Minimum).Minimum
            StartupMaxMs = ($measurements.StartupMilliseconds | Measure-Object -Maximum).Maximum
            ShutdownMedianMs = (Get-Median $measurements.ShutdownMilliseconds)
            PrivateMedianBytes = (Get-Median @($measurements | ForEach-Object { $_.Settled.PrivateBytes }))
            WorkingSetMedianBytes = (Get-Median @($measurements | ForEach-Object { $_.Settled.WorkingSetBytes }))
            KernelHandleMedian = (Get-Median @($measurements | ForEach-Object { $_.Settled.KernelHandles }))
            GdiMedian = (Get-Median @($measurements | ForEach-Object { $_.Settled.GdiObjects }))
            UserMedian = (Get-Median @($measurements | ForEach-Object { $_.Settled.UserObjects }))
            ThreadMedian = (Get-Median @($measurements | ForEach-Object { $_.Settled.Threads }))
            LoadedModuleMedian = (Get-Median @($measurements | ForEach-Object { $_.Settled.LoadedModules }))
            FileCount = $case.Inventory.FileCount; AssemblyCount = $case.Inventory.AssemblyCount
            PublishBytes = $case.Inventory.TotalBytes
        }
    }
    $summary | Export-Csv (Join-Path $output 'summary.csv') -NoTypeInformation
    $metrics = @('StartupMedianMs', 'ShutdownMedianMs', 'PrivateMedianBytes', 'WorkingSetMedianBytes',
        'KernelHandleMedian', 'GdiMedian', 'UserMedian', 'ThreadMedian', 'LoadedModuleMedian', 'FileCount', 'AssemblyCount', 'PublishBytes')
    $comparisons = foreach ($basic in $summary | Where-Object Variant -NE 'current') {
        $baseline = @($summary | Where-Object { $_.Variant -eq 'current' -and $_.Language -eq $basic.Language -and $_.Deployment -eq $basic.Deployment })
        if ($baseline.Count -ne 1) { throw 'Missing matching classic baseline.' }
        $row = [ordered]@{ Language = $basic.Language; Deployment = $basic.Deployment; Variant = $basic.Variant }
        foreach ($metric in $metrics) {
            $row["Delta$metric"] = $basic.$metric - $baseline[0].$metric
        }
        [pscustomobject]$row
    }
    $comparisons | Export-Csv (Join-Path $output 'comparisons.csv') -NoTypeInformation
    $optional = foreach ($watchers in $summary | Where-Object Variant -EQ 'basic-watchers') {
        $defaults = @($summary | Where-Object { $_.Variant -eq 'basic' -and $_.Language -eq $watchers.Language -and $_.Deployment -eq $watchers.Deployment })
        if ($defaults.Count -ne 1) { throw 'Missing matching basic defaults.' }
        $row = [ordered]@{ Language = $watchers.Language; Deployment = $watchers.Deployment }
        foreach ($metric in $metrics) { $row["Delta$metric"] = $watchers.$metric - $defaults[0].$metric }
        [pscustomobject]$row
    }
    $optional | Export-Csv (Join-Path $output 'optional-comparisons.csv') -NoTypeInformation
    foreach ($file in $sourceHashes.Keys) {
        if ((Get-FileHash $file).Hash -ne $sourceHashes[$file]) { throw "Original source changed: $file" }
    }
    $completed = $true
}
catch {
    $failure = $_.ToString()
    throw
}
finally {
    ConvertTo-Json -InputObject $samples.ToArray() -Depth 8 | Set-Content (Join-Path $output 'samples.json')
    @{ Outcome = $(if ($completed) { 'Passed' } else { 'Failed' }); MeasuredSamples = @($samples | Where-Object { -not $_.Warmup }).Count
        Error = $failure; Hybrid = 'Blocked'; RemoteCI = $(if ($env:TF_BUILD -eq 'True') { 'Executed' } else { 'NotRun' }) } |
        ConvertTo-Json | Set-Content (Join-Path $output 'result.json')
    Write-Output "Template performance evidence: $output"
}
