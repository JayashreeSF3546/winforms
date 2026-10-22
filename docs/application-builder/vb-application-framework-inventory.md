# Visual Basic Application Framework capability inventory

**Scope:** issue [#14973](https://github.com/dotnet/winforms/issues/14973), the first child issue in the VB Application Framework parity stream under [#14972](https://github.com/dotnet/winforms/issues/14972).

**Purpose:** record the existing Visual Basic Application Framework behavior and define parity boundaries for later child issues. This inventory does not add runtime contracts or change legacy behavior.

## Summary

`Microsoft.VisualBasic.ApplicationServices.WindowsFormsApplicationBase` is a compatibility application model, not just a startup helper. It owns startup and shutdown events, the WinForms message loop, a custom `ApplicationContext`, exception routing, a splash-screen loop, optional single-instance activation, and lazy network-availability notifications. Its base classes also expose application metadata, logging, culture, environment variables, and command-line arguments. Visual Studio-generated `MyApplication` and `ApplicationEvents.vb` code is part of the developer experience.

The existing builder infrastructure already supplies language-neutral startup selection, Generic Host integration, lifetime notifications, opt-in exception logging, and user-settings services. These are useful shared primitives, but they are not drop-in replacements for all legacy events or `My.*` APIs. Preserve the existing VB framework for compatibility; add shared capabilities through the `Microsoft.Extensions.WinForms` contracts and use explicit migration adapters where needed. Do not build a second VB-only host or lifecycle system.

## Legacy startup and shutdown sequence

For a non-single-instance application, `WindowsFormsApplicationBase.Run(args)` stores the arguments and invokes the application model:

1. `OnInitialize(args)` raises `ApplyApplicationDefaults`, installs the WinForms synchronization context, applies DPI/visual-style/color/form defaults, and may start the splash screen.
2. `OnStartup(StartupEventArgs)` raises `Startup`. Setting `Cancel = true` prevents `OnRun` and `OnShutdown`.
3. `OnRun()` creates the main form through the generated `OnCreateMainForm` hook if necessary and runs `Application.Run` with the VB framework's `ApplicationContext`.
4. After the message loop returns normally, `OnShutdown()` raises `Shutdown`.

With a debugger attached, the sequence is the same but the broad application-model exception catch is omitted. In a single-instance application, pipe ownership is established before this sequence; a subsequent instance only forwards its arguments and returns.

`ShutdownStyle.AfterMainFormCloses` ends the message loop when the main form closes. `AfterAllFormsClose` chooses another open form as the main form and continues until no forms remain. The `Shutdown` event is not cancelable. The separate WinForms `FormClosing`/`ApplicationContext` behavior remains available to applications that need to veto a close.

### Lifecycle capability matrix

| Area | Existing VB behavior | Compatibility and implementation notes |
|---|---|---|
| Startup initialization | `ApplyApplicationDefaults` runs first and can set font, DPI, visual-style, color, form-reveal, and splash timing defaults. The framework then applies those values before creating the main form. | Keep initialization before any control handle is created. The shared builder does not replace application-specific `ApplicationConfiguration.Initialize()` calls. |
| Startup event | `Startup` receives `StartupEventArgs`, including a read-only command-line collection. | Preserve the event for legacy apps. Builder lifetime notifications are not equivalent to this pre-message-loop callback. |
| Startup cancellation | `StartupEventArgs.Cancel` prevents the message loop and suppresses `Shutdown`. `ApplyApplicationDefaults` itself is not cancelable. | The current flow has no explicit cleanup callback after startup cancellation. Since the splash may already have started during initialization, cancellation/splash cleanup must be characterized before a migration adapter promises different semantics. |
| Main form and context | Generated `OnCreateMainForm` selects the initial form. The application model owns a custom `ApplicationContext` and exposes it through `ApplicationContext`. | The builder can select a deferred startup form or an `ApplicationContext`; this is the closest shared primitive, not identical designer-generated behavior. |
| Shutdown event | `Shutdown` is raised after `Application.Run` returns normally, provided startup was not canceled. Its event arguments are plain `EventArgs`. | There is no cancellation parameter. The event is not a substitute for `ApplicationStopping` or form-closing cancellation. |
| Shutdown condition | `ShutdownStyle` selects main-form-close or last-form-close behavior. | The builder can accept a custom context, but does not currently provide this VB-specific policy as a direct option. |
| Command line | `CommandLineArgs` omits the executable path, is cached/read-only, and can be primed from the generated `Main(args)` argument array. `Startup` gets it; `StartupNextInstance` gets the later launch's arguments. `/nosplash` and `-nosplash` suppress the splash screen. | `CreateBuilder(args)` passes arguments into Generic Host configuration; the builder does not expose a VB-compatible raw, read-only application argument property or forwarded-activation event. |
| Threading and defaults | `OnInitialize` establishes a WinForms synchronization context and applies application-wide defaults before normal form creation. | Keep ordering explicit across languages. The builder runtime owns the message loop, but application configuration remains the application's responsibility. |

## Exception behavior

The legacy `UnhandledException` event has `ExitApplication`, defaulting to `true` at the framework call sites. It is subscribed to `Application.ThreadException` only while at least one handler is attached; this avoids changing WinForms exception handling when nobody opted in. If a handler sets `ExitApplication = false`, the event is treated as handled. Exceptions thrown by the exception handler itself are not caught and recursively reported.

| Exception origin | Existing behavior | Parity implication |
|---|---|---|
| UI message-loop exception | Routed through `Application.ThreadException` and translated to `UnhandledException` only when the VB event has a handler. | Builder exception logging is opt-in and logs/handles UI exceptions; it does not provide the VB event or its `ExitApplication` decision. |
| Initialization/startup/run/shutdown exception outside the message pump | Without a debugger, the application model catches it and offers it to `UnhandledException`; if no handler handles it, it is rethrown. With a debugger, it bubbles directly to the debugger. | Do not conflate notification, logging, and exception suppression. Any shared lifecycle implementation must retain cleanup and exception propagation guarantees. |
| Arbitrary background-thread exception | It is not automatically a WinForms `ThreadException`; process/runtime behavior applies unless the application explicitly observes or marshals it. | The builder's optional logger covers additional CLR/task exception notifications, but is not the same exception-event contract. |
| Network-availability handler exception | The VB application event adaptor routes handler failures through `UnhandledException`; an unhandled failure is rethrown. | This is a VB-specific event-dispatch policy, not a general background exception policy. |
| Exception from `StartupNextInstance` handler | The marshalling adaptor suppresses failures before the UI callback is entered, but allows exceptions thrown by the user handler to propagate. | Activation needs dedicated ordering and failure tests before shared support is claimed. |

The current builder's `EnableExceptionLogging()` is an optional Generic Host logging integration for WinForms and CLR/task exception sources. It neither adds an application exception event nor changes legacy `My.Application.UnhandledException` semantics.

## Single-instance behavior

When `IsSingleInstance` is enabled, `Run` uses a named pipe whose name is based on the calling assembly's `GuidAttribute` plus major/minor version, or the module MVID when no GUID is present. In generated applications, the caller is normally the application assembly. The first process creates one asynchronous pipe server with `PipeOptions.CurrentUserOnly` and listens while its application model runs. A later process connects, serializes its command-line arguments, and exits; the first process marshals `StartupNextInstance` to the main form or synchronization context. By default, the original main form is restored from minimized state and activated after the event when `BringToForeground` is true.

| Concern | Current behavior and boundary |
|---|---|
| Detection and identity | One pipe server per calculated assembly identity. The identity does not include an explicit Windows session or installation path. |
| IPC and access | Named pipe, one connection at a time, asynchronous byte transport, `CurrentUserOnly`; arguments are serialized as a string array. A malformed payload is ignored by the reader. |
| Activation | `StartupNextInstance` runs on the UI/synchronization-context path and carries `BringToForeground`; it is not cancelable. If no main form exists, the synchronization context is used. |
| Timeout and failure | The later instance waits up to 2,500 ms. Connection/setup failures become `CantStartSingleInstanceException`. |
| Races and scope | The pipe server is established before application initialization and startup complete. Cross-user, cross-session, rapid-relaunch, activation-before-main-form, and identity-collision behavior require explicit validation; current pipe naming and access policy do not define all of those boundaries. |

Related issue reports are inputs to the later single-instance work, not proof of current status or a substitute for reproduction:

- [#11232](https://github.com/dotnet/winforms/issues/11232) reports a single-instance startup failure for different users on an RDP/terminal server.
- [#11365](https://github.com/dotnet/winforms/issues/11365) reports unexpected behavior when identical application output is launched from different folders.
- [#8441](https://github.com/dotnet/winforms/issues/8441) reports failure to create an MDI child from `StartupNextInstance`.
- [#4805](https://github.com/dotnet/winforms/issues/4805) reports missing `StartupNextInstance` when no main form is set; current source has a synchronization-context fallback, but this path lacks focused regression coverage.
- [#3296](https://github.com/dotnet/winforms/issues/3296) discusses instance identity compatibility with .NET Framework. Current source uses GUID plus major/minor when available and MVID only as a fallback.

The shared builder has no single-instance detection, activation, or argument-forwarding contract today. These features belong to #14976, which must specify user/session/elevation boundaries, pipe naming, races, error behavior, and validation scope before adding a shared API.

## Splash-screen behavior

The generated application can provide a splash screen through `OnCreateSplashScreen`, or the application can assign `SplashScreen`. Initialization starts it on a separate task/message loop, unless `/nosplash` or `-nosplash` was passed. The default minimum display time is 2,000 ms; a positive configured duration is measured from the splash loop starting. The main form's `Load` handler coordinates its first display with the timer, then the framework activates the main form and marshals splash disposal to the splash form's UI thread.

This implementation depends on two UI loops/threads and a timer/task coordination handshake. Splash display is not available on the current builder API. Startup cancellation and initialization/startup failure paths do not run the normal main-form/shutdown path; the existing code does not explicitly close an already-started splash in those paths. #14977 owns any shared splash contract and its failure/cancellation semantics.

## Application and device services

| Service | Existing VB surface | Shared parity assessment |
|---|---|---|
| Application information | `My.Application.Info` exposes assembly name, location, version, metadata attributes, loaded assemblies, stack trace, and working set through `AssemblyInfo`. | Generic Host environment metadata is useful but not equivalent. A language-neutral application-information service is a #14977 design question. |
| Runtime/culture | `ApplicationBase` exposes current culture/UI culture, culture-change methods, and environment-variable lookup; `AssemblyInfo` includes runtime/process information such as working set. | Keep runtime and assembly information distinct from hosting environment; no exact builder adapter is present. |
| Logging | `My.Application.Log` exposes `Microsoft.VisualBasic.Logging.Log`. | Generic Host `ILogger` is a shared alternative, but provider configuration and logging semantics differ; migration is opt-in. |
| Network availability | `My.Computer.Network.IsAvailable` reports network availability. `NetworkAvailabilityChanged` subscribes lazily, observes address changes, and marshals notifications to the captured synchronization context. `My.Application.NetworkAvailabilityChanged` is also available and is initialized after the application principal/context setup. | No equivalent service/event is on the builder. This and its synchronization/error policy are in #14977. |
| Other computer devices | `My.Computer` lazily exposes ComputerInfo, Clock, FileSystem, Network, and Registry services; other VB device APIs include keyboard, mouse, and audio. | These are broader VB runtime conveniences, not builder responsibilities or a target for wholesale reimplementation. |
| Settings | Generated VB application code sets `SaveMySettingsOnExit`; `My.Settings` is supplied through VB project/settings generation. In this WinForms runtime source tree, the flag is stored and exposed, but no runtime call to `My.Settings.Save()` was found. | The builder's user-settings service is a different, opt-in store. The existing `ApplicationSettingsBase` migration helper is an explicit migration path, not drop-in `My.Settings` compatibility. Confirm settings-save ownership with generated-project integration before defining parity. |
| User/principal | `WindowsFormsApplicationBase` defaults to Windows authentication and sets `Thread.CurrentPrincipal` when possible; `ApplicationBase.User` provides principal identity/role access. | Preserve existing behavior; do not introduce a second authentication or identity subsystem in the builder. |

## Designer, template, and migration constraints

Visual Studio-generated `MyApplication` code configures `IsSingleInstance`, visual styles, `SaveMySettingsOnExit`, shutdown style, and the main-form factory. `ApplicationEvents.vb` uses partial `MyApplication` methods and `Handles Me.<Event>` syntax. Existing event handlers and generated overrides are source- and tooling-level compatibility surfaces even where the runtime has a language-neutral analogue.

The runtime currently implements `StartupNextInstance`, but the integration `ApplicationEvents.vb` example still labels it “Not yet implemented.” Treat that discrepancy as a project-system/template validation item, not as justification to remove the runtime event.

Migration must be incremental. Existing VB applications can continue using `MyApplication` and `WindowsFormsApplicationBase`; new or migrated applications can use the same `Microsoft.Extensions.WinForms` builder APIs from VB or C#. A migration adapter may translate selected VB events/settings, but should not require a complete rewrite or replace designer-generated behavior implicitly.

## Parity decisions and ownership

For this inventory, **full parity** means a behavior has a shared builder contract with sufficiently matching semantics; **partial parity** means a shared primitive exists but semantics or surface differ; **migration-only** means legacy behavior stays available while migration is explicit; **non-goal** means the feature remains owned by the VB runtime or another platform layer.

| Classification | Capabilities and decision |
|---|---|
| Full parity target | Both languages use the same `WinFormsApplicationBuilder`, `WinFormsApplication`, `WinFormsApplicationLifetime`, Generic Host, and service contracts. No VB-only builder/host/lifecycle implementation. |
| Partial parity | Startup form/application-context selection maps to builder startup selection. `ApplicationStarted`/`ApplicationStopping`/`ApplicationStopped` provide shared host lifecycle notifications, but do not match cancelable VB `Startup`, post-loop `Shutdown`, or `ShutdownStyle`. Generic Host command-line configuration is not the same as VB's read-only application args or activation args. Exception logging is not the VB exception event. |
| Migration-only | `ApplicationEvents.vb`/`MyApplication` event wiring, `My.Settings` save behavior, `StartupNextInstance`, the VB splash screen, VB `Log`, and legacy `My.Application` services remain supported through their existing APIs; adapters and migration samples are explicit and opt-in. |
| Non-goal | Reimplementing `ConsoleApplicationBase`, VB compiler/project-system generation, all of `My.Computer`, authentication/principal management, or a new settings/logging/configuration/DI/host infrastructure. Reuse platform and Generic Host services where they meet the intended contract. |

### Architecture decisions

1. Keep `WindowsFormsApplicationBase` binary/source-compatible; #14973 is inventory and decision documentation only.
2. Put new cross-language lifecycle and service contracts in `Microsoft.Extensions.WinForms`. VB and C# call the same APIs; language-specific code is limited to compatibility adapters and generated-code migration.
3. Treat `ApplicationConfiguration.Initialize()` and other application defaults as application-owned initialization. The builder does not replace generated setup or promise it can apply settings after native handles exist.
4. Preserve semantic distinctions: startup cancellation versus host lifetime notification, shutdown notification versus form-close cancellation, exception logging versus exception handling, raw arguments versus host configuration, and legacy settings versus the builder's settings service.
5. Keep settings and service migration explicit. Do not silently save or import legacy settings, change the storage format, or claim `My.Settings` equivalence.
6. Do not make single-instance, splash, network, or application-information APIs part of the shared contract until their compatibility and failure semantics are specified in their owning issues.

### Follow-up issue ownership

| Issue | Follow-up scope |
|---|---|
| #14974 | Define shared VB/C# contracts and any compatibility adapters. |
| #14975 | Implement shared lifecycle and validate event ordering/cancellation. |
| #14976 | Implement and validate single-instance detection, activation, IPC, and argument forwarding. |
| #14977 | Define splash-screen and application/network/information services. |
| #14978 | Add cross-language parity and regression tests, including the legacy edge paths identified here. |
| #14979 | Provide incremental VB and C# migration samples. |
| #14980 | Validate multi-user/session/RDP and rapid-relaunch behavior. |
| #14981 | Benchmark legacy VB and shared builder behavior. |

## Existing test coverage

The current VB unit tests exercise application properties, basic splash show/hide, application-default application, single-instance identity generation, and pipe creation/argument transfer. They do not currently establish full startup/shutdown ordering, startup-cancellation cleanup, exception ordering, splash minimum-duration/failure behavior, or real multi-process/multi-user/RDP behavior. The known-issue list above and these gaps should inform #14978 and #14980.

## Source map

- [`WindowsFormsApplicationBase.vb`](../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/ApplicationServices/WindowsFormsApplicationBase.vb#L545): lifecycle flow, initialization, events, splash, exception routing, instance ID, and `Run`.
- [`WindowsFormsApplicationBase.WinFormsAppContext.vb`](../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/ApplicationServices/WindowsFormsApplicationBase.WinFormsAppContext.vb#L39): shutdown policy.
- [`SingleInstanceHelpers.vb`](../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/ApplicationServices/SingleInstanceHelpers.vb#L13): named-pipe transport and argument serialization.
- [`ConsoleApplicationBase.vb`](../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/ApplicationServices/ConsoleApplicationBase.vb#L36): command-line argument contract.
- [`ApplicationBase.vb`](../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/ApplicationServices/ApplicationBase.vb#L28) and [`AssemblyInfo.vb`](../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/ApplicationServices/AssemblyInfo.vb#L56): application metadata, culture, log, and environment helpers.
- [`Network.Availability.vb`](../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/Devices/Network.Availability.vb#L38) and [`ServerComputer.vb`](../../src/Microsoft.VisualBasic.Forms/src/Microsoft/VisualBasic/Devices/ServerComputer.vb#L95): `My.Computer.Network`.
- [`WindowsFormsApplicationBaseTests.vb`](../../src/Microsoft.VisualBasic.Forms/tests/UnitTests/System/Windows/Forms/WindowsFormsApplicationBaseTests.vb), [`WindowsFormsApplicationBaseTests.cs`](../../src/Microsoft.VisualBasic/tests/UnitTests/Microsoft/VisualBasic/ApplicationServices/WindowsFormsApplicationBaseTests.cs), and [`SingleInstanceHelpersTests.vb`](../../src/Microsoft.VisualBasic.Forms/tests/UnitTests/System/Windows/Forms/SingleInstanceHelpersTests.vb): current unit-test coverage.
- [`ApplicationEvents.vb`](../../src/test/integration/VisualBasicTemplate/ApplicationEvents.vb) and the [Visual Basic project template](../../pkg/Microsoft.Dotnet.WinForms.ProjectTemplates/content/WinFormsApplication-VisualBasic/My%20Project/Application.Designer.vb): generated VB application patterns.
- [`WinFormsApplicationBuilder.cs`](../../src/System.Windows.Forms/Microsoft/Extensions/WinForms/WinFormsApplicationBuilder.cs), [`WinFormsApplication.cs`](../../src/System.Windows.Forms/Microsoft/Extensions/WinForms/WinFormsApplication.cs), and [`WinFormsApplicationLifetime.cs`](../../src/System.Windows.Forms/Microsoft/Extensions/WinForms/WinFormsApplicationLifetime.cs): existing shared builder and lifetime primitives.
- [Application Builder core contracts](core-contracts.md): current shared infrastructure decisions, including user settings and exception logging.
