# WinForms Application Builder sample

The C# sample keeps the builder setup close to the screenshot: settings and exception service
registration, Debug logging, fluent startup configuration, then Build and Run. These APIs are
prototypes from this checkout and are not present in an installed WinForms runtime.

The main form displays the effective DPI, color, text-rendering, font, and visual-style defaults.
Exception routing is labeled as a configured selection because Application has no public getter.
System color mode follows Windows; High Contrast can override the requested appearance.

The sample registers a source-generated JSON user settings service, but the form does not resolve
or use it, so no settings file is created by running the sample.

The exception service has an explicit UI-thread policy and a separate logging-failure fallback; this
sample requests message-loop exit for a UI exception and prints only exception types to stderr.
`CatchException` is selected so the UI-thread policy can run.

`UseHost(new HostBuilder())` is required because the service registrations, scoped form activation,
and exception integration use the selected Generic Host.

## Run in Visual Studio

The sample targets .NET 11.0, as selected by the repository's `global.json`. Ensure the .NET 11.0
SDK and runtime are installed before building and running it.

1. Open `Winforms.sln` using `.\start-vs.cmd`.
2. Under **tests > integration**, set **ApplicationBuilderStartupConfiguration.CSharp** as the startup project.
3. Select the **Startup configuration** launch profile and press **F5** or **Ctrl+F5**.
4. Inspect the displayed defaults, then select **Close** or close the window.

## Build and run from PowerShell

From the repository root:

```powershell
.\build.cmd -projects src\test\integration\ApplicationBuilderStartupConfiguration\CSharp\ApplicationBuilderStartupConfiguration.CSharp.csproj -verbosity minimal
$env:DOTNET_ROOT = Join-Path $PWD '.dotnet'
& .\artifacts\bin\ApplicationBuilderStartupConfiguration.CSharp\Debug\net11.0-windows7.0\ApplicationBuilderStartupConfiguration.CSharp.exe
```

The output framework directory follows the repository's `NetCurrent` value. Keep the matching
repository WinForms binaries beside the executable: the installed framework does not contain these
prototype builder APIs. `SystemFonts.DefaultFont` remains caller-owned and is kept alive for the
process default. Do not combine `UseHighDpiMode` with an earlier initializer that already established
DPI awareness.
