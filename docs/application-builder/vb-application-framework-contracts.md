# Shared Visual Basic and C# application-feature contracts

**Scope:** issue [#14974](https://github.com/dotnet/winforms/issues/14974), following the capability inventory in [#14973](https://github.com/dotnet/winforms/issues/14973).

**Decision:** Visual Basic and C# applications use the same `Microsoft.Extensions.WinForms` contracts and Generic Host infrastructure. Keep `WindowsFormsApplicationBase` as the supported compatibility model; do not add a second VB-only builder, host, or lifecycle system. This document defines the mapping and migration boundaries; the shared lifecycle, single-instance, splash-screen, and application-service APIs are implemented by their respective child issues.

## Contract layers

| Layer | Shared contract | Responsibility |
|---|---|---|
| Composition | `WinFormsApplicationBuilder` | Select a startup form or `ApplicationContext`; optionally configure or accept a Generic Host; register services, logging, configuration, and opt-in WinForms services. |
| Runtime | `WinFormsApplication` | Own the single `Run()` operation on the caller's WinForms thread, coordinate the message loop with the optional host, and release application resources. |
| Application lifecycle | `WinFormsApplicationLifetime` | Publish one-shot `ApplicationStarting`, `ApplicationStarted`, `ApplicationStopping`, and `ApplicationStopped` notifications. Only `ApplicationStarting` is cancellable; it is not a separate host lifecycle. |
| Host lifecycle | `IHost`, `IHostApplicationLifetime`, hosted services | Reuse Generic Host startup, stopping, cancellation tokens, and service ownership. Do not duplicate host tokens or hosted-service abstractions in a VB namespace. |
| Optional services | `AddApplicationServices()`, `AddUserSettings()`, `EnableExceptionLogging()`, and registered services | Opt into shared application/runtime information and network availability, user settings, or exception logging. These are not implicitly enabled by choosing VB or C#. |

The public builder contract already supports both languages. `CreateBuilder()` retains the hostless path unless a host-building surface is used. `CreateBuilder(args)` configures a builder-owned Generic Host; `UseHost(IHost)` selects an already-built host instead. Those modes cannot be combined. `WinFormsApplication.CommandLineArgs` exposes a read-only snapshot of raw arguments; use `UseHost(host, args)` when supplying an already-built host. Startup selection is deferred when a form factory is used; a supplied form or context retains the creating-thread requirement. `Build()` creates the application object; `Run()` is single-use and executes on the calling WinForms thread.

Applications continue to own framework-generated initialization such as `ApplicationConfiguration.Initialize()` or the equivalent setup used by their project template. This must happen before controls create native handles. The builder does not replace, reorder, or infer application-specific DPI, visual-style, font, or color-mode configuration.

## Mapping the confirmed VB capabilities

| Existing VB capability | Shared contract or migration boundary | Compatibility decision |
|---|---|---|
| Generated main form and application context | `UseStartupForm<TForm>()`, a startup-form factory/instance, or `UseApplicationContext(...)`. | The shared startup-selection API is the preferred new-app path. Preserve designer-generated `OnCreateMainForm` in existing applications. Do not run the legacy application model and the builder as competing owners of the same message loop. |
| `ApplyApplicationDefaults` | Application-owned WinForms initialization before the builder runtime starts. | No direct builder event replacement is introduced here. Retain legacy defaults in `WindowsFormsApplicationBase`; a future shared configuration contract must preserve the before-handle-creation constraint. |
| `Startup` and `StartupEventArgs.Cancel` | `ApplicationStarting` runs before startup UI activation or builder-owned host startup and accepts `CancelEventArgs`. Setting `Cancel` skips UI activation, host startup, and message-loop entry; `ApplicationStarted` remains the successful-start notification. | Cancellation is terminal and cleanup still raises `ApplicationStopping` and `ApplicationStopped`. It cannot undo startup work already performed by the caller; the application still stops/disposes a supplied host according to `UseHost` ownership. |
| `Shutdown` | `ApplicationStopping` signals that terminal shutdown coordination is beginning; `ApplicationStopped` signals that loop/host shutdown has completed. | Neither event is an exact replacement for the legacy post-loop `Shutdown` event. The legacy event remains supported by the legacy runtime. |
| `ShutdownStyle` and main-form-close policy | Supply a custom `ApplicationContext` with `UseApplicationContext(...)` when its behavior is compatible with builder ownership. | There is no direct builder option equivalent to `AfterMainFormCloses` or `AfterAllFormsClose`. Do not silently impose legacy close policy on C# or new VB applications. |
| `CommandLineArgs` | `WinFormsApplication.CommandLineArgs` provides a read-only snapshot. `CreateBuilder(args)` also passes the arguments to Generic Host configuration; `UseHost(host, args)` supplies them when using an existing host. | The builder does not parse or rewrite raw arguments. Forwarded activation arguments remain deferred to #14976. |
| `UnhandledException` and `ExitApplication` | `EnableExceptionLogging()` is an opt-in logging integration when a Generic Host is configured. Applications may also use the established WinForms/CLR exception mechanisms directly. | Logging is not event handling or recovery. Do not claim the builder logger replaces the VB event, its exit decision, or debugger behavior. |
| `IsSingleInstance`, `StartupNextInstance`, and argument forwarding | `UseSingleInstance(...)` scopes identity by application, major/minor version, user, and session; `ActivationRequested` carries read-only forwarded arguments on the UI thread. | The shared event is not a VB `StartupNextInstance` alias. Identity policy, foreground handling, transport limits, and security boundaries are specified in the [single-instance architecture](single-instance-architecture.md); environmental multi-session validation remains #14980. |
| `SplashScreen` and minimum display time | `UseSplashScreen<TSplashScreen>()` or `UseSplashScreen(factory, minimumDisplayDuration)`. | The shared runtime displays the form on the application UI thread, waits asynchronously for the configured minimum duration while the message loop pumps, then disposes it before showing the startup form. Startup cancellation avoids constructing the splash; shutdown and failure paths dispose it. This does not change legacy VB behavior. |
| `SaveMySettingsOnExit` and `My.Settings` | `AddUserSettings()` opts into the builder's independent user-settings service. The existing `MigrateFromLegacySettingsAsync(ApplicationSettingsBase)` API is an explicit migration path. | Do not enable settings, import values, change storage format, or save legacy settings automatically. This is not drop-in `My.Settings` parity. |
| `My.Application.Info`, `Log`, `Culture`, and `My.Computer` services | `AddApplicationServices()` registers `IWinFormsApplicationInfo`, `IWinFormsRuntimeInfo`, and `IWinFormsNetworkAvailabilityService`. | Application/runtime information and network availability have language-neutral builder services. Logging, culture, and the remaining `My.*` conveniences retain their existing boundaries; existing VB APIs remain supported. |
| `ApplicationEvents.vb` and `MyApplication` event wiring | Keep existing generated partial classes and `Handles Me.<Event>` handlers for legacy projects. New code can subscribe to the shared runtime lifetime events from either language. | Do not require project-system regeneration or automatic event translation as a condition for builder use. Incremental examples belong to #14979. |

### Splash and application-service usage

Register the default services before building the application. The service-collection extension is available when the application already owns a Generic Host; the builder method is the fluent path when the builder creates the host.

```csharp
WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder()
    .AddApplicationServices()
    .UseSplashScreen<SplashForm>()
    .UseStartupForm<MainForm>();
```

The default splash minimum display duration is two seconds. Pass a different non-negative `TimeSpan` to `UseSplashScreen<TSplashScreen>(minimumDisplayDuration)` or to the factory overload. The splash factory runs on the UI thread after `ApplicationStarting` is accepted and the startup target has been created. Its form must be distinct from the startup form.

`IWinFormsApplicationInfo` provides entry-assembly name, version, metadata, application base directory, and a snapshot of loaded assemblies. The base directory is `AppContext.BaseDirectory`, which also works for single-file deployments; it is not guaranteed to match the legacy `AssemblyInfo.DirectoryPath`. `IWinFormsRuntimeInfo` provides runtime/OS descriptions, process architecture and path, process ID, working set, and stack trace.

`IWinFormsNetworkAvailabilityService` reports `IsAvailable` and raises `NetworkAvailabilityChanged` only when the availability value changes. The default implementation subscribes to operating-system address-change notifications only while it has listeners, captures the current synchronization context during application startup, marshals notifications through that context, and disconnects during application stopping. Applications that supply an already-built host can call `services.AddWinFormsApplicationServices()` before building it. A custom replacement service owns its own startup and shutdown behavior. The network service does not replace `My.Computer.Network.DownloadFile` or introduce a download API.

## Lifecycle, threading, cancellation, and errors

### Existing shared lifetime notifications

The runtime publishes the same `WinFormsApplicationLifetime` notifications to C# and VB:

1. `ApplicationStarting` is raised once on the application UI thread before startup UI activation or builder-owned host startup. Setting `Cancel` skips both and message-loop entry; cleanup still proceeds.
2. `ApplicationStarted` is raised once after startup UI initialization and successful host startup, when configured. Without a splash, hostless mode raises it before `Application.Run`; with a splash, the message loop pumps through the minimum display interval and the splash closes before the notification. With a host, the message loop pumps while host startup and any splash interval complete, and the startup form is shown after the started notification.
3. `ApplicationStopping` is raised once when terminal shutdown coordination begins, before the host is stopped and the WinForms loop is allowed to unwind.
4. `ApplicationStopped` is raised once during finalization after loop/host shutdown coordination, before `Run()` returns.

If runtime startup fails after lifecycle coordination begins, `ApplicationStarted` is omitted. Stop/finalization notifications and cleanup still run. Failures in `Build()` or `Run()` thread/single-use preconditions occur before lifecycle coordination and do not synthesize lifetime events. A missing startup selection is detected during `Run()` setup, before `ApplicationStarting`; stopping/stopped cleanup still runs. Repeated stop requests converge on the one-shot lifetime state. Generic Host lifetime tokens remain the host-facing source of truth; the WinForms events do not create a second token system.

These are synchronous .NET events. Runtime coordination raises them on the application lifecycle/UI thread; a host-originated stop from another thread is dispatched to that thread. Handlers should be short, non-blocking, and avoid synchronous waits for work that needs the UI thread. Generic Host service callbacks are not promised to run off the UI thread; do not block in their synchronous portions or depend on UI access from a hosted service.

`ApplicationStarting` is the shared pre-start cancellation hook; the other lifecycle events are notifications. Use standard host cancellation tokens for hosted work. A WinForms form-close veto is distinct from startup cancellation and host shutdown. A host-stop request is terminal once host stopping has begun; it is not rolled back into a running application because a form declines to close.

The host-backed startup order intentionally keeps the message loop pumping while `IHost.StartAsync` and any configured splash interval complete, so hosted startup callbacks can dispatch to the UI synchronization context without blocking the UI thread. `ApplicationStarted` follows successful host startup and splash cleanup and precedes showing the startup form. Hostless startup without a splash raises `ApplicationStarted` after UI initialization and before entering `Application.Run`; with a splash, it raises the event from the running message loop after the minimum display interval. These semantics are shared by C# and VB.

### Error semantics

- Builder validation and `Build()` failures are synchronous and do not start the message loop.
- An `ApplicationStarting` handler may cancel startup or throw. Cancellation skips host startup and UI activation but still runs stopping/stopped cleanup; a handler exception is propagated after cleanup.
- Startup-form/context creation and host-start failures prevent a successful `ApplicationStarted` notification. The runtime performs best-effort stop and cleanup, then surfaces the failure from `Run()`.
- Splash factory, display, and minimum-delay failures prevent `ApplicationStarted`; cleanup disposes the splash and follows the normal application-stopping path.
- A lifetime-handler exception is not silently ignored. Runtime cleanup continues, and the failure is surfaced after cleanup, combined with later failures when needed.
- UI-message exceptions continue to use WinForms' exception policy unless the application explicitly opts into `EnableExceptionLogging()` or installs its own handler. Enabling logging changes UI-thread exception handling; it is not a recovery event.
- Hosted-service exceptions and cancellation use Generic Host behavior. Do not translate them into VB `UnhandledExceptionEventArgs` automatically.
- The legacy `WindowsFormsApplicationBase` exception policy remains in force only when the legacy application model owns startup and its message loop.

## Language usage

The names, ownership rules, event order, and failure semantics are the same in both languages. Only event subscription and generic-method syntax differ. Keep application initialization in the application's existing generated entry point and perform it before the runtime creates or shows controls.

**C#:**

```csharp
WinFormsApplicationBuilder builder =
    WinFormsApplication.CreateBuilder(args);
builder.UseStartupForm<MainForm>();

using WinFormsApplication application = builder.Build();
application.Lifetime.ApplicationStarted +=
    (_, _) => Console.WriteLine("Started");
application.Lifetime.ApplicationStopping +=
    (_, _) => Console.WriteLine("Stopping");
application.Lifetime.ApplicationStopped +=
    (_, _) => Console.WriteLine("Stopped");

application.Run();
```

**Visual Basic:**

```vb
Dim builder As WinFormsApplicationBuilder =
    WinFormsApplication.CreateBuilder(args)
builder.UseStartupForm(Of MainForm)()

Using application As WinFormsApplication = builder.Build()
    AddHandler application.Lifetime.ApplicationStarted,
        Sub(sender, e) Console.WriteLine("Started")
    AddHandler application.Lifetime.ApplicationStopping,
        Sub(sender, e) Console.WriteLine("Stopping")
    AddHandler application.Lifetime.ApplicationStopped,
        Sub(sender, e) Console.WriteLine("Stopped")

    application.Run()
End Using
```

When an application supplies an existing host, configure and build that `IHost` in application code, then pass it to `UseHost(host)` in either language. The WinForms application takes ownership according to the existing `UseHost` contract. Do not configure the builder-owned host and supply a second host.

## Compatibility-adapter strategy

1. **No automatic wrapper around `WindowsFormsApplicationBase`.** Its startup event can cancel before the loop, its shutdown event occurs after the loop, and it owns synchronization-context, exception, splash, settings, and optional singleton behavior. Running a second lifecycle coordinator or translating these events implicitly would change ordering and error semantics.
2. **Keep legacy VB projects on the legacy runtime until they opt into migration.** Existing `MyApplication`, `ApplicationEvents.vb`, `My.Settings`, and designer-generated main-form selection remain valid. A project can migrate individual application logic to services/forms without replacing the entry-point model all at once.
3. **Use shared primitives for migrated behavior when semantics match.** A compatible custom `ApplicationContext` can be selected through the builder; Generic Host services and lifetime notifications are language-neutral.
4. **Keep data migration explicit.** The existing legacy-settings migration method is opt-in. The builder must not save or import `My.Settings` merely because `SaveMySettingsOnExit` was set in old generated code.
5. **Do not add language-specific aliases for convenience alone.** VB syntax supports the same builder and events. Add an adapter only when it preserves a documented compatibility contract and cannot be expressed cleanly through existing shared APIs.

## Alternatives rejected

- **A VB-specific application builder or host:** duplicates builder, DI, configuration, and lifecycle ownership, and creates two public APIs that would drift.
- **Running the legacy base class around a new `WinFormsApplication`:** creates competing owners for message-loop entry, shutdown, exception handling, and splash/settings cleanup.
- **Treating `ApplicationStarted` as VB `Startup`:** loses pre-run cancellation and changes the moment at which user code executes.
- **Treating `ApplicationStopped` as an automatic settings-save trigger:** changes existing settings ownership and can persist data without explicit user consent.
- **Automatically forwarding every `My.*` service to Generic Host:** merges unrelated compatibility APIs and expands the builder beyond application composition.

## Dependencies and follow-up ownership

| Issue | Contract boundary |
|---|---|
| #14973 | Supplies the source-backed capability inventory and compatibility findings. |
| #14975 | Owns shared startup/shutdown behavior, cancellation decisions, exception routing, and lifecycle-order tests. |
| #14976 | Owns shared single-instance activation, IPC, and argument forwarding. |
| #14977 | Supplies `UseSplashScreen`, the shared application/runtime information contracts, and synchronization-context-aware network availability service. |
| #14978 | Owns cross-language parity tests for equivalent VB and C# scenarios. |
| #14979 | Owns the incremental migration examples; this design deliberately does not require a project rewrite. |

The legacy framework must remain supported throughout these follow-ups. Any public API additions or adapters still require the repository's normal API review and compatibility process.

## References

- [VB Application Framework capability inventory](vb-application-framework-inventory.md).
- [`WinFormsApplicationServices`](../../src/System.Windows.Forms/Microsoft/Extensions/WinForms/WinFormsApplicationServices.cs) and [`WinFormsSplashScreen`](../../src/System.Windows.Forms/Microsoft/Extensions/WinForms/WinFormsSplashScreen.cs).
- [Application Builder core contracts](core-contracts.md).
- [Application Builder lifetime architecture](lifetime-architecture.md).
- [C# and Visual Basic Application Builder samples](samples/README.md).
- [C# graceful-shutdown sample](../../src/test/integration/ApplicationBuilderSamples/CSharp/GracefulShutdown/Program.cs) and [Visual Basic graceful-shutdown sample](../../src/test/integration/ApplicationBuilderSamples/VisualBasic/GracefulShutdown/Program.vb).
- [`WinFormsApplicationBuilder`](../../src/System.Windows.Forms/Microsoft/Extensions/WinForms/WinFormsApplicationBuilder.cs), [`WinFormsApplication`](../../src/System.Windows.Forms/Microsoft/Extensions/WinForms/WinFormsApplication.cs), and [`WinFormsApplicationLifetime`](../../src/System.Windows.Forms/Microsoft/Extensions/WinForms/WinFormsApplicationLifetime.cs).
