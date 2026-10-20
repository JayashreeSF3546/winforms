param(
    [string] $DotNetPath = "dotnet",
    [string] $RepoRoot = (Join-Path $PSScriptRoot "..\..\.."),
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"

if ($env:OS -ne "Windows_NT") {
    throw "WinForms project templates can only be benchmarked on Windows."
}

$RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path
$templateRoot = Join-Path $RepoRoot "pkg\Microsoft.Dotnet.WinForms.ProjectTemplates\content"
$nuGetConfig = Join-Path $RepoRoot "NuGet.config"
$frameworkProject = Join-Path $RepoRoot "src\System.Windows.Forms\System.Windows.Forms.csproj"
$workRoot = Join-Path ([IO.Path]::GetTempPath()) ("winforms-template-benchmark-" + [Guid]::NewGuid().ToString("N"))
$cliHome = Join-Path $workRoot "cli-home"
$generatedRoot = Join-Path $workRoot "generated"
$templatesInstalled = $false
$previousDotNetCliHome = $env:DOTNET_CLI_HOME
$previousSkipFirstTimeExperience = $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE
$previousTelemetryOptOut = $env:DOTNET_CLI_TELEMETRY_OPTOUT
$previousNuGetPackages = $env:NUGET_PACKAGES

if ($DotNetPath -match "[\\/]") {
    $DotNetPath = [IO.Path]::GetFullPath($DotNetPath)
    if (-not (Test-Path -LiteralPath $DotNetPath -PathType Leaf)) {
        throw "The .NET CLI was not found at '$DotNetPath'."
    }
}

function Invoke-DotNet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $Arguments,

        [Parameter(Mandatory = $true)]
        [string] $Description
    )

    Write-Host "==> $Description"
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $output = & $DotNetPath @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    $stopwatch.Stop()

    if ($exitCode -ne 0) {
        foreach ($line in $output) {
            Write-Host $line
        }

        throw "'$DotNetPath $($Arguments -join ' ')' failed with exit code $exitCode."
    }

    return [pscustomobject] @{
        ElapsedSeconds = $stopwatch.Elapsed.TotalSeconds
        Output = ($output -join [Environment]::NewLine)
    }
}

function Add-ProductFrameworkReferences {
    param(
        [Parameter(Mandatory = $true)]
        [string] $ProjectPath,

        [Parameter(Mandatory = $true)]
        [string] $TargetFramework
    )

    $referencePaths = @(
        (Join-Path $RepoRoot "artifacts\obj\System.Windows.Forms\$Configuration\$TargetFramework\ref\System.Windows.Forms.dll"),
        (Join-Path $RepoRoot "artifacts\obj\System.Windows.Forms.Primitives\$Configuration\$TargetFramework\ref\System.Windows.Forms.Primitives.dll"),
        (Join-Path $RepoRoot "artifacts\obj\System.Private.Windows.Core\$Configuration\$TargetFramework\ref\System.Private.Windows.Core.dll")
    )

    [xml] $project = Get-Content -LiteralPath $ProjectPath -Raw
    $itemGroup = $project.CreateElement("ItemGroup")

    foreach ($referencePath in $referencePaths) {
        if (-not (Test-Path -LiteralPath $referencePath -PathType Leaf)) {
            throw "A product reference assembly required for Application Builder template benchmarking was not found: '$referencePath'."
        }

        $assemblyName = [Reflection.AssemblyName]::GetAssemblyName($referencePath)
        $reference = $project.CreateElement("Reference")
        $reference.SetAttribute("Include", $assemblyName.FullName)

        $hintPath = $project.CreateElement("HintPath")
        $hintPath.InnerText = $referencePath
        [void] $reference.AppendChild($hintPath)

        $private = $project.CreateElement("Private")
        $private.InnerText = "false"
        [void] $reference.AppendChild($private)
        [void] $itemGroup.AppendChild($reference)
    }

    [void] $project.DocumentElement.AppendChild($itemGroup)
    $project.Save($ProjectPath)
}

function Get-PublishedSizeBytes {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Directory
    )

    $files = @(Get-ChildItem -LiteralPath $Directory -Recurse -File -Force)
    if ($files.Count -eq 0) {
        throw "Publish output '$Directory' contains no files."
    }

    return [long](($files | Measure-Object -Property Length -Sum).Sum)
}

