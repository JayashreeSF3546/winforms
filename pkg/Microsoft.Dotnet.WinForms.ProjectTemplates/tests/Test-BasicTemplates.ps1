#Requires -Version 7.0
param(
    [Parameter(Mandatory = $true)]
    [string] $TemplatePackage,
    [string] $DotNet,
    [string] $OutputRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = [IO.Path]::GetFullPath([IO.Path]::Combine($PSScriptRoot, '..\..\..'))
if (-not $DotNet) {
    $DotNet = Join-Path $repository '.dotnet\dotnet.exe'
}
if (-not $OutputRoot) {
    $OutputRoot = Join-Path $repository 'artifacts\tmp\BasicTemplateTests'
}
$TemplatePackage = (Resolve-Path -LiteralPath $TemplatePackage).Path
$output = [IO.Directory]::CreateDirectory((Join-Path $OutputRoot ([Guid]::NewGuid().ToString('N')))).FullName
$hiveArguments = @('--debug:custom-hive', (Join-Path $output 'hive'))

function Invoke-DotNet {
    param([string[]] $Arguments)
    & $DotNet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Assert-Condition {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) {
        throw $Message
    }
}

# Verify the packed artifact, including preservation of all existing template payloads.
$archive = [IO.Compression.ZipFile]::OpenRead($TemplatePackage)
try {
    $nuspec = @($archive.Entries | Where-Object FullName -Like '*.nuspec')
    Assert-Condition ($nuspec.Count -eq 1) 'Expected one package manifest.'
    $reader = [IO.StreamReader]::new($nuspec[0].Open())
    try {
        [xml] $manifest = $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
    }
    Assert-Condition ($manifest.SelectNodes("//*[local-name()='packageType' and @name='Template']").Count -eq 1) `
        'The package must advertise the Template package type.'
    Assert-Condition (@($archive.Entries | Where-Object FullName -Like 'lib/*').Count -eq 0) `
        'A template package must not contain compiled library output.'

    $content = Join-Path $PSScriptRoot '..\content'
    $classicFolders = @(Get-ChildItem $content -Directory | Where-Object Name -NotLike 'WinFormsApplicationBuilder-*')
    foreach ($folder in $classicFolders) {
        foreach ($file in Get-ChildItem $folder.FullName -Recurse -File) {
            $relative = $folder.Name + '/' + [IO.Path]::GetRelativePath($folder.FullName, $file.FullName).Replace('\', '/')
            $entries = @($archive.Entries | Where-Object { $_.FullName.EndsWith($relative, [StringComparison]::Ordinal) })
            Assert-Condition ($entries.Count -eq 1) "Missing or duplicate classic payload: $relative"
            $stream = $entries[0].Open()
            $hash = [Security.Cryptography.SHA256]::Create()
            try {
                $packedHash = [BitConverter]::ToString($hash.ComputeHash($stream)).Replace('-', '')
            }
            finally {
                $hash.Dispose()
                $stream.Dispose()
            }
            Assert-Condition ($packedHash -eq (Get-FileHash $file.FullName -Algorithm SHA256).Hash) `
                "Classic payload changed during packaging: $relative"
        }
    }
}
finally {
    $archive.Dispose()
}

$installed = $false
try {
    Invoke-DotNet -Arguments (@('new', 'install', $TemplatePackage) + $hiveArguments)
    $installed = $true
    $identities = [Collections.Generic.HashSet[string]]::new()
    foreach ($case in @(
        @{ Language = 'C#'; Extension = 'cs'; Name = 'BasicCSharp' },
        @{ Language = 'VB'; Extension = 'vb'; Name = 'BasicVisualBasic' },
        @{ Language = 'C#'; Extension = 'cs'; Name = 'CSharp App.1' },
        @{ Language = 'VB'; Extension = 'vb'; Name = 'VB App.1' }
    )) {
        $destination = Join-Path $output $case.Name
        Invoke-DotNet -Arguments (@('new', 'winforms-builder', '--language', $case.Language,
            '--name', $case.Name, '--output', $destination, '--no-restore', '--no-update-check') + $hiveArguments)
        $extension = $case.Extension
        $expected = @("$($case.Name).${extension}proj", "Program.$extension", "Form1.$extension",
            "Form1.Designer.$extension", "ApplicationOptions.$extension", "UserPreferences.$extension",
            'README.md', 'appsettings.json')
        if ($case.Language -eq 'C#') {
            $expected += 'PreferencesJsonContext.cs'
        }
        $actual = @(Get-ChildItem $destination -Recurse -File | ForEach-Object {
            [IO.Path]::GetRelativePath($destination, $_.FullName)
        })
        Assert-Condition (@(Compare-Object $expected $actual).Count -eq 0) `
            "Unexpected generated files for $($case.Name): $($actual -join ', ')"
        [xml] $project = Get-Content (Join-Path $destination "$($case.Name).${extension}proj") -Raw
        Assert-Condition ($project.SelectSingleNode('/Project/PropertyGroup/TargetFramework').InnerText -eq 'net12.0-windows') `
            'The builder template must not imply support for older frameworks.'
        Assert-Condition ($project.SelectNodes('//ProjectReference | //Import[@Project]').Count -eq 0) `
            'Generated projects must not embed repository references or build imports.'
        $hosting = $project.SelectSingleNode("//PackageReference[@Include='Microsoft.Extensions.WinForms.Hosting']")
        Assert-Condition ($hosting.Version -eq '12.0.0-dev') 'Expected the explicitly pinned prototype hosting version.'
        $configuration = Get-Content (Join-Path $destination 'appsettings.json') -Raw | ConvertFrom-Json
        Assert-Condition (-not [string]::IsNullOrWhiteSpace($configuration.Application.Title)) 'Missing application title.'
        $program = Get-Content (Join-Path $destination "Program.$extension") -Raw
        $identity = [regex]::Match($program, 'WinFormsApp\.[0-9a-fA-F-]{36}').Value
        Assert-Condition ($identity.Length -gt 0 -and $identities.Add($identity)) `
            'Each generated application must have a distinct, stable settings-ID literal.'
        if ($case.Language -eq 'VB') {
            Assert-Condition ($project.SelectSingleNode('/Project/PropertyGroup/StartupObject').InnerText -eq 'Sub Main') `
                'VB must use explicit Main startup, independently of the host.'
        }
        foreach ($file in Get-ChildItem $destination -Recurse -File) {
            Assert-Condition (-not ([IO.File]::ReadAllText($file.FullName).Contains('Company.WinFormsBuilderApp1'))) `
                "Unexpanded source-name token in $($file.Name)."
        }
        Write-Output "PASS creation and manifest: $($case.Language), $($case.Name)"
    }

    $rejected = & $DotNet new winforms-builder --framework net10.0 --no-restore `
        --output (Join-Path $output 'unsupported') @hiveArguments 2>&1
    Assert-Condition ($LASTEXITCODE -ne 0) "Unsupported framework unexpectedly accepted: $rejected"
    Write-Output 'PASS unsupported framework rejection'
}
finally {
    if ($installed) {
        Invoke-DotNet -Arguments (@('new', 'uninstall', 'Microsoft.Dotnet.WinForms.ProjectTemplates') + $hiveArguments)
    }
}
Write-Output "PASS basic-template checks. Generated projects retained at $output"
