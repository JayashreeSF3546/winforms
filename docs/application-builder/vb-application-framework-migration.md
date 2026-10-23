# Migrating from the Visual Basic Application Framework

The Visual Basic Application Framework remains supported. A migration is
opt-in: existing `MyApplication`, `ApplicationEvents.vb`, generated startup
forms, `My.Settings`, and `WindowsFormsApplicationBase` behavior continue to
work until the application chooses a new entry point. The shared
`WinFormsApplicationBuilder` APIs are available to both Visual Basic and C#;
they do not wrap or run a second legacy application model.

The paired runnable samples are under
[`samples/Migration/VisualBasic`](samples/Migration/VisualBasic) and
[`samples/Migration/CSharp`](samples/Migration/CSharp). They keep the sample
forms simple and demonstrate lifetime notifications, a splash screen,
single-instance activation, and forwarded arguments. The C# sample uses a
language-neutral equivalent of the shared API; it does not claim to reproduce
VB-specific `My.*` behavior.

## Existing `ApplicationEvents.vb` pattern

The [VB project template's `ApplicationEvents.vb`](../../pkg/Microsoft.Dotnet.WinForms.ProjectTemplates/content/WinFormsApplication-VisualBasic/ApplicationEvents.vb)
adds handlers to the generated partial `MyApplication` class. A project might
add startup logic like this while the Visual Basic Application Framework owns
the entry point and message loop:

```vb
Imports Microsoft.VisualBasic.ApplicationServices

Namespace My
    Partial Friend Class MyApplication
        Private Sub MyApplication_Startup(
            sender As Object,
            e As StartupEventArgs) Handles Me.Startup
            ' Existing startup logic.
        End Sub
    End Class
End Namespace
```

The new VB sample moves that startup work to the shared application lifecycle
and keeps `MainForm` as a normal WinForms form. It also demonstrates how the
same builder can opt into activation forwarding and splash-screen behavior.
The existing generated event class remains supported for projects that have
not migrated.

## Choose an incremental migration

1. **Keep the current application running while preparing the change.** Move
   reusable startup, shutdown, and activation work into methods or services
   that can be called by either entry point. Keep designer-generated Forms and
   controls; the builder does not require replacing them.
2. **Migrate one behavior at a time.** Replace a legacy startup handler with
   `ApplicationStarting` when its work belongs before startup UI activation.
   Move work that requires a running UI to the form's normal lifecycle. Use
   `ApplicationStarting`'s `Cancel` property for pre-start cancellation.
3. **Switch lifecycle ownership once.** When opting into the builder, select a
   manual `Sub Main`/startup object for the VB project as needed and let the
   builder own the message loop. Do not call `WindowsFormsApplicationBase.Run`
   and `WinFormsApplication.Run` for the same application.
4. **Add single-instance and splash behavior explicitly.** Configure
   `UseSingleInstance(...)` and `UseSplashScreen(...)` only when the migrated
   application needs them. Use `ActivationRequested` to handle forwarded
   arguments on the primary application's UI thread.
5. **Review compatibility-dependent features separately.** The builder does
   not automatically save `My.Settings`, translate every
   `UnhandledException` policy, or replace `My.Application` and
   `My.Computer` services. Use the shared settings and logging services only
   after deciding how their explicit behavior should map to the application.

## Map the legacy events and features

| Legacy VB surface | Shared builder surface | Migration note |
|---|---|---|
| `MyApplication.Startup` | `ApplicationStarting` | Runs before startup UI activation and builder-owned host startup. Set `Cancel` to skip startup; cleanup still completes. |
| `MyApplication.Shutdown` | `ApplicationStopping` and/or `ApplicationStopped` | These are notifications, not exact aliases. `ApplicationStopping` starts before host and message-loop shutdown; `ApplicationStopped` runs during final cleanup after shutdown coordination. |
| `MyApplication.StartupNextInstance` | `ActivationRequested` | Receives an immutable argument snapshot on the primary UI thread. `BringToForeground` controls the runtime's best-effort restore/activate request. |
| `IsSingleInstance` | `UseSingleInstance()` or `UseSingleInstance(applicationId)` | The shared identity is scoped to the current user and Windows session. An explicit ID is a product identity, not an authorization boundary. |
| `SplashScreen` and `MinimumSplashScreenDisplayTime` | `UseSplashScreen<TSplashScreen>(minimumDisplayDuration)` | Uses a shared form factory and `TimeSpan`; cancellation avoids showing the splash, and the runtime disposes it as startup completes or fails. |
| `My.Settings` and `SaveMySettingsOnExit` | Optional `AddUserSettings()` | This is a separate service and storage contract. It does not import, save, or reinterpret legacy settings automatically. |
| `UnhandledException` | Application-specific WinForms/CLR handling or opt-in exception logging | Logging does not translate the legacy event's exit/recovery decision. Review the exception policy rather than mapping it mechanically. |

The legacy `Startup` and `Shutdown` event names do not establish identical
ordering with builder notifications. The shared lifetime ordering and error
semantics are specified in the
[VB/C# application contracts](vb-application-framework-contracts.md).
Single-instance identity, transport, and failure boundaries are described in
the [single-instance architecture](single-instance-architecture.md).

## Run the paired sample

Build and run each project from the repository root. Run one process at a time
when comparing the languages; the sample IDs are intentionally distinct.

```powershell
dotnet build docs\application-builder\samples\Migration\VisualBasic\WinFormsApplicationBuilder.Migration.VisualBasic.vbproj
dotnet build docs\application-builder\samples\Migration\CSharp\WinFormsApplicationBuilder.Migration.CSharp.csproj

dotnet run --project docs\application-builder\samples\Migration\VisualBasic\WinFormsApplicationBuilder.Migration.VisualBasic.vbproj -- first.txt
dotnet run --project docs\application-builder\samples\Migration\CSharp\WinFormsApplicationBuilder.Migration.CSharp.csproj -- first.txt
```

For each language, leave the first application open and launch the same
project again with a different argument, such as `second.txt`. The existing
window should display the forwarded activation arguments; the second launch
should return without opening another startup form. Close the primary window
and inspect the debugger output for `ApplicationStopping` and
`ApplicationStopped`. The splash is configured with a short nonzero minimum
duration for demonstration; the sample does not assert wall-clock timing.

To adapt startup cancellation, use the shared event rather than retaining a
second VB startup loop:

```vb
AddHandler application.Lifetime.ApplicationStarting,
    Sub(sender, eventArgs)
        If ShouldCancelStartup() Then
            eventArgs.Cancel = True
        End If
    End Sub
```

The C# form is the same `CancelEventArgs` contract:

```csharp
application.Lifetime.ApplicationStarting += (_, eventArgs) =>
{
    if (ShouldCancelStartup())
    {
        eventArgs.Cancel = true;
    }
};
```

This sample is a migration starting point, not an automatic adapter for
generated VB projects. Preserve the existing framework when its event timing,
settings persistence, exception policy, or designer integration is required.

## Decisions and follow-up

- The samples use the shared builder and one message-loop owner. Wrapping
  `WindowsFormsApplicationBase` around `WinFormsApplication` was rejected
  because it would duplicate lifecycle and exception ownership.
- Legacy settings and exception decisions remain explicit migration work;
  neither is silently translated by the sample.
- The samples depend on the shared contracts and runtime from #14974 through
  #14977. Multi-user, multi-session, Terminal Services, and RDP validation
  remains follow-up work for #14980.
