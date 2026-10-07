# WinForms Application Builder service infrastructure architecture

**Status:** Decision record for [#14956](https://github.com/dotnet/winforms/issues/14956)

**Parent feature:** [#14955](https://github.com/dotnet/winforms/issues/14955)

**Parent proposal:** [#14082](https://github.com/dotnet/winforms/issues/14082)

This document defines the boundaries for configuration, options, user
settings, logging, exception integration, hosted services, and health
diagnostics. It consumes the existing WinForms host/lifetime and DI contracts;
it does not implement them or introduce those later features in this issue.
The decisions guide #14957 through #14960.

## Decision summary

1. Reuse `Microsoft.Extensions.*` contracts and providers wherever they fit.
   The Generic Host owns its service provider, configuration root, options,
   logger factory, hosted services, and host lifetime. WinForms owns the UI
   thread, message loop, and the bridge between host lifetime and desktop
   shutdown.
2. Treat `IConfiguration` as a read-only view. User-editable per-user
   preferences belong to a separate `IUserSettingsService`; an optional
   settings-backed configuration provider may project selected values, but
   settings are never written through `IConfiguration` or back to
   `appsettings.json`.
3. Preserve Generic Host provider semantics: later configuration providers
   override earlier providers. Use `IHostEnvironment` for environment identity
   and load user secrets only for the Development environment.
4. Validate required configuration and options before the WinForms message
   loop shows the startup UI. Startup failures are reported to the caller with
   their original exception and actionable context; the infrastructure must
   not silently substitute defaults or display an unsolicited modal error
   dialog.
5. Use the standard `IHostedService`, `BackgroundService`, and health-check
   abstractions. They do not own or replace the WinForms message loop, and
   background work must not block the UI thread.
6. Keep diagnostics opt-in and privacy-safe. Do not add telemetry, a file
   logger, a network health endpoint, or automatic exception collection by
   default.

## Reuse and ownership boundaries

| Concern | Reuse / owner | WinForms-specific responsibility |
| --- | --- | --- |
| Host and service provider | `Microsoft.Extensions.Hosting` owns the built `IHost`, root provider, and disposal of registered services. | The application builder composes or accepts a host. It does not implement a second container. An externally built host is not silently reconfigured. |
| Configuration | `Microsoft.Extensions.Configuration` owns `IConfiguration`, provider composition, reload tokens, and binding sources. | The WinForms builder provides desktop-appropriate defaults and a public extension point; it does not create a competing configuration model. |
| Options | `Microsoft.Extensions.Options` owns `IOptions<T>`, `IOptionsSnapshot<T>`, `IOptionsMonitor<T>`, binding, and validation contracts. | The startup bridge ensures required validation finishes before the message loop shows the UI. Existing Form scopes determine where scoped snapshots can be resolved. |
| Logging | `Microsoft.Extensions.Logging` owns `ILogger`, `ILogger<T>`, `ILoggerFactory`, filters, and providers. | The WinForms integration may bridge selected WinForms exception events to the host logger, without replacing WinForms exception policy or installing a provider. |
| User settings | A WinForms-specific, replaceable `IUserSettingsService` owns mutable per-user preferences and their persistence. | Keep settings separate from deployment configuration and provide an opt-in migration path from generated settings. |
| Hosted services | The Generic Host owns `IHostedService`, `BackgroundService`, start/stop order, and cancellation. | The application coordinates host startup and shutdown with the existing UI thread and message loop. |
| Health diagnostics | `Microsoft.Extensions.Diagnostics.HealthChecks` owns registration, execution, and `HealthReport`. | WinForms can provide an explicit desktop reporting/invocation surface; it does not add HTTP endpoints or poll checks automatically. |
| UI lifetime | WinForms owns `Application.Run`, `ApplicationContext`, UI-thread affinity, modal loops, and WinForms synchronization-context behavior. | The host bridge marshals terminal shutdown to the UI thread and keeps the message loop responsive during asynchronous teardown. |

The builder should expose standard public extension points (for example,
`IConfigurationBuilder`, `IServiceCollection`, and `ILoggingBuilder`, or the
corresponding `IHostApplicationBuilder` surface) so provider and library
authors do not depend on WinForms internals. Existing applications that do not
use the Application Builder retain their current startup, configuration,
logging, and exception behavior.

## Workload patterns reviewed

| Workload | Adopt | Keep workload-specific |
| --- | --- | --- |
| Generic Host / Worker Service | Host composition, DI, configuration, logging, options, `IHostedService`, cooperative cancellation, and graceful host shutdown. | A Worker Service's process loop is not a desktop message loop. |
| ASP.NET Core | The Generic Host and standard configuration, options, logging, hosted-service, and health-check contracts. | HTTP servers, middleware, endpoint routing, web roots, and HTTP health-check endpoints. A desktop health check is invoked or surfaced by desktop code, not mapped to an endpoint. |
| WPF | Composition through a host, resolving the startup window from DI, and stopping/disposing the host with the application. | WPF's `Startup`/`Exit` event wiring and dispatcher are not substitutes for WinForms `Application.Run`, `ApplicationContext`, or `WindowsFormsSynchronizationContext`. WinForms needs an explicit asynchronous exit gate because its normal loop boundary is synchronous. |
| .NET MAUI | Builder composition and the use of Microsoft.Extensions abstractions where supported. | MAUI's platform-dependent window lifecycle includes activation, deactivation, suspension, and resume; it is not equivalent to a single WinForms process/message-loop lifetime. |

The common builder experience is useful; copying another workload's
application lifecycle is not. In particular, neither ASP.NET Core's server
lifecycle nor MAUI's suspend/resume model should change existing WinForms
message-loop semantics.

## Configuration and environment

### Construction and defaults

- When the WinForms builder creates the host, it should compose the standard
  Generic Host configuration and logging infrastructure rather than build a
  parallel provider pipeline. Applications that pass an already-built `IHost`
  retain the configuration and providers they supplied.
- `IHostEnvironment` is the single environment identity. Use the Generic Host
  environment variable and command-line behavior; do not introduce a separate
  WinForms environment or apply `ASPNETCORE_ENVIRONMENT` web-host rules.
  `Development`, `Staging`, and `Production` are conventional names, with
  Production as the safe default.
- Resolve application configuration files from the executable's application
  directory by default (`AppContext.BaseDirectory`), not the process current
  directory. A GUI app can be launched with an unrelated working directory.
  Make the content root explicitly overridable. This is a deliberate
  desktop-specific default; custom hosts keep their own content root.
- A missing optional JSON file is allowed. A malformed file or invalid
  provider setup is an explicit startup error with the source/provider
  identified; it must not be ignored as if the file were absent.
- Keep configuration reload provider-driven. Enable reload for the default
  JSON providers, matching Generic Host behavior; a provider that does not
  support reload does not acquire it through a WinForms polling loop.

### Default application-configuration precedence

Precedence is listed from lowest to highest. The last provider containing a
key wins.

| Priority | Source |
| --- | --- |
| 1 | `appsettings.json` |
| 2 | `appsettings.{Environment}.json` |
| 3 | Optional settings-backed provider, when enabled |
| 4 | User Secrets when the environment is `Development` and a User Secrets ID is configured |
| 5 | Environment variables |
| 6 | Command-line arguments |
| 7 | Additional providers explicitly added after the defaults |

Additional providers are registered at an explicit position in the provider
sequence. Appending one after the defaults gives it the highest priority;
applications that want command-line arguments to remain highest must insert
their provider before the command-line provider. No custom provider is
required to implement an internal WinForms type.

Generic Host *host configuration* (such as `DOTNET_`-prefixed host settings)
is distinct from application configuration and keeps the host's own
precedence. User Secrets are a development convenience, not a deployed secret
store. Production credentials must come from an application-selected secure
provider; secrets must not be placed in source-controlled JSON or user
settings.

The user-settings provider is opt-in and read-only. When enabled, it projects
selected settings beneath a reserved `UserSettings` section so applications
can bind options without confusing mutable preferences with deployment
configuration. `IUserSettingsService` remains the only write path. A successful
save/reset/migration publishes a configuration change token for that
projection, allowing `IOptionsMonitor<T>` to observe the new committed values.
The service implementation and provider registration belong to #14958.

## Options and startup validation

- Reuse `IOptions<T>` for stable, cached options; `IOptionsMonitor<T>` for
  singleton consumers that need current values/change notifications; and
  `IOptionsSnapshot<T>` only inside a real DI scope. In this API, per-Form and
  dialog scopes are appropriate snapshot boundaries; the application root is
  not.
- Use standard binding and validation (`IValidateOptions<T>`, data annotation
  validation where appropriate, and the supported `ValidateOnStart` path).
  Do not implement a second binder or validator.
- Parse/configure providers and complete required options validation before
  entering `Application.Run`, which shows the startup window. The existing
  lifetime contract may construct the startup Form or `ApplicationContext`
  first, but no window is shown and no message loop begins when validation
  fails; the unshown startup object is cleaned up.
- Startup exceptions propagate to the caller. Preserve the original
  configuration/validation exception and include the provider or options type,
  section/property, environment, and startup phase where available. Do not log
  secret values or expose them in a generated dialog. Any partially built
  application/host must still be disposed, with cleanup failures retained as
  secondary diagnostics rather than masking the primary failure.
- Reload notifications are provided by `IOptionsMonitor<T>` only when the
  underlying provider supports change tokens. A failed reload is observable
  through the normal options error path; it must not silently publish invalid
  values or silently clear the last valid application state.

**Implementation dependency:** `ValidateOnStart` is part of host startup,
which the existing lifetime design performs after it obtains the startup UI
object and before `Application.Run`. #14957 must ensure the supported
Microsoft.Extensions.Options validation pipeline completes before the window
is shown, without manually running validators twice. If the acceptance
criterion is interpreted to forbid even constructing an unshown startup Form,
that stricter requirement conflicts with the current
[lifetime architecture](lifetime-architecture.md) and must be reconciled
explicitly; lazy `IOptions<T>.Value` access alone is not startup validation.

## User settings

Application configuration and user settings have different owners and
lifetimes. `IConfiguration` is an effective, layered, read-only view.
`IUserSettingsService` is a WinForms-specific, replaceable service for mutable
preferences belonging to the current user. An optional provider may expose
selected preferences to configuration consumers, but saving a preference must
not rewrite an application configuration provider.

| Decision | Default |
| --- | --- |
| Storage | Versioned JSON below the current user's Local Application Data directory, in an application-specific directory derived from stable product identity. Allow an explicit path/store override. Do not use the working directory or a shared machine-wide file by default. |
| Offline/roaming behavior | Local, offline-first storage. No network sync, roaming, telemetry, or encryption claim by default. Applications needing sync or secret storage supply their own implementation. |
| Versioning | Store a monotonically increasing schema version in the JSON document; do not use the executable's marketing version as the schema. Apply explicit, ordered schema migrations before loading values. |
| Upgrade and recovery | Keep the previous valid file until a migrated document has been validated and atomically committed. Preserve a corrupt input for diagnosis; try a known-good backup if available and otherwise report an actionable failure rather than silently replacing user data with defaults. Missing files initialize from defaults. |
| Writes and concurrency | Serialize writes within the service and coordinate competing processes for the same user/profile file. Write to a temporary file and atomically replace the target; do not expose partially written JSON. Surface read-only, lock, and I/O failures to the caller. |
| Reset | Restore the current schema's defaults through the same atomic write path; retain the previous file as recovery data until the reset succeeds. |
| Change notifications | Notify subscribers after a successful in-process save, reset, or migration commit; signal the optional configuration projection after commit. A file watcher and cross-process notification guarantee are not part of the default contract. |
| Legacy migration | Migration from `Settings.settings` / generated `ApplicationSettings` is opt-in. Import only user-scoped values through an explicit migration path; never require migration for existing applications or delete the original settings as part of import. |

The concrete API, serialization shape, backup retention, conflict policy, and
recovery UX belong to #14958. The boundary decision is that storage is
per-user, versioned, replaceable, and non-destructive.

## Logging and exception integration

- Resolve `ILogger`, `ILogger<T>`, and `ILoggerFactory` from the host container.
  Reuse the host's standard filters and providers, including provider
  extensions. Do not create a WinForms logger factory alongside the host.
- Do not add a file or telemetry provider automatically. File logging is an
  application/provider choice whose location, ACLs, rotation, retention, and
  failure policy must be documented. A GUI process must not create a file or
  transmit diagnostics without explicit application configuration.
- Keep structured log templates and categories. Default messages must not
  include settings contents, credentials, command-line secrets, or other
  sensitive values. Do not enable scopes or diagnostic collection that
  disclose sensitive data by default.
- Exception forwarding is opt-in and must preserve existing WinForms and CLR
  policy:

| Exception source | Boundary decision |
| --- | --- |
| `Application.ThreadException` / UI-dispatch exceptions | Do not subscribe globally by default. An opt-in adapter must make handling semantics explicit because subscribing may replace WinForms' default exception presentation. Do not change `UnhandledExceptionMode` implicitly. |
| `AppDomain.UnhandledException` | Treat `IsTerminating` as authoritative. Logging is best-effort only; never promise recovery from a terminating process exception. |
| `TaskScheduler.UnobservedTaskException` | Forward only when configured. Do not call `SetObserved` automatically or suppress the runtime's policy. |
| Startup/configuration failure | Propagate synchronously to the caller after cleanup; log only if a logger is available and the app has opted into that behavior. |
| Hosted/background-service failure | Use Generic Host failure behavior and cancellation. Log diagnostic context without swallowing the error or leaving a failed service silently running. |

- Host stop and disposal must complete before the application releases its
  logging provider graph. Provider-specific flush/disposal semantics remain
  owned by the provider and `IHost`; the WinForms layer must not promise a
  universal flush API that `ILogger` does not define.

The event subscriptions, exception policy, and shutdown-flush tests are owned
by #14959, not this architecture-only issue.

## Hosted services and health diagnostics

### Hosted-service lifecycle

- Register and run standard `IHostedService` and `BackgroundService`
  implementations through the Generic Host. Do not define a parallel WinForms
  background-service base class or start a second process/UI thread.
- Host startup completes before the WinForms message loop displays the startup
  window. Startup failure prevents the UI loop from starting and triggers
  cleanup. Shutdown requests propagate through the host's stopping token;
  services must cooperate with cancellation and honor the host's shutdown
  timeout.
- The UI thread must not synchronously block on long-running service startup,
  stop, or disposal work. Keep the message loop responsive while asynchronous
  shutdown runs. Host stop is terminal once the host has entered stopping; a
  form-close veto must not pretend that the already-stopping host returned to
  a running state.
- `IHostedService.StartAsync` should return promptly. `BackgroundService`
  implementations must not perform long synchronous work before their first
  incomplete `await`; the host does not make arbitrary user code non-blocking
  or guarantee that `ExecuteAsync` begins on a worker thread. Background work
  must use the stopping token, avoid capturing the UI context for its
  background loop (for example, use `ConfigureAwait(false)`), and must not
  access Controls directly.
- UI interaction is explicit. Use the established WinForms synchronization
  context or a future supported dispatcher after it is available. Post work
  asynchronously; do not use synchronous cross-thread marshaling or wait on
  the UI thread from a hosted service. The WinForms runtime does not implicitly
  marshal arbitrary service callbacks.
- On host-initiated stop, marshal the request to the owning UI thread. On
  window/application exit, allow the message loop to continue pumping until
  asynchronous host shutdown and cleanup finish, then unwind it once. These
  constraints reuse the existing [lifetime decisions](lifetime-architecture.md).

### Health checks

- Reuse `IHealthCheck`, `AddHealthChecks`, `HealthCheckService`, and
  `HealthReport` from `Microsoft.Extensions.Diagnostics.HealthChecks`.
- Registration is opt-in; execution is explicit or initiated by a
  caller-selected background monitor. Do not run checks on the UI thread, add
  periodic polling automatically, or show modal UI for a failed check.
- There is no HTTP listener or endpoint in a desktop application. Applications
  may log, display, or export a report through an explicitly selected
  integration. A failed health check is diagnostic state, not an implicit
  application shutdown command.
- Checks must support cancellation and timeouts and avoid blocking the message
  loop. External dependency checks must be optional so offline desktop
  applications can still start and operate where appropriate.

Registration, execution, failure reporting, UI marshaling guidance, and
graceful service teardown are implemented and tested by #14960.

## Alternatives considered

- **Replace Generic Host with a WinForms-owned DI/configuration/logging stack:**
  rejected. It would duplicate mature Microsoft.Extensions providers and
  lifetime semantics and make third-party integration harder.
- **Use `IConfiguration` as the user-settings write API:** rejected.
  Configuration is a read-oriented projection; user settings need explicit
  per-user persistence, transactional upgrades, and data recovery. A
  read-only provider bridge remains available when applications opt in.
- **Use `Settings.settings` as the new primary store:** rejected. It remains a
  supported optional migration source, not the versioned JSON service
  requested by the proposal.
- **Add a WinForms HTTP health endpoint or automatic health polling:** rejected.
  These are server assumptions with no desktop listener or universal
  monitoring consumer.
- **Automatically install file/telemetry logging or global exception handlers:**
  rejected. Destinations, retention, privacy, exception handling policy, and
  consent are application decisions.
- **Use a dedicated UI thread or custom message pump for hosted services:**
  rejected. WinForms continues to own the caller-selected UI thread and its
  existing `Application.Run`/modal-loop behavior.

## Scope and follow-up ownership

This issue completes the architecture decisions only; no public API,
configuration provider, settings implementation, logger, exception hook,
hosted-service adapter, or health-check runner is introduced here.

| Issue | Follow-up ownership |
| --- | --- |
| #14957 | Implement approved configuration defaults, content-root/environment behavior, provider extension points, options binding, pre-UI validation, reload behavior, actionable startup failures, and precedence tests. |
| #14958 | Implement `IUserSettingsService`, versioned JSON, concurrency/recovery, upgrades, change notifications, and optional `Settings.settings` migration. |
| #14959 | Integrate logging and opt-in exception forwarding, provider shutdown/flush guidance, and privacy-safe diagnostics. |
| #14960 | Integrate hosted services and health checks with UI-safe lifecycle, cancellation, shutdown, and explicit reporting. |
| #14961 | Add deterministic tests for the preceding implementations and their failure paths. |
| #14962 | Add C# and Visual Basic reference samples after the APIs stabilize. |
| #14963 | Benchmark only after the integration is implemented and stable. |

## Sources reviewed

- Proposal inputs supplied for this feature: `Builder_Requirement.docx`,
  `BuilderAPI_ModernizationGoals.docx`, `BuilderAPI_Proposal.docx`, and
  `BuilderAPI_Rationale.docx`.
- WinForms issues [#14955](https://github.com/dotnet/winforms/issues/14955)
  and [#14956](https://github.com/dotnet/winforms/issues/14956).
- Repository decisions: [lifetime architecture](lifetime-architecture.md),
  [core contracts](core-contracts.md), and [DI/scope architecture](di-scopes-architecture.md).
- [.NET Generic Host](https://learn.microsoft.com/dotnet/core/extensions/generic-host)
- [.NET configuration](https://learn.microsoft.com/dotnet/core/extensions/configuration)
- [.NET options pattern](https://learn.microsoft.com/dotnet/core/extensions/options)
- [.NET logging](https://learn.microsoft.com/dotnet/core/extensions/logging/overview)
- [.NET Worker Services](https://learn.microsoft.com/dotnet/core/extensions/workers)
- [Health checks in ASP.NET Core](https://learn.microsoft.com/aspnet/core/host-and-deploy/health-checks)
- [Generic Host in WPF](https://learn.microsoft.com/dotnet/desktop/wpf/app-development/how-to-use-host-builder)
- [.NET MAUI dependency injection](https://learn.microsoft.com/dotnet/maui/fundamentals/dependency-injection)
- [.NET MAUI app lifecycle](https://learn.microsoft.com/dotnet/maui/fundamentals/app-lifecycle)
