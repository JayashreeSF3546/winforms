# Licensed to the .NET Foundation under one or more agreements.
# The .NET Foundation licenses this file to you under the MIT license.

<#
.SYNOPSIS
Runs the bounded local #14980 selectors and retains private, machine-specific evidence.
.DESCRIPTION
Build first through build.cmd. This script never provisions users/sessions, changes machine
configuration, contacts another machine, or retries uncertain IPC deliveries.
#>
[CmdletBinding()]
param(
    [string] $ArtifactsDir = (Join-Path $env:TEMP 'WinForms14978Artifacts'),
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',
    [ValidateSet('implicit-x64', 'x64', 'x86', 'arm64')]
    [string] $Platform = 'implicit-x64',
    [string] $EvidenceDirectory = (Join-Path $env:TEMP ("WinForms14980Evidence-" + [guid]::NewGuid().ToString('N')))
)

$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\..'))
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if ($evidence.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    $evidence -eq $repo -or (Test-Path $evidence)) {
    throw 'Evidence must be a new, private directory outside the checkout.'
}

$bin = Join-Path $ArtifactsDir 'bin\System.Windows.Forms.UI.IntegrationTests'
if ($Platform -ne 'implicit-x64') { $bin = Join-Path $bin $Platform }
$bin = Join-Path $bin "$Configuration\net12.0-windows7.0"
$exe = Join-Path $bin 'System.Windows.Forms.UI.IntegrationTests.exe'
if (!(Test-Path $exe)) { throw "Build the affected graph first: $exe" }
[void][IO.Directory]::CreateDirectory($evidence)
$utf8 = [Text.UTF8Encoding]::new($false)
$rows = [Collections.Generic.List[object]]::new()
$payloads = [Collections.Generic.List[object]]::new()
$observations = [Collections.Generic.List[object]]::new()

