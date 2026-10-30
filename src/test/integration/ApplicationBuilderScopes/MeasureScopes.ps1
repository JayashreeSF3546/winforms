param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [ValidateRange(1, 100000)]
    [int] $Cycles = 1000,
    [ValidateRange(0, 1000)]
    [int] $Warmup = 10,
    [ValidateRange(1, 100000)]
    [int] $Interval = 100
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
$exe = Join-Path $root "artifacts\bin\ApplicationBuilderScopes.CSharp\$Configuration\net11.0\ApplicationBuilderScopes.CSharp.exe"
if (-not (Test-Path $exe)) {
    throw 'Build the CSharp scope sample first with build.cmd. Update the TFM path if NetCurrent has changed.'
}

$env:DOTNET_ROOT = Join-Path $root '.dotnet'
$directory = Join-Path $root ('artifacts\log\ScopeStress\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$revision = (& git -C $root rev-parse HEAD)
if ($LASTEXITCODE -ne 0) {
    throw 'Could not record the source revision.'
}

@{
    Revision = $revision
    WorkingTree = @(& git -C $root status --short)
    Configuration = $Configuration
    Cycles = $Cycles
    Warmup = $Warmup
    Interval = $Interval
} | ConvertTo-Json | Set-Content (Join-Path $directory 'source.json') -Encoding utf8

$summary = @()
foreach ($backend in @('light', 'component-manager')) {
    foreach ($mode in @('manual', 'scoped')) {
        $start = [Diagnostics.ProcessStartInfo]::new()
        $start.FileName = $exe
        $start.Arguments = "--stress --mode $mode --cycles $Cycles --warmup $Warmup --interval $Interval"
        if ($backend -eq 'component-manager') {
            $start.Arguments += ' --component-manager'
        }

        $start.UseShellExecute = $false
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $process = [Diagnostics.Process]::new()
        $process.StartInfo = $start
        $started = $false
        try {
            Write-Host "Measuring $mode/$backend ($Cycles cycles, three windows per cycle)..."
            $started = $process.Start()
            $stdout = $process.StandardOutput.ReadToEndAsync()
            $stderr = $process.StandardError.ReadToEndAsync()
            if (-not $process.WaitForExit(1200000)) {
                throw "Stress watchdog expired for $mode/$backend. Reports: $directory"
            }

            $transcript = $stdout.GetAwaiter().GetResult()
            $errors = $stderr.GetAwaiter().GetResult()
            $transcript | Set-Content (Join-Path $directory "$mode-$backend.stdout.txt") -Encoding utf8
            $errors | Set-Content (Join-Path $directory "$mode-$backend.stderr.txt") -Encoding utf8
            if ($process.ExitCode -ne 0 -or -not [string]::IsNullOrWhiteSpace($errors)) {
                throw "Stress failed for $mode/$backend (exit $($process.ExitCode)): $errors"
            }

            $lines = $transcript -split "`n", 2
            if ($lines[0].TrimEnd("`r") -ne 'READY' -or $lines.Count -ne 2) {
                throw "Missing dispatched-workload signal for $mode/$backend."
            }

            $report = $lines[1] | ConvertFrom-Json
            $report | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $directory "$mode-$backend.json") -Encoding utf8
            $report.Checkpoints | Export-Csv (Join-Path $directory "$mode-$backend.checkpoints.csv") -NoTypeInformation
            $first = $report.Checkpoints[0]
            $last = $report.Checkpoints[-1]
            $milliseconds = ($report.Checkpoints | Measure-Object -Property BatchMilliseconds -Sum).Sum
            $allocated = ($report.Checkpoints | Measure-Object -Property AllocatedBytes -Sum).Sum
            $summary += [pscustomobject]@{
                Mode = $mode
                Backend = $backend
                MeasuredWindows = 3 * $Cycles
                WindowsPerSecond = [Math]::Round(3000 * $Cycles / $milliseconds, 3)
                BytesPerWindow = [Math]::Round($allocated / (3 * $Cycles), 1)
                ManagedHeapDeltaBytes = $last.ManagedHeapBytes - $first.ManagedHeapBytes
                PrivateBytesDelta = $last.PrivateBytes - $first.PrivateBytes
                HandleDelta = $last.Handles - $first.Handles
                GdiDelta = [long]$last.GdiObjects - [long]$first.GdiObjects
                UserDelta = [long]$last.UserObjects - [long]$first.UserObjects
                ServicesCreated = $report.ServicesCreated
                ServicesDisposed = $report.ServicesDisposed
                ApplicationRetained = $report.ApplicationRetained
                ContextRetained = $report.ContextRetained
                CoordinatorRetained = $report.CoordinatorRetained
            }
            $summary | Export-Csv (Join-Path $directory 'summary.csv') -NoTypeInformation
            Write-Host "Completed $mode/$backend; raw checkpoints: $directory"
        }
        finally {
            if ($started -and -not $process.HasExited) {
                $process.Kill($true)
                $process.WaitForExit()
            }

            $process.Dispose()
        }
    }
}

$summary | Format-Table -AutoSize
Write-Host "Inspect all warmed checkpoints for trends; successful cleanup is not a resource-budget pass: $directory"
