param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [ValidateRange(1, 100000)]
    [int] $Cycles = 100,
    [ValidateRange(0, 1000)]
    [int] $Warmup = 5
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
$project = Join-Path $root 'src\test\integration\WinFormsApplicationLifetimePerformance\WinFormsApplicationLifetimePerformance.csproj'
$exe = Join-Path $root "artifacts\bin\WinFormsApplicationLifetimePerformance\$Configuration\net11.0\WinFormsApplicationLifetimePerformance.exe"
if (-not (Test-Path $exe)) {
    throw "Build first: .\build.cmd -projects $project -configuration $Configuration -verbosity minimal. Update the TFM path if NetCurrent has changed."
}

$env:DOTNET_ROOT = Join-Path $root '.dotnet'
$directory = Join-Path $root ('artifacts\log\LifetimePerformance\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$summary = @()
foreach ($backend in @('light', 'component-manager')) {
    foreach ($mode in @('legacy', 'builder', 'hosted')) {
        $start = [Diagnostics.ProcessStartInfo]::new()
        $start.FileName = $exe
        $start.Arguments = "--mode $mode --cycles $Cycles --warmup $Warmup"
        if ($backend -eq 'component-manager') {
            $start.Arguments += ' --component-manager'
        }

        $start.UseShellExecute = $false
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $process = [Diagnostics.Process]::new()
        $process.StartInfo = $start
        $started = $false
        $clock = [Diagnostics.Stopwatch]::StartNew()
        try {
            $started = $process.Start()
            $stderr = $process.StandardError.ReadToEndAsync()
            $ready = $process.StandardOutput.ReadLineAsync()
            if (-not $ready.Wait([TimeSpan]::FromSeconds(60))) {
                throw "No first UI dispatch from $mode/$backend."
            }

            $launchToReady = $clock.Elapsed.TotalMilliseconds
            if ($ready.Result -ne 'READY') {
                throw "Unexpected first-dispatch signal from $mode/$backend : $($ready.Result)"
            }

            $json = $process.StandardOutput.ReadToEndAsync()
            if (-not $process.WaitForExit(600000)) {
                throw "Measurement watchdog expired for $mode/$backend."
            }

            $errorText = $stderr.GetAwaiter().GetResult()
            if ($process.ExitCode -ne 0) {
                throw "Measurement failed for $mode/$backend : $errorText"
            }

            $report = $json.GetAwaiter().GetResult() | ConvertFrom-Json
            $report | Add-Member -NotePropertyName LaunchToFirstDispatchMilliseconds -NotePropertyValue $launchToReady
            $report | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $directory "$mode-$backend.json") -Encoding utf8
            $startup = @($report.WarmCycles.StartupMilliseconds | Sort-Object)
            $shutdown = @($report.WarmCycles.ShutdownMilliseconds | Sort-Object)
            $allocations = @($report.WarmCycles.AllocatedBytes | Sort-Object)
            $middle = [int][Math]::Floor($Cycles / 2)
            $lower = [int][Math]::Floor(($Cycles - 1) / 2)
            $summary += [pscustomobject]@{
                Mode = $mode
                Backend = $backend
                LaunchToReadyMs = [Math]::Round($launchToReady, 3)
                FirstUseStartupMs = [Math]::Round($report.FirstUse.StartupMilliseconds, 3)
                WarmStartupMedianMs = [Math]::Round(($startup[$lower] + $startup[$middle]) / 2, 3)
                ShutdownMedianMs = [Math]::Round(($shutdown[$lower] + $shutdown[$middle]) / 2, 3)
                AllocationMedianBytes = ($allocations[$lower] + $allocations[$middle]) / 2
                ManagedHeapDeltaBytes = $report.Checkpoints[-1].ManagedHeapBytes - $report.Checkpoints[0].ManagedHeapBytes
                HandleDelta = $report.Checkpoints[-1].Handles - $report.Checkpoints[0].Handles
                ThreadDelta = $report.Checkpoints[-1].Threads - $report.Checkpoints[0].Threads
                GdiDelta = $report.Checkpoints[-1].GdiObjects - $report.Checkpoints[0].GdiObjects
                UserDelta = $report.Checkpoints[-1].UserObjects - $report.Checkpoints[0].UserObjects
            }
        }
        finally {
            if ($started -and -not $process.HasExited) {
                Stop-Process -Id $process.Id
                $process.WaitForExit()
            }

            $process.Dispose()
        }
    }
}

$summary | Export-Csv (Join-Path $directory 'summary.csv') -NoTypeInformation
$summary | Format-Table -AutoSize
Write-Host "Raw checkpoints and summary: $directory"
