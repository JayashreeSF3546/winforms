# Licensed to the .NET Foundation under one or more agreements.
# The .NET Foundation licenses this file to you under the MIT license.

[CmdletBinding()]
param(
    [string] $ArtifactsDir = (Join-Path $env:TEMP 'WinForms14978Artifacts'),
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [ValidateSet('implicit-x64', 'x64', 'x86', 'arm64')]
    [string] $Platform = 'implicit-x64',
    [ValidateRange(1, 10000)]
    [int] $Cycles = 100,
    [ValidateRange(0, 100)]
    [int] $Warmup = 5,
    [ValidateRange(1, 100)]
    [int] $Activations = 20,
    [ValidateRange(1, 20)]
    [int] $Launches = 3,
    [string] $EvidenceDirectory = (Join-Path $env:TEMP ("WinForms14981-" + [guid]::NewGuid().ToString('N')))
)

$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\..'))
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if ($evidence -eq $repo -or $evidence.StartsWith($repo + '\', [StringComparison]::OrdinalIgnoreCase) -or
    (Test-Path $evidence)) {
    throw 'Select a new private evidence directory outside the checkout.'
}

$bin = Join-Path $ArtifactsDir 'bin\System.Windows.Forms.UI.IntegrationTests'
if ($Platform -ne 'implicit-x64') { $bin = Join-Path $bin $Platform }
$bin = Join-Path $bin "$Configuration\net12.0-windows7.0\Parity"
$env:DOTNET_ROOT = Join-Path $repo '.dotnet'
$env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
$utf8 = [Text.UTF8Encoding]::new($false)
[void][IO.Directory]::CreateDirectory($evidence)
$summary = [Collections.Generic.List[object]]::new()
$activationRows = [Collections.Generic.List[object]]::new()
$payloads = [Collections.Generic.List[object]]::new()

function Start-Worker($language, $arguments, $name) {
    $exe = Join-Path $bin "ApplicationBuilderParity.$language\ApplicationBuilderParity.$language.exe"
    $info = [Diagnostics.ProcessStartInfo]::new($exe)
    $info.UseShellExecute = $false
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($argument in $arguments) { $info.ArgumentList.Add([string] $argument) }
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $process = [Diagnostics.Process]::Start($info)
    return [pscustomobject]@{
        Process = $process; Errors = $process.StandardError.ReadToEndAsync(); Clock = $clock
        Name = $name; Transcript = [Collections.Generic.List[string]]::new()
    }
}

function Read-Line($worker) {
    $line = $worker.Process.StandardOutput.ReadLineAsync()
    if (!$line.Wait(60000)) { throw "Signal watchdog expired: $($worker.Name)" }
    $text = $line.GetAwaiter().GetResult()
    if ($null -eq $text) { throw "Worker exited before signal: $($worker.Name)" }
    $worker.Transcript.Add($text)
    return $text
}

function Stop-Owned($worker) {
    if (!$worker.Process.HasExited) {
        $worker.Process.Kill($true)
        if (!$worker.Process.WaitForExit(10000)) { throw "Owned worker did not terminate: $($worker.Name)" }
    }

    $log = Join-Path $evidence "$($worker.Name).log"
    if (!(Test-Path $log)) {
        $errors = $worker.Errors.GetAwaiter().GetResult()
        [IO.File]::WriteAllText($log,
            "Incomplete run; retained signals:`n" + ($worker.Transcript -join "`n") + "`n" + $errors, $utf8)
    }

    $worker.Process.Dispose()
}

function Complete-Worker($worker) {
    $tail = $worker.Process.StandardOutput.ReadToEndAsync()
    if (!$worker.Process.WaitForExit(600000)) { throw "Lifetime watchdog expired: $($worker.Name)" }
    $text = $tail.GetAwaiter().GetResult()
    $errors = $worker.Errors.GetAwaiter().GetResult()
    $raw = ($worker.Transcript -join [Environment]::NewLine) + [Environment]::NewLine + $text
    [IO.File]::WriteAllText((Join-Path $evidence "$($worker.Name).log"), $raw + $errors, $utf8)
    if ($worker.Process.ExitCode -ne 0 -or $errors.Length) {
        throw "Worker failed: $($worker.Name); exit=$($worker.Process.ExitCode); $errors"
    }

    return $text.Trim()
}

function Median($values) {
    $ordered = @($values | Sort-Object)
    return ($ordered[[int][Math]::Floor(($ordered.Count - 1) / 2)] +
        $ordered[[int][Math]::Floor($ordered.Count / 2)]) / 2
}

function Read-Checkpoint($primary) {
    $primary.Process.StandardInput.WriteLine('snapshot')
    $line = Read-Line $primary
    if (!$line.StartsWith('CHECKPOINT ')) { throw "Missing resource checkpoint: $line" }
    return ($line.Substring(11) | ConvertFrom-Json)
}

foreach ($language in 'VisualBasic', 'CSharp') {
    $directory = Join-Path $bin "ApplicationBuilderParity.$language"
    foreach ($file in Get-ChildItem $directory -File | Where-Object Extension -in '.dll', '.exe', '.json') {
        $payloads.Add([ordered]@{
            Language = $language; File = $file.Name; SHA256 = (Get-FileHash $file.FullName).Hash
        })
    }
}

$variants = @(
    @{ Language = 'VisualBasic'; Mode = 'legacy' },
    @{ Language = 'VisualBasic'; Mode = 'shared' },
    @{ Language = 'CSharp'; Mode = 'shared' }
)

foreach ($variant in $variants) {
    $language = $variant.Language
    $mode = $variant.Mode
    foreach ($workload in 'empty', 'splash', 'network') {
        $name = "$language-$mode-$workload"
        $worker = Start-Worker $language @('measure', $mode, $workload, $Cycles, $Warmup) $name
        try {
            if ((Read-Line $worker) -ne 'READY') { throw "Missing UI dispatch: $name" }
            $launchMs = $worker.Clock.Elapsed.TotalMilliseconds
            $json = Complete-Worker $worker
            $report = $json | ConvertFrom-Json
            if ($report.Samples.Count -ne $Cycles) { throw "Wrong cycle count: $name" }
            [IO.File]::WriteAllText((Join-Path $evidence "$name.json"), $json, $utf8)
            $summary.Add([pscustomobject]@{
                Language = $language; Mode = $mode; Workload = $workload
                LaunchToDispatchMs = $launchMs; FirstUseStartupMs = $report.FirstUse.StartupMilliseconds
                WarmStartupMedianMs = (Median $report.Samples.StartupMilliseconds)
                ShutdownMedianMs = (Median $report.Samples.ShutdownMilliseconds)
                AllocatedBytesMedian = (Median $report.Samples.AllocatedBytes)
                ManagedHeapDelta = $report.Checkpoints[-1].ManagedHeapBytes - $report.Checkpoints[0].ManagedHeapBytes
                HandleDelta = $report.Checkpoints[-1].Handles - $report.Checkpoints[0].Handles
                ThreadDelta = $report.Checkpoints[-1].Threads - $report.Checkpoints[0].Threads
                UserDelta = $report.Checkpoints[-1].UserObjects - $report.Checkpoints[0].UserObjects
                GdiDelta = $report.Checkpoints[-1].GdiObjects - $report.Checkpoints[0].GdiObjects
                LiveForms = $report.Checkpoints[-1].LiveForms
                LiveApplications = $report.Checkpoints[-1].LiveApplications
            })
            Write-Host "$name completed: $Cycles measured cycles."
        }
        finally { Stop-Owned $worker }
    }

    for ($launch = 0; $launch -lt $Launches; $launch++) {
        $id = [guid]::NewGuid().ToString('N')
        $name = "$language-$mode-ipc-$launch"
        $primary = Start-Worker $language @('measure-ipc', $mode, $id, 'owner') $name
        try {
            # The legacy framework's assembly-derived identity is intentionally unchanged.
            # Require our new owner to become ready before launching any secondary.
            if ((Read-Line $primary) -ne 'READY') { throw "No owned primary: $name. Do not run overlapping benchmark copies." }
            $checkpoints = [Collections.Generic.List[object]]::new()
            for ($index = -$Warmup; $index -lt $Activations; $index++) {
                if ($index -eq 0) { $checkpoints.Add((Read-Checkpoint $primary)) }
                $client = Start-Worker $language @('measure-ipc', $mode, $id, $index) "$name-client-$index"
                try {
                    $line = Read-Line $primary
                    $launchToCallback = $client.Clock.Elapsed.TotalMilliseconds
                    if (!$line.StartsWith('CALLBACK ')) { throw "Missing activation: $line" }
                    $callback = $line.Substring(9) | ConvertFrom-Json
                    if ($callback.Sequence -ne [string]$index) { throw 'Wrong activation correlation.' }
                    $result = Complete-Worker $client
                    $expected = if ($mode -eq 'shared') { 'RESULT Handled' } else { 'RESULT LegacyReturned' }
                    if ($result -ne $expected) { throw "Unexpected delivery observation: $result" }
                    if ($index -ge 0) {
                        $activationRows.Add([pscustomobject]@{
                            Language = $language; Mode = $mode; Launch = $launch; Sequence = $index
                            RequestToCallbackMs = $callback.Milliseconds; LaunchToCallbackMs = $launchToCallback
                            DeliveryResult = $result
                        })
                    }
                }
                finally { Stop-Owned $client }
                if ($index -ge 0 -and (($index + 1) % 5 -eq 0 -or $index + 1 -eq $Activations)) {
                    $checkpoints.Add((Read-Checkpoint $primary))
                }
            }

            $primary.Process.StandardInput.WriteLine('stop')
            $json = Complete-Worker $primary
            [IO.File]::WriteAllText((Join-Path $evidence "$name.json"), $json, $utf8)
            [IO.File]::WriteAllText((Join-Path $evidence "$name-checkpoints.json"),
                (ConvertTo-Json -InputObject $checkpoints.ToArray() -Depth 8), $utf8)
            Write-Host "$name completed: $Activations correlated callbacks and owned exit."
        }
        finally { Stop-Owned $primary }
    }
}

$summary | Export-Csv (Join-Path $evidence 'lifetimes.csv') -NoTypeInformation
$activationRows | Export-Csv (Join-Path $evidence 'activations.csv') -NoTypeInformation
$manifest = [ordered]@{
    SchemaVersion = 1; Issue = 14981; UTC = [DateTime]::UtcNow.ToString('O')
    Revision = (& git -C $repo rev-parse HEAD); Dirty = [bool](& git -C $repo status --porcelain)
    Configuration = $Configuration; Platform = $Platform; Cycles = $Cycles; Warmup = $Warmup
    SDK = (Get-Content (Join-Path $repo 'global.json') -Raw | ConvertFrom-Json).sdk.version
    ActivationsPerOwner = $Activations; OwnerLaunches = $Launches; Payloads = $payloads.ToArray()
    ActivationWarmupPerOwner = $Warmup
    Sources = @(
        'src\test\integration\ApplicationBuilderParity\CSharp\BenchmarkMeasurements.cs',
        'src\test\integration\ApplicationBuilderParity\CSharp\PerformanceWorkload.cs',
        'src\test\integration\ApplicationBuilderParity\CSharp\Program.cs',
        'src\test\integration\ApplicationBuilderParity\CSharp\ApplicationBuilderParity.CSharp.csproj',
        'src\test\integration\ApplicationBuilderParity\VisualBasic\PerformanceWorkload.vb',
        'src\test\integration\ApplicationBuilderParity\VisualBasic\Program.vb',
        'src\test\integration\ApplicationBuilderParity\Measure-Parity.ps1',
        'src\test\integration\UIIntegrationTests\WinFormsApplicationParityTests.Performance.cs'
    ) | ForEach-Object { [ordered]@{ File = $_; SHA256 = (Get-FileHash (Join-Path $repo $_)).Hash } }
    Limits = 'Local finite run, sequential order, no OS-cache flush, native hardware/remote CI not inferred. Raw logs can contain private paths.'
}
[IO.File]::WriteAllText((Join-Path $evidence 'manifest.json'), ($manifest | ConvertTo-Json -Depth 8), $utf8)
$summary | Format-Table Language, Mode, Workload, WarmStartupMedianMs, ShutdownMedianMs, AllocatedBytesMedian -AutoSize
Write-Host "Private evidence retained in $evidence. No performance budget or leak-free acceptance is implied."