foreach ($language in 'CSharp', 'VisualBasic') {
    $worker = Join-Path $bin "Parity\ApplicationBuilderParity.$language"
    foreach ($name in "ApplicationBuilderParity.$language.exe", "ApplicationBuilderParity.$language.dll",
        'System.Windows.Forms.dll', 'Microsoft.VisualBasic.Forms.dll') {
        $file = Join-Path $worker $name
        $machine = $null
        if ($name.EndsWith('.exe')) {
            $stream = [IO.File]::OpenRead($file)
            $reader = [IO.BinaryReader]::new($stream)
            try {
                $stream.Position = 0x3c
                $header = $reader.ReadInt32()
                $stream.Position = $header + 4
                $machine = '{0:X4}' -f $reader.ReadUInt16()
            }
            finally { $reader.Dispose() }
            $expected = switch ($Platform) { 'x86' { '014C' } 'arm64' { 'AA64' } default { '8664' } }
            if ($machine -ne $expected) { throw "Apphost architecture mismatch: $language $machine != $expected" }
        }

        $payloads.Add([ordered]@{
            Language = $language; File = $name; PEMachine = $machine
            SHA256 = (Get-FileHash $file -Algorithm SHA256).Hash
        })
    }

    Copy-Item (Join-Path $worker "ApplicationBuilderParity.$language.runtimeconfig.json") `
        (Join-Path $evidence "$language.runtimeconfig.json")
}

$selections = @(
    @{ Name = 'Environment'; Count = 8; Arguments = @('--filter-method', '*NativeEnvironment_*') },
    @{ Name = 'NativeIPCRegression'; Count = 8; Arguments = @(
        '--filter-method', '*SameExecutable_ReportsActualDeliveryOutcome',
        '--filter-method', '*DifferentExecutablePaths_AreSeparatePrimaries') },
    @{ Name = 'MigrationRegression'; Count = 6; Arguments = @(
        '--filter-class', 'System.Windows.Forms.UITests.WinFormsApplicationMigrationTests') }
)

foreach ($selection in $selections) {
    $resultDir = Join-Path $evidence $selection.Name
    $info = [Diagnostics.ProcessStartInfo]::new($exe)
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.WorkingDirectory = $repo
    foreach ($argument in ($selection.Arguments + @('--parallel', 'none', '--report-trx',
        '--results-directory', $resultDir, '--show-live-output', 'on'))) {
        $info.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::Start($info)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $timedOut = $false
    try {
        if (!$process.WaitForExit(360000)) {
            $timedOut = $true
            $process.Kill($true) # Only this invocation's test process and its descendants.
            if (!$process.WaitForExit(10000)) { throw 'Owned test tree did not terminate.' }
        }

        $text = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
        [IO.File]::WriteAllText((Join-Path $evidence "$($selection.Name).log"), $text, $utf8)
        Write-Host "$($selection.Name): exit $($process.ExitCode); raw output retained in $evidence"
        $trx = @(Get-ChildItem $resultDir -Filter '*.trx' -Recurse -ErrorAction SilentlyContinue)
        if ($timedOut -or $trx.Count -ne 1) {
            $rows.Add([ordered]@{ Case = $selection.Name; Status = 'Failed'; Reason = 'Timeout or missing/ambiguous TRX' })
            continue
        }

        [xml] $document = [IO.File]::ReadAllText($trx[0].FullName)
        $results = @($document.SelectNodes("//*[local-name()='UnitTestResult']"))
        foreach ($result in $results) {
            $status = switch ($result.outcome) { 'Passed' { 'Passed' } 'NotExecuted' { 'NotRun' } default { 'Failed' } }
            $rows.Add([ordered]@{
                Case = $result.testName; Status = $status; Outcome = $result.outcome
                Evidence = [IO.Path]::GetRelativePath($evidence, $trx[0].FullName)
            })
        }

        if ($results.Count -ne $selection.Count -or $process.ExitCode -ne 0) {
            $rows.Add([ordered]@{ Case = "$($selection.Name)-runner"; Status = 'Failed'; Reason = "Exit=$($process.ExitCode); tests=$($results.Count)" })
        }
    }
    finally {
        $watch.Stop()
        $observations.Add([ordered]@{
            Selection = $selection.Name; DiagnosticWallSeconds = $watch.Elapsed.TotalSeconds
            TimedOut = $timedOut
        })
        Write-Host "$($selection.Name): diagnostic wall time $($watch.Elapsed), not a benchmark."
        if (!$process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        $process.Dispose()
    }
}

foreach ($case in 'DifferentWindowsUsers', 'SameUserDifferentWindowsSessions', 'TerminalServices-RDP',
    'NativeNonAppleWindowsARM64', 'SurfaceGo2SlowHardware', 'ElevationBoundary', 'RemoteCI') {
    $rows.Add([ordered]@{
        Case = $case; Status = 'NotRun'
        Reason = 'Requires separately authorized environment/operator qualification; not established by this local runner.'
    })
}

$sources = @(
    'src/System.Windows.Forms/Microsoft/Extensions/WinForms/WinFormsSingleInstance.cs',
    'src/test/integration/ApplicationBuilderParity/CSharp/Program.cs',
    'src/test/integration/ApplicationBuilderParity/CSharp/Probes.cs',
    'src/test/integration/ApplicationBuilderParity/VisualBasic/Program.vb',
    'src/test/integration/UIIntegrationTests/WinFormsApplicationParityTests.cs',
    'src/test/integration/UIIntegrationTests/WinFormsApplicationParityTests.Environment.cs',
    'src/test/unit/System.Windows.Forms/Microsoft/Extensions/WinForms/WinFormsSingleInstanceTests.cs',
    'src/test/integration/ApplicationBuilderParity/Invoke-EnvironmentValidation.ps1'
) | ForEach-Object { [ordered]@{ File = $_; SHA256 = (Get-FileHash (Join-Path $repo $_)).Hash } }
$report = [ordered]@{
    SchemaVersion = 1; Issue = 14980; UTC = [DateTime]::UtcNow.ToString('O')
    Revision = (& git -C $repo rev-parse HEAD); Dirty = [bool](& git -C $repo status --porcelain)
    Configuration = $Configuration; Platform = $Platform
    SDK = (Get-Content (Join-Path $repo 'global.json') -Raw | ConvertFrom-Json).sdk.version
    Sources = @($sources); Payloads = $payloads.ToArray(); Rows = $rows.ToArray()
    Observations = $observations.ToArray()
    Note = 'Local raw evidence can contain account paths; do not commit or upload without redaction. Worker traces carry actual MVID/runtime/OS/remote-session facts.'
}
[IO.File]::WriteAllText((Join-Path $evidence 'matrix.json'), ($report | ConvertTo-Json -Depth 10), $utf8)
Write-Host "Private evidence: $evidence"
if (@($rows | Where-Object Status -eq 'Failed').Count) { throw 'Local validation failed; retain evidence, do not retry until diagnosed.' }
if (@($rows | Where-Object Status -ne 'Passed').Count) {
    Write-Warning 'Local run recorded; #14980 acceptance remains incomplete. Inspect NotRun/Blocked rows.'
}
