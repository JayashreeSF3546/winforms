param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [ValidateRange(1, 100000)]
    [int] $Cycles = 100,
    [ValidateRange(0, 1000)]
    [int] $Warmup = 5,
    [ValidateRange(1, 20)]
    [int] $Repeats = 3,
    [ValidateSet('light', 'component-manager')]
    [string[]] $Backends = @('light'),
    [ValidateSet('empty', 'configuration', 'settings', 'logging', 'health', 'all')]
    [string[]] $Workloads = @('empty', 'configuration', 'settings', 'logging', 'health', 'all')
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
$project = Join-Path $PSScriptRoot 'WinFormsInfrastructurePerformance.csproj'
$bin = Join-Path $root "artifacts\bin\WinFormsInfrastructurePerformance\$Configuration"
$candidates = @(Get-ChildItem -LiteralPath $bin -Directory |
    Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'WinFormsInfrastructurePerformance.exe') })
if ($candidates.Count -ne 1) {
    throw "Build a single current TFM first with build.cmd -projects $project -configuration $Configuration."
}

$exe = Join-Path $candidates[0].FullName 'WinFormsInfrastructurePerformance.exe'
$directory = Join-Path $root ('artifacts\log\InfrastructurePerformance\' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$null = New-Item -ItemType Directory -Path $directory
$source = & git -C $root rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot record source revision.' }
$changes = @(& git -C $root status --porcelain)
if ($LASTEXITCODE -ne 0) { throw 'Cannot record worktree status.' }
[ordered]@{
    Source = $source
    Worktree = $changes
    Configuration = $Configuration
    Cycles = $Cycles
    Warmup = $Warmup
    Repeats = $Repeats
    Backends = $Backends
    Workloads = $Workloads
    ProcessorCount = [Environment]::ProcessorCount
    Processor = @(Get-CimInstance Win32_Processor | Select-Object -ExpandProperty Name)
    StopwatchFrequency = [Diagnostics.Stopwatch]::Frequency
    ProbeSha256 = (Get-FileHash (Join-Path $candidates[0].FullName 'WinFormsInfrastructurePerformance.dll')).Hash
    WinFormsSha256 = (Get-FileHash (Join-Path $candidates[0].FullName 'System.Windows.Forms.dll')).Hash
    CompanionSha256 = (Get-FileHash (Join-Path $candidates[0].FullName 'Microsoft.Extensions.WinForms.Hosting.dll')).Hash
    InfrastructureBinaries = @(Get-ChildItem -LiteralPath $candidates[0].FullName -Filter 'Microsoft.Extensions.*.dll' |
        ForEach-Object { [ordered]@{ Name = $_.Name; Sha256 = (Get-FileHash $_.FullName).Hash } })
    RuntimeSwitches = [ordered]@{
        DOTNET_gcServer = $env:DOTNET_gcServer
        DOTNET_TieredCompilation = $env:DOTNET_TieredCompilation
        DOTNET_ReadyToRun = $env:DOTNET_ReadyToRun
        COMPlus_gcServer = $env:COMPlus_gcServer
        COMPlus_TieredCompilation = $env:COMPlus_TieredCompilation
        COMPlus_ReadyToRun = $env:COMPlus_ReadyToRun
    }
    RuntimeConfiguration = Get-Content (Join-Path $candidates[0].FullName 'WinFormsInfrastructurePerformance.runtimeconfig.json') -Raw
    StartedUtc = [DateTime]::UtcNow.ToString('O')
    OrderPolicy = 'Reverse all cases on even repeats; no parallel measurements.'
} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $directory 'manifest.json') -Encoding utf8

function Get-Median($Values) {
    $sorted = @($Values | Sort-Object)
    if ($sorted.Count -eq 0) { throw 'No measurements to summarize.' }
    return ($sorted[[int][Math]::Floor(($sorted.Count - 1) / 2)] +
        $sorted[[int][Math]::Floor($sorted.Count / 2)]) / 2
}

$cases = @()
foreach ($backend in $Backends) {
    $cases += [pscustomobject]@{ Mode = 'hostless'; Workload = 'empty'; Watchers = $false; Backend = $backend }
    foreach ($workload in $Workloads) {
        $watcherCases = if ($workload -eq 'configuration') { @($false, $true) } else { @($false) }
        foreach ($watchers in $watcherCases) {
            foreach ($mode in @('conventional', 'integrated')) {
                $cases += [pscustomobject]@{ Mode = $mode; Workload = $workload; Watchers = $watchers; Backend = $backend }
            }
        }
    }
}

