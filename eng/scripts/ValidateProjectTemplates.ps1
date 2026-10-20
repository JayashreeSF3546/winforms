param(
    [string] $DotNetPath = "dotnet",
    [string] $RepoRoot = (Join-Path $PSScriptRoot "..\.."),
    [string] $Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

if ($env:OS -ne "Windows_NT") {
    throw "WinForms project templates can only be validated on Windows."
}

$RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path
$templateRoot = Join-Path $RepoRoot "pkg\Microsoft.Dotnet.WinForms.ProjectTemplates\content"
$nuGetConfig = Join-Path $RepoRoot "NuGet.config"
$workRoot = Join-Path ([IO.Path]::GetTempPath()) ("winforms-template-validation-" + [Guid]::NewGuid().ToString("N"))
$cliHome = Join-Path $workRoot "cli-home"
$generatedRoot = Join-Path $workRoot "generated"
$frameworkProject = Join-Path $RepoRoot "src\System.Windows.Forms\System.Windows.Forms.csproj"
$templatesInstalled = $false
$previousDotNetCliHome = $env:DOTNET_CLI_HOME
$previousSkipFirstTimeExperience = $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE
$previousTelemetryOptOut = $env:DOTNET_CLI_TELEMETRY_OPTOUT
$previousNuGetPackages = $env:NUGET_PACKAGES
$previousAppData = $env:APPDATA
$previousLocalAppData = $env:LOCALAPPDATA

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
    $output = & $DotNetPath @Arguments 2>&1
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0) {
        foreach ($line in $output) {
            Write-Host $line
        }

        $command = ($Arguments -join " ")
        throw "'$DotNetPath $command' failed with exit code $exitCode."
    }

    Write-Host "Succeeded: $Description"
    return $output
}

