# Shared Visual Basic and C# application-feature contracts

**Scope:** issue [#14974](https://github.com/dotnet/winforms/issues/14974), following the capability inventory in [#14973](https://github.com/dotnet/winforms/issues/14973).

**Decision:** Visual Basic and C# applications use the same `Microsoft.Extensions.WinForms` contracts and Generic Host infrastructure. Keep `WindowsFormsApplicationBase` as the supported compatibility model; do not add a second VB-only builder, host, or lifecycle system. This issue defines the mapping and migration boundaries. It does not implement lifecycle behavior assigned to #14975 or feature APIs assigned to #14976 and #14977.

## Contract layers

| Layer | Shared contract | Responsibility |
|---|---|---|
| Composition | `WinFormsApplicationBuilder` | Select a startup form or `ApplicationContext`; optionally configure or accept a Generic Host; register services, logging, configuration, and opt-in WinForms services. |
| Runtime | `WinFormsApplication` | Own the single `Run()` operation on the caller's WinForms thread, coordinate the message loop with the optional host, and release application resources. |
| Application lifecycle | `WinFormsApplicationLifetime` | Publish one-shot `ApplicationStarted`, `ApplicationStopping`, and `ApplicationStopped` notifications. It is a notification surface, not a separate host or a cancellable event pipeline. |
| Host lifecycle | `IHost`, `IHostApplicationLifetime`, hosted services | Reuse Generic Host startup, stopping, cancellation tokens, and service ownership. Do not duplicate host tokens or hosted-service abstractions in a VB namespace. |
| Optional services | Existing builder extensions and registered services | Opt into host exception logging or the user-settings service. These are not implicitly enabled by choosing VB or C#. |

The public builder contract already supports both languages. `CreateBuilder()` retains the hostless path unless a host-building surface is used. `CreateBuilder(args)` configures a builder-owned Generic Host; `UseHost(IHost)` selects an already-built host instead. Those modes cannot be combined. Startup selection is deferred when a form factory is used; a supplied form or context retains the creating-thread requirement. `Build()` creates the application object; `Run()` is single-use and executes on the calling WinForms thread.

Applications continue to own framework-generated initialization such as `ApplicationConfiguration.Initialize()` or the equivalent setup used by their project template. This must happen before controls create native handles. The builder does not replace, reorder, or infer application-specific DPI, visual-style, font, or color-mode configuration.

## Mapping the confirmed VB capabilities

| Existing VB capability | Shared contract or migration boundary | Compatibility decision |
|---|---|---|
| Generated main form and application context | `UseStartupForm<TForm>()`, a startup-form factory/instance, or `UseApplicationContext(...)`. | The shared startup-selection API is the preferred new-app path. Preserve designer-generated `OnCreateMainForm` in existing applications. Do not run the legacy application model and the builder as competing owners of the same message loop. |
| `ApplyApplicationDefaults` | Application-owned WinForms initialization before the builder runtime starts. | No direct builder event replacement is introduced here. Retain legacy defaults in `WindowsFormsApplicationBase`; a future shared configuration contract must preserve the before-handle-creation constraint. |
| `Startup` and `StartupEventArgs.Cancel` | No exact current equivalent. `ApplicationStarted` is a successful-start notification, not a pre-start callback or a cancellation point. | Do not map startup cancellation to `ApplicationStopping` or to a throwing event handler. A shared pre-start/cancellation contract, if required, belongs to #14975. |
| `Shutdown` | `ApplicationStopping` signals that terminal shutdown coordination is beginning; `ApplicationStopped` signals that loop/host shutdown has completed. | Neither event is an exact replacement for the legacy post-loop `Shutdown` event. The legacy event remains supported by the legacy runtime. |
| `ShutdownStyle` and main-form-close policy | Supply a custom `ApplicationContext` with `UseApplicationContext(...)` when its behavior is compatible with builder ownership. | There is no direct builder option equivalent to `AfterMainFormCloses` or `AfterAllFormsClose`. Do not silently impose legacy close policy on C# or new VB applications. |
| `CommandLineArgs` | `CreateBuilder(args)` passes arguments to Generic Host configuration. | This is not a raw, read-only application argument collection. Do not promise equivalent parsing or expose a false mapping in migration guidance. Forwarded activation arguments are deferred to #14976. |
| `UnhandledException` and `ExitApplication` | `EnableExceptionLogging()` is an opt-in logging integration when a Generic Host is configured. Applications may also use the established WinForms/CLR exception mechanisms directly. | Logging is not event handling or recovery. Do not claim the builder logger replaces the VB event, its exit decision, or debugger behavior. |
| `IsSingleInstance`, `StartupNextInstance`, and argument forwarding | No current shared contract. | Deferred to #14976, including identity, session/user security boundaries, activation ordering, and failure semantics. |
| `SplashScreen` and minimum display time | No current shared contract. | Deferred to #14977; the legacy secondary UI loop and cancellation/failure behavior require a deliberate design. |
| `SaveMySettingsOnExit` and `My.Settings` | `AddUserSettings()` opts into the builder's independent user-settings service. The existing `MigrateFromLegacySettingsAsync(ApplicationSettingsBase)` API is an explicit migration path. | Do not enable settings, import values, change storage format, or save legacy settings automatically. This is not drop-in `My.Settings` parity. |
| `My.Application.Info`, `Log`, `Culture`, and `My.Computer` services | Use the appropriate platform, Generic Host, or application service explicitly. | No single builder service is equivalent to all VB `My.*` conveniences. New application/network/information services are deferred to #14977; existing VB APIs remain supported. |
| `ApplicationEvents.vb` and `MyApplication` event wiring | Keep existing generated partial classes and `Handles Me.<Event>` handlers for legacy projects. New code can subscribe to the shared runtime lifetime events from either language. | Do not require project-system regeneration or automatic event translation as a condition for builder use. Incremental examples belong to #14979. |

## Lifecycle, threading, cancellation, and errors

### Existing shared lifetime notifications

The current runtime publishes the same `WinFormsApplicationLifetime` notifications to C# and VB:

1. `ApplicationStarted` is raised once after the startup context/form has been selected and, when configured, Generic Host startup succeeds. In hostless mode it is raised before `Application.Run`; with a host, the message loop is entered to keep the UI responsive while host startup runs, and the startup form is shown after the started notification.
2. `ApplicationStopping` is raised once when terminal shutdown coordination begins, before the host is stopped and the WinForms loop is allowed to unwind.
3. `ApplicationStopped` is raised once during finalization after loop/host shutdown coordination, before `Run()` returns.

If runtime startup fails after lifecycle coordination begins, `ApplicationStarted` is omitted. Stop/finalization notifications and cleanup still run. Failures in `Build()` or `Run()` preconditions occur before lifecycle coordination and do not synthesize lifetime events. Repeated stop requests converge on the one-shot lifetime state. Generic Host lifetime tokens remain the host-facing source of truth; the WinForms events do not create a second token system.

These are synchronous .NET events. Runtime coordination raises them on the application lifecycle/UI thread; a host-originated stop from another thread is dispatched to that thread. Handlers should be short, non-blocking, and avoid synchronous waits for work that needs the UI thread. Generic Host service callbacks are not promised to run off the UI thread; do not block in their synchronous portions or depend on UI access from a hosted service.

The events are notifications, not cancellation hooks. Use standard host cancellation tokens for hosted work and an application's own pre-run decision when deciding whether to call `Run()`. A WinForms form-close veto is distinct from host shutdown. A host-stop request is terminal once host stopping has begun; it is not rolled back into a running application because a form declines to close.

**Ordering discrepancy to resolve:** the current runtime posts host startup with `BeginInvoke` and enters `Application.Run` before `IHost.StartAsync` completes; the started notification then fires on the UI thread before the startup form is shown. The [lifetime architecture record](lifetime-architecture.md) instead specifies that host startup completes before `Application.Run` begins. This document records observed implementation behavior, not a new permanent guarantee. #14975 must reconcile the runtime and architecture record and lock down the intended ordering with tests.

### Error semantics

- Builder validation and `Build()` failures are synchronous and do not start the message loop.
- Startup-form/context creation and host-start failures prevent a successful `ApplicationStarted` notification. The runtime performs best-effort stop and cleanup, then surfaces the failure from `Run()`.
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
| #14977 | Owns splash-screen semantics and language-neutral network/application-information services. |
| #14978 | Owns cross-language parity tests for equivalent VB and C# scenarios. |
| #14979 | Owns the incremental migration examples; this design deliberately does not require a project rewrite. |

The legacy framework must remain supported throughout these follow-ups. Any public API additions or adapters still require the repository's normal API review and compatibility process.

## References

- [VB Application Framework capability inventory](vb-application-framework-inventory.md).
- [Application Builder core contracts](core-contracts.md).
- [Application Builder lifetime architecture](lifetime-architecture.md).
- [C# and Visual Basic Application Builder samples](samples/README.md).
- [C# graceful-shutdown sample](../../src/test/integration/ApplicationBuilderSamples/CSharp/GracefulShutdown/Program.cs) and [Visual Basic graceful-shutdown sample](../../src/test/integration/ApplicationBuilderSamples/VisualBasic/GracefulShutdown/Program.vb).
- [`WinFormsApplicationBuilder`](../../src/System.Windows.Forms/Microsoft/Extensions/WinForms/WinFormsApplicationBuilder.cs), [`WinFormsApplication`](../../src/System.Windows.Forms/Microsoft/Extensions/WinForms/WinFormsApplication.cs), and [`WinFormsApplicationLifetime`](../../src/System.Windows.Forms/Microsoft/Extensions/WinForms/WinFormsApplicationLifetime.cs).
