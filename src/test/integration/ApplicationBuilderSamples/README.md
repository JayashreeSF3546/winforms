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

## Infrastructure services and configuration

The C# and Visual Basic infrastructure samples demonstrate the Generic Host
configuration pipeline, validated options and reload, Development User Secrets,
per-user JSON settings with schema migration, a hosted status worker,
health checks, structured logging, and opt-in exception logging.

### Build and run

From the repository root, build and run each project separately:

```powershell
$env:DOTNET_ROOT = "$PWD\.dotnet"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"

.\build.cmd -projects src\test\integration\ApplicationBuilderSamples\CSharp\Infrastructure\Infrastructure.CSharp.csproj
.\build.cmd -projects src\test\integration\ApplicationBuilderSamples\VisualBasic\Infrastructure\Infrastructure.VisualBasic.vbproj

dotnet run --no-build --project src\test\integration\ApplicationBuilderSamples\CSharp\Infrastructure\Infrastructure.CSharp.csproj
dotnet run --no-build --project src\test\integration\ApplicationBuilderSamples\VisualBasic\Infrastructure\Infrastructure.VisualBasic.vbproj
```

Run one application at a time. In Visual Studio, open `Winforms.sln`, select
`Infrastructure.CSharp` or `Infrastructure.VisualBasic` as the startup project,
and press F5 to view structured log entries in **View > Output > Debug**.

### Configuration, settings, and health-check steps

Repeat these steps for each language:

1. Start the application. Confirm the environment and default display name are
   shown, then observe the background status log in the Debug output.
2. To exercise Development User Secrets, set the environment to Development
   before launching, then configure a non-sensitive marker:

   ```powershell
   $env:DOTNET_ENVIRONMENT = "Development"
   dotnet user-secrets set "Sample:DiagnosticToken" "local-marker" --project src\test\integration\ApplicationBuilderSamples\CSharp\Infrastructure\Infrastructure.CSharp.csproj
   ```

   Use the Visual Basic project path when testing that sample. Restart the
   application and verify that **Development secret configured** changes to
   `True`; the secret value itself is never displayed or logged. Remove the
   marker afterward with `dotnet user-secrets remove "Sample:DiagnosticToken"`
   for the same project.
3. With the application running, change `Sample:DisplayName` in that project's
   `appsettings.json`, save the file, and verify the updated name appears in
   the background worker log. Restore the original value after testing.
   Configuration validation also rejects an empty display name or a refresh
   interval outside 2–60 seconds; restart after introducing an invalid value
   and confirm startup reports the validation failure rather than opening the
   main form. Restore valid settings before continuing.
4. Change the selected theme and startup-tips checkbox, then choose **Save user
   settings**. Confirm the settings label refreshes and a structured
   `SettingsSaved` event appears. The settings file is stored under
   `%LOCALAPPDATA%\WinForms\WinFormsApplicationBuilder.InfrastructureSample\user-settings.json`
   for C#, or
   `%LOCALAPPDATA%\WinForms\WinFormsApplicationBuilder.InfrastructureSample.VisualBasic\user-settings.json`
   for Visual Basic. Close and restart the app to verify the preferences
   persist for that Windows user.
5. Click **Run health checks**. Confirm the `user-settings` check reports
   Healthy, including the current schema version when a settings file exists,
   and check the corresponding structured log entry. Select each **Simulated
   dependency** state (Healthy, Degraded, and Unhealthy) and rerun the check;
   confirm the aggregate health label and structured log reflect each state.
6. To exercise schema migration, back up the settings file and replace its
   contents with a version-one document containing
   `{"schemaVersion":1,"settings":{"themeName":"Dark"}}`.
   Restart the application, run the health check, and confirm the stored schema
   version is now 2 and the selected theme is Dark. Restore or delete the test
   settings file when finished.
7. Click **Log sample exception**. Confirm an Error log entry identifies
   `WinForms.ThreadException` and includes the sample exception, then verify
   the window remains responsive. This deliberately exercises the opt-in
   exception logger; it is not an application error-recovery pattern.
8. Close the application normally. Confirm the background worker logs
   `WorkerStopped` and the host exits without leaving the process running.

The samples' `appsettings.Development.json` changes only the display name;
`appsettings.json` provides the default name and worker interval. User Secrets
are used only in Development and are not checked into the repository.