$templateCases = @(
    @{
        ShortName = "winforms"; Language = "C#"; Folder = "WinFormsApplication-CSharp"; Name = "TemplatePerfWinFormsCSharp"
        FrameworkOption = $true; IsApplication = $true; UsesApplicationBuilder = $false
    },
    @{
        ShortName = "winforms"; Language = "VB"; Folder = "WinFormsApplication-VisualBasic"; Name = "TemplatePerfWinFormsVB"
        FrameworkOption = $true; IsApplication = $true; UsesApplicationBuilder = $false
    },
    @{
        ShortName = "winforms-builder"; Language = "C#"; Folder = "WinFormsApplicationBuilder-CSharp"; Name = "TemplatePerfBuilderCSharp"
        FrameworkOption = $false; IsApplication = $true; UsesApplicationBuilder = $true
    },
    @{
        ShortName = "winforms-builder"; Language = "VB"; Folder = "WinFormsApplicationBuilder-VisualBasic"; Name = "TemplatePerfBuilderVB"
        FrameworkOption = $false; IsApplication = $true; UsesApplicationBuilder = $true
    },
    @{
        ShortName = "winforms-blazor"; Language = "C#"; Folder = "WinFormsBlazorHybrid-CSharp"; Name = "TemplatePerfBlazorHybrid"
        FrameworkOption = $false; IsApplication = $true; UsesApplicationBuilder = $true
    },
    @{
        ShortName = "winformslib"; Language = "C#"; Folder = "WinFormsLibrary-CSharp"; Name = "TemplatePerfLibraryCSharp"
        FrameworkOption = $true; IsApplication = $false; UsesApplicationBuilder = $false
    },
    @{
        ShortName = "winformslib"; Language = "VB"; Folder = "WinFormsLibrary-VisualBasic"; Name = "TemplatePerfLibraryVB"
        FrameworkOption = $true; IsApplication = $false; UsesApplicationBuilder = $false
    },
    @{
        ShortName = "winformscontrollib"; Language = "C#"; Folder = "WinFormsControlLibrary-CSharp"; Name = "TemplatePerfControlLibraryCSharp"
        FrameworkOption = $true; IsApplication = $false; UsesApplicationBuilder = $false
    },
    @{
        ShortName = "winformscontrollib"; Language = "VB"; Folder = "WinFormsControlLibrary-VisualBasic"; Name = "TemplatePerfControlLibraryVB"
        FrameworkOption = $true; IsApplication = $false; UsesApplicationBuilder = $false
    }
)

New-Item -ItemType Directory -Path $workRoot -Force | Out-Null
New-Item -ItemType Directory -Path $generatedRoot -Force | Out-Null
$env:DOTNET_CLI_HOME = $cliHome
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
if ([string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES)) {
    $env:NUGET_PACKAGES = Join-Path $env:USERPROFILE ".nuget\packages"
}

$resultRows = [System.Collections.Generic.List[object]]::new()
Push-Location $RepoRoot