function Get-RelativePath {
    param(
        [Parameter(Mandatory = $true)]
        [string] $BasePath,

        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    $base = [IO.Path]::GetFullPath($BasePath).TrimEnd("\", "/") + "\"
    $fullPath = [IO.Path]::GetFullPath($Path)

    if (-not $fullPath.StartsWith($base, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path '$fullPath' is outside '$base'."
    }

    return $fullPath.Substring($base.Length).Replace("\", "/")
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
            throw "A product reference assembly required for Application Builder template validation was not found: '$referencePath'."
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

function Get-AllowedGeneratedFiles {
    param(
        [Parameter(Mandatory = $true)]
        [string] $TemplateDirectory,

        [Parameter(Mandatory = $true)]
        [object] $TemplateConfig,

        [Parameter(Mandatory = $true)]
        [string] $ProjectName
    )

    $allowedFiles = New-Object "System.Collections.Generic.HashSet[string]" ([StringComparer]::OrdinalIgnoreCase)

    foreach ($source in $TemplateConfig.sources) {
        $sourceDirectory = $TemplateDirectory
        if (-not [string]::IsNullOrEmpty($source.source)) {
            $sourceDirectory = [IO.Path]::GetFullPath((Join-Path $TemplateDirectory $source.source))
        }

        if (-not (Test-Path -LiteralPath $sourceDirectory -PathType Container)) {
            continue
        }

        foreach ($file in Get-ChildItem -LiteralPath $sourceDirectory -Recurse -File) {
            $relativePath = Get-RelativePath -BasePath $sourceDirectory -Path $file.FullName
            if ($relativePath.StartsWith(".template.config/", [StringComparison]::OrdinalIgnoreCase)) {
                continue
            }

            $generatedPath = $relativePath.Replace($TemplateConfig.sourceName, $ProjectName)
            [void] $allowedFiles.Add($generatedPath)
        }

        foreach ($modifier in $source.modifiers) {
            if ($null -ne $modifier.rename) {
                foreach ($rename in $modifier.rename.PSObject.Properties) {
                    $generatedPath = $rename.Value.Replace($TemplateConfig.sourceName, $ProjectName)
                    [void] $allowedFiles.Add($generatedPath)
                }
            }
        }
    }

    return ,$allowedFiles
}

function Test-GeneratedFiles {
    param(
        [Parameter(Mandatory = $true)]
        [hashtable] $TemplateCase,

        [Parameter(Mandatory = $true)]
        [string] $TemplateDirectory,

        [Parameter(Mandatory = $true)]
        [string] $OutputDirectory
    )

    $configPath = Join-Path $TemplateDirectory ".template.config\template.json"
    $templateConfig = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    $allowedFiles = Get-AllowedGeneratedFiles `
        -TemplateDirectory $TemplateDirectory `
        -TemplateConfig $templateConfig `
        -ProjectName $TemplateCase.Name

    $unexpectedFiles = @()
    foreach ($file in Get-ChildItem -LiteralPath $OutputDirectory -Recurse -File) {
        $relativePath = Get-RelativePath -BasePath $OutputDirectory -Path $file.FullName
        if (-not $allowedFiles.Contains($relativePath)) {
            $unexpectedFiles += $relativePath
        }
    }

    if ($unexpectedFiles.Count -gt 0) {
        throw "Template '$($TemplateCase.ShortName)' generated unexpected files: $($unexpectedFiles -join ', ')."
    }

    $projectExtension = if ($TemplateCase.Language -eq "VB") { ".vbproj" } else { ".csproj" }
    $projectPath = Join-Path $OutputDirectory ($TemplateCase.Name + $projectExtension)
    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
        throw "Template '$($TemplateCase.ShortName)' did not generate the expected project '$projectPath'."
    }

    foreach ($requiredFile in $TemplateCase.RequiredFiles) {
        $requiredPath = Join-Path $OutputDirectory $requiredFile
        if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
            throw "Template '$($TemplateCase.ShortName)' did not generate required file '$requiredFile'."
        }
    }

    $projectFiles = @(Get-ChildItem -LiteralPath $OutputDirectory -File | Where-Object { $_.Extension -in @(".csproj", ".vbproj") })
    if ($projectFiles.Count -ne 1) {
        throw "Template '$($TemplateCase.ShortName)' generated $($projectFiles.Count) project files; expected exactly one."
    }

    return $projectPath
}

function Test-ApplicationStarts {
    param(
        [Parameter(Mandatory = $true)]
        [hashtable] $TemplateCase,

        [Parameter(Mandatory = $true)]
        [string] $PublishDirectory
    )

    $applicationPath = Join-Path $PublishDirectory ($TemplateCase.Name + ".exe")
    if (-not (Test-Path -LiteralPath $applicationPath -PathType Leaf)) {
        throw "The published application host was not found: '$applicationPath'."
    }

    $standardOutput = Join-Path $workRoot ($TemplateCase.Name + ".stdout.txt")
    $standardError = Join-Path $workRoot ($TemplateCase.Name + ".stderr.txt")
    $process = $null

    $env:APPDATA = Join-Path $workRoot "user-data"
    $env:LOCALAPPDATA = Join-Path $workRoot "local-user-data"
    try {
        $process = Start-Process `
            -FilePath $applicationPath `
            -WorkingDirectory $PublishDirectory `
            -RedirectStandardOutput $standardOutput `
            -RedirectStandardError $standardError `
            -PassThru

        if ($process.WaitForExit(15000)) {
            $standardErrorText = if (Test-Path -LiteralPath $standardError) { Get-Content -LiteralPath $standardError -Raw } else { "" }
            throw "Application '$($TemplateCase.Name)' exited during startup with code $($process.ExitCode). $standardErrorText"
        }

        Write-Host "Application '$($TemplateCase.Name)' stayed running through the startup smoke-test window."
    }
    finally {
        if ($null -ne $process) {
            $process.Refresh()
            if (-not $process.HasExited) {
                Stop-Process -Id $process.Id -Force
                if (-not $process.WaitForExit(5000)) {
                    throw "Application process '$($TemplateCase.Name)' did not exit after the smoke test."
                }
            }
        }

        $env:APPDATA = $previousAppData
        $env:LOCALAPPDATA = $previousLocalAppData
    }
}

$templateCases = @(
    @{
        ShortName = "winforms"; Language = "C#"; Folder = "WinFormsApplication-CSharp"; Name = "TemplateWinFormsCSharp"
        FrameworkOption = $true; IsApplication = $true; Run = $true; UsesApplicationBuilder = $false
        RequiredFiles = @("Form1.cs", "Program.cs")
    },
    @{
        ShortName = "winforms"; Language = "VB"; Folder = "WinFormsApplication-VisualBasic"; Name = "TemplateWinFormsVB"
        FrameworkOption = $true; IsApplication = $true; Run = $true; UsesApplicationBuilder = $false
        RequiredFiles = @("Form1.vb", "Program.vb")
    },
    @{
        ShortName = "winforms-builder"; Language = "C#"; Folder = "WinFormsApplicationBuilder-CSharp"; Name = "TemplateBuilderCSharp"
        FrameworkOption = $false; IsApplication = $true; Run = $false; UsesApplicationBuilder = $true
        RequiredFiles = @("MainForm.cs", "Program.cs", "appsettings.json")
    },
    @{
        ShortName = "winforms-builder"; Language = "VB"; Folder = "WinFormsApplicationBuilder-VisualBasic"; Name = "TemplateBuilderVB"
        FrameworkOption = $false; IsApplication = $true; Run = $false; UsesApplicationBuilder = $true
        RequiredFiles = @("MainForm.vb", "Program.vb", "appsettings.json")
    },
    @{
        ShortName = "winforms-blazor"; Language = "C#"; Folder = "WinFormsBlazorHybrid-CSharp"; Name = "TemplateBlazorHybrid"
        FrameworkOption = $false; IsApplication = $true; Run = $false; UsesApplicationBuilder = $true
        RequiredFiles = @("MainForm.cs", "Program.cs", "App.razor", "wwwroot\index.html")
    },
    @{
        ShortName = "winformslib"; Language = "C#"; Folder = "WinFormsLibrary-CSharp"; Name = "TemplateLibraryCSharp"
        FrameworkOption = $true; IsApplication = $false; Run = $false; UsesApplicationBuilder = $false
        RequiredFiles = @("Class1.cs")
    },
    @{
        ShortName = "winformslib"; Language = "VB"; Folder = "WinFormsLibrary-VisualBasic"; Name = "TemplateLibraryVB"
        FrameworkOption = $true; IsApplication = $false; Run = $false; UsesApplicationBuilder = $false
        RequiredFiles = @("Class1.vb")
    },
    @{
        ShortName = "winformscontrollib"; Language = "C#"; Folder = "WinFormsControlLibrary-CSharp"; Name = "TemplateControlLibraryCSharp"
        FrameworkOption = $true; IsApplication = $false; Run = $false; UsesApplicationBuilder = $false
        RequiredFiles = @("UserControl1.cs")
    },
    @{
        ShortName = "winformscontrollib"; Language = "VB"; Folder = "WinFormsControlLibrary-VisualBasic"; Name = "TemplateControlLibraryVB"
        FrameworkOption = $true; IsApplication = $false; Run = $false; UsesApplicationBuilder = $false
        RequiredFiles = @("UserControl1.vb")
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
Push-Location $RepoRoot

try {
    $frameworkOutput = Invoke-DotNet `
        -Arguments @("msbuild", $frameworkProject, "-getProperty:NetCurrent") `
        -Description "Read the repository target framework"
    $targetFramework = ($frameworkOutput -join "").Trim()
    if ([string]::IsNullOrEmpty($targetFramework)) {
        throw "Could not determine NetCurrent from '$frameworkProject'."
    }

    [void] (Invoke-DotNet `
        -Arguments @("new", "install", $templateRoot) `
        -Description "Install all WinForms project templates into the isolated CLI home")
    $templatesInstalled = $true

    [void] (Invoke-DotNet `
        -Arguments @("new", "install", $templateRoot, "--force") `
        -Description "Reinstall the template package to validate template update handling")

    foreach ($templateCase in $templateCases) {
        $templateDirectory = Join-Path $templateRoot $templateCase.Folder
        $outputDirectory = Join-Path $generatedRoot $templateCase.Name
        New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

        $generationArguments = @(
            "new", $templateCase.ShortName,
            "--language", $templateCase.Language,
            "--name", $templateCase.Name,
            "--output", $outputDirectory
        )

        $restoreOption = if ($templateCase.UsesApplicationBuilder) { "--skipRestore" } else { "--no-restore" }
        $generationArguments += $restoreOption

        if ($templateCase.FrameworkOption) {
            $generationArguments += @("--framework", $targetFramework)
        }

        [void] (Invoke-DotNet `
            -Arguments $generationArguments `
            -Description "Generate $($templateCase.Language) $($templateCase.ShortName) template")

        $projectPath = Test-GeneratedFiles `
            -TemplateCase $templateCase `
            -TemplateDirectory $templateDirectory `
            -OutputDirectory $outputDirectory

        if ($templateCase.UsesApplicationBuilder) {
            Add-ProductFrameworkReferences -ProjectPath $projectPath -TargetFramework $targetFramework
        }

        [void] (Invoke-DotNet `
            -Arguments @("restore", $projectPath, "--configfile", $nuGetConfig) `
            -Description "Restore $($templateCase.Name)")
        [void] (Invoke-DotNet `
            -Arguments @("build", $projectPath, "--no-restore", "--configuration", $Configuration) `
            -Description "Build $($templateCase.Name)")

        $publishDirectory = Join-Path $workRoot ($templateCase.Name + "-framework-dependent")
        if ($templateCase.IsApplication) {
            [void] (Invoke-DotNet `
                -Arguments @("restore", $projectPath, "--runtime", "win-x64", "--configfile", $nuGetConfig, "-p:SelfContained=false") `
                -Description "Restore framework-dependent assets for $($templateCase.Name)")
            [void] (Invoke-DotNet `
                -Arguments @("publish", $projectPath, "--no-restore", "--configuration", $Configuration, "--runtime", "win-x64", "--self-contained", "false", "--output", $publishDirectory) `
                -Description "Publish framework-dependent $($templateCase.Name)")

            $selfContainedDirectory = Join-Path $workRoot ($templateCase.Name + "-self-contained")
            [void] (Invoke-DotNet `
                -Arguments @("restore", $projectPath, "--runtime", "win-x64", "--configfile", $nuGetConfig, "-p:SelfContained=true") `
                -Description "Restore self-contained assets for $($templateCase.Name)")
            [void] (Invoke-DotNet `
                -Arguments @("publish", $projectPath, "--no-restore", "--configuration", $Configuration, "--runtime", "win-x64", "--self-contained", "true", "--output", $selfContainedDirectory) `
                -Description "Publish self-contained $($templateCase.Name)")

            if ($templateCase.Run) {
                Test-ApplicationStarts -TemplateCase $templateCase -PublishDirectory $publishDirectory
            }
        }
        else {
            [void] (Invoke-DotNet `
                -Arguments @("publish", $projectPath, "--no-restore", "--configuration", $Configuration, "--output", $publishDirectory) `
                -Description "Publish $($templateCase.Name)")
        }

        Write-Host "Validated template: $($templateCase.Language) $($templateCase.ShortName)"
    }

    [void] (Invoke-DotNet `
        -Arguments @("new", "uninstall", $templateRoot) `
        -Description "Uninstall templates from the isolated CLI home")

    $templatesInstalled = $false
    Write-Host "All nine WinForms project templates passed generation, output, restore, build, and publish validation."
}
finally {
    if ($templatesInstalled) {
        try {
            [void] (Invoke-DotNet `
                -Arguments @("new", "uninstall", $templateRoot) `
                -Description "Clean up the isolated template installation")
        }
        catch {
            Write-Warning "Could not automatically uninstall the test templates: $_"
        }
    }

    Pop-Location
    $env:DOTNET_CLI_HOME = $previousDotNetCliHome
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = $previousSkipFirstTimeExperience
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = $previousTelemetryOptOut
    $env:NUGET_PACKAGES = $previousNuGetPackages
    $env:APPDATA = $previousAppData
    $env:LOCALAPPDATA = $previousLocalAppData

    if (Test-Path -LiteralPath $workRoot) {
        Remove-Item -LiteralPath $workRoot -Recurse -Force
    }
}
