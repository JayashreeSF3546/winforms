# Application Builder integration samples

## Dependency-injection scopes and Designer-created controls

The C# and Visual Basic projects cover constructor-injected startup and
navigated Forms, Designer-created `UserControl` and component instances,
independent modal and modeless Form scopes, navigation and cancellation, and
scope/component disposal diagnostics.

The sample implementation is present in both languages. Designer round trips
and the environment matrix below still require manual validation; successful
builds or local smoke runs do not establish those results.

### Build and run

Run these commands from the repository root. The repository-local runtime is
needed when the system does not have the matching preview runtime installed:

```powershell
$env:DOTNET_ROOT = "$PWD\.dotnet"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"

.\build.cmd -projects src\test\integration\ApplicationBuilderSamples\CSharp\DiScopes\DiScopes.CSharp.csproj
.\build.cmd -projects src\test\integration\ApplicationBuilderSamples\VisualBasic\DiScopes\DiScopes.VisualBasic.vbproj

dotnet run --no-build --project src\test\integration\ApplicationBuilderSamples\CSharp\DiScopes\DiScopes.CSharp.csproj
dotnet run --no-build --project src\test\integration\ApplicationBuilderSamples\VisualBasic\DiScopes\DiScopes.VisualBasic.vbproj
```

Run each `dotnet run` command separately and close that application before
starting the other. Alternatively, open `Winforms.sln` in Visual Studio, set
the corresponding `DiScopes.CSharp` or `DiScopes.VisualBasic` project as the
startup project, and press F5. F5 makes the `Debug.WriteLine` scope and
shutdown diagnostics visible in **View > Output > Debug**.

### Functional scope and navigation steps

Repeat these steps for both projects:

1. Start the application. Record the startup scope ID shown in the main Form.
   Verify the embedded UserControl says it uses the same Form scope.
2. Select **Open modeless Form**. Verify the Details Form has a different
   scope ID and that its parent ID matches the startup Form. Close Details and
   check the Debug output for that scope's disposal.
3. Select **Review navigation**. Verify the modal dialog has its own scope ID
   and the startup Form's ID as its parent.
4. Select **Cancel**. Verify the dialog closes, the startup Form remains, and
   no Details Form is opened. Repeat the review and select **Continue**;
   verify that a Details Form opens with a separate scope and the startup
   scope as its parent.
5. Close the Forms. Check Debug output for scoped-service creation/disposal,
   the Designer component's disposal state, and the hosted service observing
   host shutdown. Note any service that is not disposed or any unhandled
   exception.

### Visual Studio Designer round trip

Repeat this procedure for the C# and Visual Basic projects on Windows with
the WinForms Designer installed:

1. Open `Winforms.sln`, build the desired `DiScopes` project, and open
   `DemoForms.cs` (C#) or `DemoForms.vb` (Visual Basic).
2. Use Visual Studio's **View Designer** command for `MainForm`. Confirm the
   designer shows the status label, embedded `SessionSummaryControl`, hosted
   service description, and the three navigation buttons.
3. Open the Designer for `DetailsForm`, `ReviewDialog`, and
   `SessionSummaryControl`. Confirm each designer-created control is present
   and the forms' button/text/layout properties match their source.
4. Make a harmless designer property change, such as changing a label's
   location. Save, close the designer, and reopen it. Confirm the change
   persists and no controls/components are duplicated or lost.
5. Build and run the project again, verify the navigation and scope steps
   above, then undo the temporary layout change and save.

Record any unavailable **View Designer** command, designer load error, save
failure, or generated-code change that breaks the build as a failed round trip.

### Theme and environment runs

For every row in the matrix, run both language projects and complete the
functional scope/navigation steps above. Start a fresh process after changing
the Windows theme. On Windows 11 use **Settings > Personalization > Colors >
Choose your mode** for Light or Dark; on Windows 10 use **Settings >
Personalization > Colors > Choose your default app mode**. Test Classic only
where the target Windows version provides that theme; record it as unavailable
rather than substituting High Contrast.

| Scenario | Step-by-step setup and run | Result |
|---|---|---|
| Local desktop, Classic | On a Windows version with a Classic theme, select it; build both projects; launch each separately; complete the functional and designer checks. | Not run |
| Local desktop, Light Mode | Select Light Mode; build both projects; launch each separately; complete the functional and designer checks. | Not run |
| Local desktop, Dark Mode | Select Dark Mode; build both projects; launch each separately; inspect text, buttons, form backgrounds, and layout; complete the functional and designer checks. | Not run |
| Terminal Services / Remote Desktop | Connect to the Windows test machine through Remote Desktop; verify the session is interactive; build/run both projects in that session; repeat the available theme checks and functional steps; note reconnect/disconnect behavior and session type. | Not run |
| Windows on ARM | On native Windows ARM64, confirm `dotnet --info` reports ARM64; build both projects on that machine; run each using the native .NET runtime; complete functional and available theme checks. | Not run |
| Slow or constrained hardware | On the target device or documented constrained VM, cold-launch each project; complete all navigation steps while observing UI responsiveness; close the app and note shutdown time and any hangs. Repeat once to distinguish cold-start effects. | Not run |

For each run, record date, OS/version, session type, process/runtime
architecture, theme, hardware (including VM limits if applicable), project
language, and observed result. Do not infer theme, architecture, or remote
session coverage from a build or a run performed in another environment.

### Current validation status

Both projects have built successfully, and local interactive smoke runs have
exercised startup, modeless activation, modal Cancel/Continue navigation, and
orderly close in C# and Visual Basic. The current smoke runs did not validate
Visual Studio Designer round trips, theme behavior, Terminal Services,
Windows on ARM, or slow hardware; those remain **Not run** in the matrix.
