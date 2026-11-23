# Fluent startup configuration sample

This C# sample uses the screenshot's `CreateBuilder`, fluent startup selection, `Build`, and `Run`
layout. `CSharp\Program.cs` demonstrates all nine implemented startup configuration methods.
The APIs are part of this checkout's Application Builder prototype, not an installed framework.

The main form displays the effective DPI, color, text-rendering, font, and visual-style defaults.
Exception routing is labeled as a configured selection because Application has no public getter
for it. System color mode follows Windows; High Contrast can override the requested appearance.

## Run in Visual Studio

1. Open `Winforms.sln` using `.\start-vs.cmd`.
2. Under **tests > integration**, set **ApplicationBuilderStartupConfiguration.CSharp** as the startup project.
3. Select the **Startup configuration** launch profile and press **F5** or **Ctrl+F5**.
4. Inspect the displayed defaults, then select **Close** or close the window.

## Build and run from PowerShell

From the repository root:

```powershell
.\build.cmd -projects src\test\integration\ApplicationBuilderStartupConfiguration\CSharp\ApplicationBuilderStartupConfiguration.CSharp.csproj -verbosity minimal
$env:DOTNET_ROOT = Join-Path $PWD '.dotnet'
& .\artifacts\bin\ApplicationBuilderStartupConfiguration.CSharp\Debug\net12.0\ApplicationBuilderStartupConfiguration.CSharp.exe
```

The output framework directory follows the repository's `NetCurrent` value.

## Startup rules and screenshot differences

- There is no separate `ApplicationConfiguration.Initialize()` or static Application initialization.
  Build applies the selected defaults, with DPI before the default font, before creating the form.
- `UseStartupForm<MainForm>()` defers form construction. Do not replace it with a pre-created form.
- The font returned by `SystemFonts.DefaultFont` is intentionally kept alive as a process default
  until the sample exits. The builder does not own or dispose it. A custom font likewise remains
  caller-owned and must stay valid while it is the process default.
- Use `UseTextRendering(false)` for `Application.SetCompatibleTextRenderingDefault(false)`.
  The screenshot's `UseTextRenderingV2()` is not an implemented API.
- The screenshot's `builder.Services`, `builder.Logging`, `AddWinFormsUserSettingsService`,
  `AddWinFormsExceptionService`, and `UseWinFormsLogging` helpers are not part of this feature.
  This sample deliberately does not invent local shims or expand the public API to mimic those lines.
- A manifest or another initializer that has already established DPI awareness can make Build fail.
  Do not combine this sample's `UseHighDpiMode` call with earlier DPI initialization.