try {
    $frameworkOutput = Invoke-DotNet `
        -Arguments @("msbuild", $frameworkProject, "-getProperty:NetCurrent") `
        -Description "Read the repository target framework"
    $targetFramework = $frameworkOutput.Output.Trim()
    if ([string]::IsNullOrEmpty($targetFramework)) {
        throw "Could not determine NetCurrent from '$frameworkProject'."
    }

    [void] (Invoke-DotNet `
        -Arguments @("new", "install", $templateRoot) `
        -Description "Install WinForms templates into an isolated CLI home")
    $templatesInstalled = $true

    $sdkVersion = (& $DotNetPath --version | Out-String).Trim()
    Write-Host "SDK: $sdkVersion"
    Write-Host "OS: $([Runtime.InteropServices.RuntimeInformation]::OSDescription)"
    Write-Host "Architecture: $([Runtime.InteropServices.RuntimeInformation]::OSArchitecture)"
    Write-Host "Package cache: warm/shared; timings are diagnostic, not CI thresholds."
    Write-Host ""

    foreach ($templateCase in $templateCases) {
        $outputDirectory = Join-Path $generatedRoot $templateCase.Name
        New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

        $generationArguments = @(
            "new", $templateCase.ShortName,
            "--language", $templateCase.Language,
            "--name", $templateCase.Name,
            "--output", $outputDirectory,
            $(if ($templateCase.UsesApplicationBuilder) { "--skipRestore" } else { "--no-restore" })
        )

        if ($templateCase.FrameworkOption) {
            $generationArguments += @("--framework", $targetFramework)
        }

        [void] (Invoke-DotNet `
            -Arguments $generationArguments `
            -Description "Generate $($templateCase.Language) $($templateCase.ShortName)")

        $projectExtension = if ($templateCase.Language -eq "VB") { ".vbproj" } else { ".csproj" }
        $projectPath = Join-Path $outputDirectory ($templateCase.Name + $projectExtension)
        if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
            throw "Template '$($templateCase.ShortName)' did not generate '$projectPath'."
        }

        if ($templateCase.UsesApplicationBuilder) {
            Add-ProductFrameworkReferences -ProjectPath $projectPath -TargetFramework $targetFramework
        }

        $restore = Invoke-DotNet `
            -Arguments @("restore", $projectPath, "--configfile", $nuGetConfig) `
            -Description "Restore $($templateCase.Name)"
        $build = Invoke-DotNet `
            -Arguments @("build", $projectPath, "--no-restore", "--configuration", $Configuration) `
            -Description "Build $($templateCase.Name)"

        if ($templateCase.IsApplication) {
            foreach ($deployment in @(
                    @{ Name = "FDD"; SelfContained = "false" },
                    @{ Name = "SCD"; SelfContained = "true" }
                )) {
                $ridRestore = Invoke-DotNet `
                    -Arguments @(
                        "restore", $projectPath,
                        "--runtime", "win-x64",
                        "--configfile", $nuGetConfig,
                        "-p:SelfContained=$($deployment.SelfContained)"
                    ) `
                    -Description "Restore $($deployment.Name) assets for $($templateCase.Name)"

                $publishDirectory = Join-Path $workRoot ($templateCase.Name + "-" + $deployment.Name)
                [void] (Invoke-DotNet `
                    -Arguments @("clean", $projectPath, "--configuration", $Configuration) `
                    -Description "Clean before $($deployment.Name) publish of $($templateCase.Name)")
                $publish = Invoke-DotNet `
                    -Arguments @(
                        "publish", $projectPath,
                        "--no-restore",
                        "--configuration", $Configuration,
                        "--runtime", "win-x64",
                        "--self-contained", $deployment.SelfContained,
                        "--output", $publishDirectory
                    ) `
                    -Description "Publish $($deployment.Name) $($templateCase.Name)"

                $resultRows.Add([pscustomobject] @{
                    Template = $templateCase.ShortName
                    Language = $templateCase.Language
                    Deployment = $deployment.Name
                    RestoreSeconds = [Math]::Round($restore.ElapsedSeconds, 2)
                    RIDRestoreSeconds = [Math]::Round($ridRestore.ElapsedSeconds, 2)
                    BuildSeconds = [Math]::Round($build.ElapsedSeconds, 2)
                    PublishSeconds = [Math]::Round($publish.ElapsedSeconds, 2)
                    OutputMiB = [Math]::Round((Get-PublishedSizeBytes $publishDirectory) / 1MB, 2)
                })
            }
        }
        else {
            $publishDirectory = Join-Path $workRoot ($templateCase.Name + "-library")
            [void] (Invoke-DotNet `
                -Arguments @("clean", $projectPath, "--configuration", $Configuration) `
                -Description "Clean before library publish of $($templateCase.Name)")
            $publish = Invoke-DotNet `
                -Arguments @(
                    "publish", $projectPath,
                    "--no-restore",
                    "--configuration", $Configuration,
                    "--output", $publishDirectory
                ) `
                -Description "Publish $($templateCase.Name)"

            $resultRows.Add([pscustomobject] @{
                Template = $templateCase.ShortName
                Language = $templateCase.Language
                Deployment = "Library"
                RestoreSeconds = [Math]::Round($restore.ElapsedSeconds, 2)
                RIDRestoreSeconds = $null
                BuildSeconds = [Math]::Round($build.ElapsedSeconds, 2)
                PublishSeconds = [Math]::Round($publish.ElapsedSeconds, 2)
                OutputMiB = [Math]::Round((Get-PublishedSizeBytes $publishDirectory) / 1MB, 2)
            })
        }
    }

    Write-Host ""
    Write-Host "Template build and deployment measurements (seconds, MiB):"
    $resultRows |
        Select-Object Template, Language, Deployment, RestoreSeconds, RIDRestoreSeconds, BuildSeconds, PublishSeconds, OutputMiB |
        Format-Table -AutoSize
}
finally {
    if ($templatesInstalled) {
        try {
            [void] (Invoke-DotNet `
                -Arguments @("new", "uninstall", $templateRoot) `
                -Description "Uninstall WinForms templates from the isolated CLI home")
        }
        catch {
            Write-Warning "Could not automatically uninstall the benchmark templates: $_"
        }
    }

    Pop-Location
    $env:DOTNET_CLI_HOME = $previousDotNetCliHome
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = $previousSkipFirstTimeExperience
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = $previousTelemetryOptOut
    $env:NUGET_PACKAGES = $previousNuGetPackages

    if (Test-Path -LiteralPath $workRoot) {
        Remove-Item -LiteralPath $workRoot -Recurse -Force
    }
}