$summary = @()
$operations = @()
$comparisons = @()
for ($repeat = 1; $repeat -le $Repeats; $repeat++) {
    $orderedCases = @($cases)
    if ($repeat % 2 -eq 0) { [Array]::Reverse($orderedCases) }
    foreach ($case in $orderedCases) {
        $name = "$repeat-$($case.Mode)-$($case.Workload)-watchers$($case.Watchers)-$($case.Backend)"
        Write-Host "Measuring $name"
        $start = [Diagnostics.ProcessStartInfo]::new()
        $start.FileName = $exe
        $start.WorkingDirectory = $root
        $start.UseShellExecute = $false
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $start.Environment['DOTNET_ROOT'] = Join-Path $root '.dotnet'
        foreach ($argument in @('--mode', $case.Mode, '--workload', $case.Workload, '--cycles', "$Cycles", '--warmup', "$Warmup")) {
            $start.ArgumentList.Add($argument)
        }
        if ($case.Watchers) { $start.ArgumentList.Add('--watchers') }
        if ($case.Backend -eq 'component-manager') { $start.ArgumentList.Add('--component-manager') }
        $process = [Diagnostics.Process]::new()
        $process.StartInfo = $start
        $started = $false
        $stderr = $null
        $firstLine = $null
        $stdout = $null
        $clock = [Diagnostics.Stopwatch]::StartNew()
        try {
            $started = $process.Start()
            $stderr = $process.StandardError.ReadToEndAsync()
            $firstLine = $process.StandardOutput.ReadLineAsync()
            if (-not $firstLine.Wait([TimeSpan]::FromSeconds(60))) { throw "No dispatch: $name" }
            $launch = $clock.Elapsed.TotalMilliseconds
            $stdout = $process.StandardOutput.ReadToEndAsync()
            if (-not $process.WaitForExit(600000)) { throw "Process watchdog: $name" }
            $errors = $stderr.GetAwaiter().GetResult()
            $raw = $stdout.GetAwaiter().GetResult()
            if ($process.ExitCode -ne 0 -or $errors.Length -ne 0 -or $firstLine.Result -ne 'READY') {
                throw "Measurement failed: $name`n$errors`n$raw"
            }
            $report = $raw | ConvertFrom-Json
            if ($report.Cycles.Count -ne $Cycles) { throw "Incomplete report: $name" }
            $report | Add-Member -NotePropertyName LaunchToFirstDispatchMilliseconds -NotePropertyValue $launch
            $report | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $directory "$name.json") -Encoding utf8
            $row = [pscustomobject]@{
                Repeat = $repeat
                Mode = $case.Mode
                Workload = $case.Workload
                Watchers = $case.Watchers
                Backend = $case.Backend
                LaunchToDispatchMs = $launch
                FirstBuildMs = $report.FirstUse.ConfigureBuild.Milliseconds
                FirstStartupMs = $report.FirstUse.StartupToDispatchMilliseconds
                BuildMedianMs = Get-Median $report.Cycles.ConfigureBuild.Milliseconds
                StartupMedianMs = Get-Median $report.Cycles.StartupToDispatchMilliseconds
                ShutdownMedianMs = Get-Median $report.Cycles.ShutdownMilliseconds
                LifetimeMedianMs = Get-Median $report.Cycles.Lifetime.Milliseconds
                AllocationMedianBytes = Get-Median $report.Cycles.Lifetime.AllocatedBytes
                ManagedDeltaBytes = $report.Checkpoints[-1].ManagedBytes - $report.Checkpoints[0].ManagedBytes
                PrivateDeltaBytes = $report.Checkpoints[-1].PrivateBytes - $report.Checkpoints[0].PrivateBytes
                WorkingSetDeltaBytes = $report.Checkpoints[-1].WorkingSetBytes - $report.Checkpoints[0].WorkingSetBytes
                KernelHandleDelta = $report.Checkpoints[-1].KernelHandles - $report.Checkpoints[0].KernelHandles
                ThreadDelta = $report.Checkpoints[-1].Threads - $report.Checkpoints[0].Threads
                GdiDelta = $report.Checkpoints[-1].GdiObjects - $report.Checkpoints[0].GdiObjects
                UserDelta = $report.Checkpoints[-1].UserObjects - $report.Checkpoints[0].UserObjects
                FinalLiveForms = $report.Checkpoints[-1].LiveForms
                FinalLiveApplications = $report.Checkpoints[-1].LiveApplications
                FinalLiveProviders = $report.Checkpoints[-1].LiveProviders
            }
            $summary += $row
            foreach ($property in $report.Cycles[0].Operations.PSObject.Properties) {
                $metrics = @($report.Cycles | ForEach-Object { $_.Operations.($property.Name) })
                $medianMs = Get-Median @($metrics | ForEach-Object { $_.Milliseconds / $_.Operations })
                $sortedMs = @($metrics | ForEach-Object { $_.Milliseconds / $_.Operations } | Sort-Object)
                $operations += [pscustomobject]@{
                    Repeat = $repeat
                    Mode = $case.Mode
                    Workload = $case.Workload
                    Watchers = $case.Watchers
                    Backend = $case.Backend
                    Operation = $property.Name
                    MedianMsPerOperation = $medianMs
                    P95MsPerOperation = $sortedMs[[int][Math]::Ceiling($sortedMs.Count * 0.95) - 1]
                    MedianBytesPerOperation = Get-Median @($metrics | ForEach-Object { $_.AllocatedBytes / $_.Operations })
                    MedianOperationsPerSecond = if ($medianMs -gt 0) { 1000 / $medianMs } else { $null }
                }
            }
            $summary | Export-Csv (Join-Path $directory 'summary.csv') -NoTypeInformation
            $operations | Export-Csv (Join-Path $directory 'operations.csv') -NoTypeInformation
        }
        finally {
            if ($started -and -not $process.HasExited) {
                Stop-Process -Id $process.Id
                $process.WaitForExit()
            }
            if ($started) {
                if ($null -ne $stderr) {
                    $stderr.GetAwaiter().GetResult() |
                        Set-Content (Join-Path $directory "$name.stderr.txt") -Encoding utf8
                }
                if ($null -ne $firstLine) {
                    $head = $firstLine.GetAwaiter().GetResult()
                    $tail = if ($null -ne $stdout) { $stdout.GetAwaiter().GetResult() } else { $process.StandardOutput.ReadToEnd() }
                    "$head`n$tail" | Set-Content (Join-Path $directory "$name.stdout.txt") -Encoding utf8
                }
            }
            $process.Dispose()
        }
    }
}