### Published and environment deployment steps

Repeat the following procedures for both languages. These publish commands
create representative framework-dependent and self-contained folder
deployments. The ZIP files are portable published-output archives, not MSIX
installers or signed packages.

```powershell
dotnet publish src\test\integration\ApplicationBuilderSamples\CSharp\Infrastructure\Infrastructure.CSharp.csproj -c Release -r win-x64 --self-contained false -o artifacts\publish\Infrastructure.CSharp.fdd
Compress-Archive -Path artifacts\publish\Infrastructure.CSharp.fdd\* -DestinationPath artifacts\publish\Infrastructure.CSharp.fdd.zip -Force
dotnet publish src\test\integration\ApplicationBuilderSamples\CSharp\Infrastructure\Infrastructure.CSharp.csproj -c Release -r win-x64 --self-contained true -o artifacts\publish\Infrastructure.CSharp.scd
Compress-Archive -Path artifacts\publish\Infrastructure.CSharp.scd\* -DestinationPath artifacts\publish\Infrastructure.CSharp.scd.zip -Force

dotnet publish src\test\integration\ApplicationBuilderSamples\VisualBasic\Infrastructure\Infrastructure.VisualBasic.vbproj -c Release -r win-x64 --self-contained false -o artifacts\publish\Infrastructure.VisualBasic.fdd
Compress-Archive -Path artifacts\publish\Infrastructure.VisualBasic.fdd\* -DestinationPath artifacts\publish\Infrastructure.VisualBasic.fdd.zip -Force
dotnet publish src\test\integration\ApplicationBuilderSamples\VisualBasic\Infrastructure\Infrastructure.VisualBasic.vbproj -c Release -r win-x64 --self-contained true -o artifacts\publish\Infrastructure.VisualBasic.scd
Compress-Archive -Path artifacts\publish\Infrastructure.VisualBasic.scd\* -DestinationPath artifacts\publish\Infrastructure.VisualBasic.scd.zip -Force
```

1. **Published output:** Extract each archive to a fresh directory and run
   `Infrastructure.CSharp.exe` or `Infrastructure.VisualBasic.exe`. Confirm
   the default configuration loads, settings can be saved, health checks run,
   the sample exception is logged, and normal shutdown completes. The
   framework-dependent deployment requires the matching .NET runtime on the
   machine; the self-contained deployment does not.
2. **Read-only install directory:** Install or copy a published output under
   a location where a standard user has read/execute but not write access
   (for example, an administrator-installed directory under `%ProgramFiles%`).
   Run as that standard user, save a preference, restart, and confirm it
   persists under that user's `%LOCALAPPDATA%\WinForms\...` settings path.
   Verify the app still reads its published `appsettings.json`; do not expect
   live reload when the configuration directory is not writable.
3. **Multiple Windows users:** Sign in as two separate standard users and run
   the same published application for each. Save a different theme or
   startup-tips value under each account, then confirm each account sees its
   own preferences and that neither account can overwrite the other's
   `%LOCALAPPDATA%` settings.
4. **Terminal Services / Remote Desktop:** Connect to a Windows host through
   Remote Desktop, run the published app in that interactive session, and
   complete the configuration, health-state, exception, settings-save, and
   graceful-shutdown steps. Record session type, OS, architecture, and whether
   reconnect/disconnect affects the app.
5. **Offline operation:** Publish and extract the self-contained deployment
   before disconnecting the network. With network access disabled, launch the
   app and repeat settings save/load, health checks, exception logging, and
   graceful shutdown. The sample has no runtime network service dependency;
   package restore and publishing must happen before going offline.

Record OS/version, runtime and process architecture, deployment type, user
account type, session type, whether the network was disconnected, and the
observed result for each language. Do not treat build or publish success as a
runtime validation result.

### Current validation status

The C# and Visual Basic infrastructure projects have built successfully. The
infrastructure UI scenarios and the published/environment deployment
procedures above are **Not run**. The DI-scope samples' local smoke runs
exercised startup, modeless activation, modal Cancel/Continue navigation, and
orderly close in both languages; their Designer round trips, theme behavior,
Terminal Services, Windows on ARM, and slow-hardware scenarios remain
**Not run**.
