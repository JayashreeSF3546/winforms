# Fluent WinForms startup configuration

Status: implementation proposal; public API review remains required. This extends the Application
Builder prototype and does not change classic Application.Run applications. GitHub API issue
creation is pending authenticated access; this document is not evidence of API approval.

## Background and motivation

Applications currently call ApplicationConfiguration.Initialize or individual Application setters
before constructing their UI. Allow the same initialization through the builder, using the requested
Use-style names, without introducing another rendering, DPI, exception, or configuration subsystem.

## API proposal

Add these methods to Microsoft.Extensions.WinForms.WinFormsApplicationBuilder, each returning the
same builder:

```csharp
public WinFormsApplicationBuilder UseHighDpiMode(HighDpiMode highDpiMode);
public WinFormsApplicationBuilder UseColorMode(SystemColorMode colorMode);
public WinFormsApplicationBuilder UseTextRendering(bool useCompatibleTextRendering);
public WinFormsApplicationBuilder UseLegacyVisualStyles();
public WinFormsApplicationBuilder UseFormRevealMode(FormRevealMode formRevealMode);
public WinFormsApplicationBuilder UseVisualStylesMode(VisualStylesMode visualStylesMode);
public WinFormsApplicationBuilder UseDefaultFont(Font font);
public WinFormsApplicationBuilder UseUnhandledExceptionMode(UnhandledExceptionMode mode);
public WinFormsApplicationBuilder UseVisualStyleState(VisualStyleState visualStyleState);
```

| Builder selection | Existing Application API |
|---|---|
| UseHighDpiMode | SetHighDpiMode |
| UseColorMode | SetColorMode |
| UseTextRendering | SetCompatibleTextRenderingDefault |
| UseLegacyVisualStyles | EnableVisualStyles |
| UseFormRevealMode | SetDefaultFormRevealMode |
| UseVisualStylesMode | SetDefaultVisualStylesMode |
| UseDefaultFont | SetDefaultFont |
| UseUnhandledExceptionMode | SetUnhandledExceptionMode (current thread) |
| UseVisualStyleState | VisualStyleState setter |

Services, Logging, and the proposed registration helpers in the discussion's screenshot are separate
work. This change does not add them.

## Requirements and execution

- Calls validate arguments and record selections without calling Application setters or creating UI.
  Repeated selections of the same option use the last valid value before Build.
- An unconfigured builder retains its existing behavior and does not impose new startup defaults.
- Build applies explicitly selected settings on the owning STA, before building the optional host.
  Consequently host configuration, service factories, startup callbacks, and the hidden dispatcher
  observe the selected defaults.
- Startup configuration requires deferred UI selection. Reject an existing startup Form and reject
  configuration application after a native window was created on the current thread. Applications
  must configure before any other UI threads/windows or native initialization too; this is not a
  process-wide UI detector.
- Apply in a fixed order independent of fluent call order: DPI, OS visual-style enablement,
  compatible text rendering, default font, color mode, default visual-styles mode, Form reveal mode,
  visual-style state, then current-thread exception routing.
- A false result from Application.SetHighDpiMode is an explicit InvalidOperationException, even if a
  manifest or an earlier initialization call already established awareness. Do not silently report
  successful configuration. Do not combine UseHighDpiMode with an earlier initializer that sets DPI.
- Preserve existing Application validation/platform behavior. Inherit is valid for FormRevealMode
  but not for the application default VisualStylesMode. Exception mode is current-thread scoped,
  matching the single-argument Application.SetUnhandledExceptionMode overload.
- Application defaults are process/thread state, not per-builder isolated state. Build can change
  them even if Run is never called. There is no rollback or restoration on application disposal.
  Once application of settings starts, the builder is consumed even if a setter fails; retry with
  partial global mutations is not safe.
- Font ownership stays with the caller, matching Application.SetDefaultFont. The caller must keep
  it valid while it remains the process default; the builder/application must not dispose it.
- UseLegacyVisualStyles enables OS visual styles; it does not select old WinForms rendering or
  disable already-enabled styles. UseVisualStylesMode selects the separate WinForms default mode.
- Do not invoke generated ApplicationConfiguration.Initialize from framework code. Prefer either
  the generated initializer or explicit builder configuration. If combined deliberately, existing
  setter restrictions and irreversible initialization still apply.

## Usage

```csharp
[STAThread]
static void Main()
{
    using WinFormsApplication app = WinFormsApplication.CreateBuilder()
        .UseStartupForm<MainForm>()
        .UseHighDpiMode(HighDpiMode.PerMonitorV2)
        .UseColorMode(SystemColorMode.System)
        .UseTextRendering(false)
        .UseLegacyVisualStyles()
        .UseFormRevealMode(FormRevealMode.Deferred)
        .UseVisualStylesMode(VisualStylesMode.Net11)
        .Build();
    app.Run();
}
```

Additional selections are UseDefaultFont, UseUnhandledExceptionMode, and UseVisualStyleState.
Ordinary Designer construction and InitializeComponent remain service/configuration-free.

The runnable C# sample is in
`src\test\integration\ApplicationBuilderStartupConfiguration\CSharp`. Its `Program.cs` demonstrates
all nine selections, and the sample README contains Visual Studio and command-line launch steps.

## Alternatives and risks

Retaining only the existing static calls remains supported. Applying settings immediately in each
fluent call would make call order observable; applying during Run would be too late for host Build
callbacks and pre-created UI. Automatically merging generated initializer defaults is rejected
because that code belongs to the application, not the framework.

Global state cannot be transactional. DPI manifests, prior initialization, other UI threads, and
the write-once visual-styles mode can prevent changes. Callers must surface Build failures.
UseTextRendering retains the requested name but means compatible text rendering, not a new text
renderer. UseLegacyVisualStyles naming and the strict DPI-failure policy need API review.

## UI impact and acceptance

These settings affect scaling, default fonts, themes, reveal behavior, and exception routing.
No new control properties or Designer serialization are introduced. Existing accessibility/high
contrast behavior remains owned by Application and the controls; configuration does not certify
visual or accessibility correctness.

Focused tests must cover all fluent methods' validation/ownership/freeze rules, no eager mutation,
last selection wins, host-Build ordering, deferred UI, existing Form/handle rejection, DPI failure,
and irreversible failed application. Process-global effects must be tested in isolated processes.
Run the targeted Arcade build and new tests, plus existing builder/host tests. Manual multi-monitor
DPI, high contrast, theme, Designer, and remote CI acceptance remain separate work.