foreach ($integrated in @($summary | Where-Object Mode -eq 'integrated')) {
    $baseline = @($summary | Where-Object {
        $_.Repeat -eq $integrated.Repeat -and $_.Mode -eq 'conventional' -and
        $_.Workload -eq $integrated.Workload -and $_.Watchers -eq $integrated.Watchers -and
        $_.Backend -eq $integrated.Backend
    })
    if ($baseline.Count -ne 1) { throw 'Missing equivalent baseline.' }
    $comparisons += [pscustomobject]@{
        Repeat = $integrated.Repeat
        Workload = $integrated.Workload
        Watchers = $integrated.Watchers
        Backend = $integrated.Backend
        BuildMedianDeltaMs = $integrated.BuildMedianMs - $baseline[0].BuildMedianMs
        StartupMedianDeltaMs = $integrated.StartupMedianMs - $baseline[0].StartupMedianMs
        ShutdownMedianDeltaMs = $integrated.ShutdownMedianMs - $baseline[0].ShutdownMedianMs
        LifetimeMedianDeltaMs = $integrated.LifetimeMedianMs - $baseline[0].LifetimeMedianMs
        AllocationMedianDeltaBytes = $integrated.AllocationMedianBytes - $baseline[0].AllocationMedianBytes
    }
}
$comparisons | Export-Csv (Join-Path $directory 'comparisons.csv') -NoTypeInformation
$summary | Format-Table Repeat, Mode, Workload, Watchers, StartupMedianMs, ShutdownMedianMs, AllocationMedianBytes -AutoSize
Write-Host "Raw measurements, source manifest, checkpoints and summaries: $directory"
