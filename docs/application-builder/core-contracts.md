# WinForms Application Builder core contracts

**Status:** Contract prototype for issue [#14942](https://github.com/dotnet/winforms/issues/14942)

**Architecture decisions:** [lifetime architecture](lifetime-architecture.md)

**DI and scope research:** [Designer-safe DI and UI scopes](di-scopes-architecture.md)

**Service infrastructure architecture:**
[service boundaries and defaults](service-infrastructure-architecture.md)

**DI contract prototype:** [runtime factory contract](di-contract-prototype.md)

**Parent proposal:** [#14082](https://github.com/dotnet/winforms/issues/14082)

## Contract boundary

The builder places `WinFormsApplicationBuilder`, `WinFormsApplication`,
`WinFormsApplicationLifetime`, and the internal `WinFormsApplicationOptions`
in the `Microsoft.Extensions.WinForms` namespace in `System.Windows.Forms.dll`.
This follows the proposal's single-assembly option. `WinFormsApplicationBuilder`
implements `IHostApplicationBuilder` and delegates configuration, service
registration, logging, metrics, and host creation to
`Microsoft.Extensions.Hosting`. Runtime coordination continues to own the
WinForms message loop and coordinate the built host's lifetime.

The builder supports selecting a form by type or instance, or selecting a
default or supplied `ApplicationContext`. The last startup-selection call wins.
Generic form selection stores a factory; it does not instantiate a control
during builder creation, `Build`, or option copying. `Build` snapshots the
builder's options so subsequent builder changes do not alter an already-built
application.

The options type is internal. Applications continue to own their generated
`ApplicationConfiguration.Initialize()` call; the builder does not attempt to
reference application-specific generated code.

## Configuration and options (#14957)

The parameterless `CreateBuilder()` preserves the hostless path: it does not
create a host unless a host-building surface such as `Configuration`,
`Services`, or `Environment` is accessed. `CreateBuilder(args)` and
`CreateBuilder(HostApplicationBuilderSettings)` explicitly opt into a
builder-owned Generic Host. The default content root is
`AppContext.BaseDirectory`; the settings overload can choose another content
root or environment. Standard Generic Host defaults provide optional base and
environment-specific JSON files, Development-only User Secrets when an
application User Secrets ID exists, environment variables, command-line
arguments, and reload support. Providers appended to `Configuration` have
higher precedence than the defaults.

Options bind and validate through the standard Microsoft.Extensions.Options
extensions. `ValidateOnStart()` runs when the host starts, before
`Application.Run` enters the WinForms message loop; the original validation
exception is propagated to the caller. A supplied, already-built host via
`UseHost` is an alternative composition mode: host configuration and service
registration cannot be combined with it, and the builder reports that
conflict rather than silently ignoring either host.

```csharp
WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder(args);
builder.Services.AddOptions<WindowOptions>()
    .Bind(builder.Configuration.GetSection("Window"))
    .ValidateOnStart();
builder.UseStartupForm<MainForm>();

using WinFormsApplication application = builder.Build();
application.Run();
```

## User settings (#14958)

`WinFormsApplicationBuilder.AddUserSettings()` registers the default
`IUserSettingsService`; pass `UserSettingsOptions` to customize its JSON store,
or pass an `IUserSettingsService` implementation to use a custom store.
By default, the file is stored under the current user's Local Application Data
directory in an application-specific `WinForms` folder, derived from the entry
assembly name. Set `ApplicationId` for a stable product identity or `FilePath`
for an explicit store. The JSON document contains a `schemaVersion` and a
settings object.

`LoadAsync(defaults)` returns the supplied defaults without creating a file
when no store exists. `SaveAsync` and `ResetAsync(defaults)` commit versioned
JSON atomically while retaining a `.bak` of the previous valid file.
`UpgradeAsync` applies ordered `IUserSettingsMigration` implementations,
one schema version at a time. Concurrent processes coordinate through a
per-file lock with a bounded wait. Corrupt primary data is retained as a
timestamped `.corrupt-*` file and recovered from `.bak` when possible; a
valid backup also restores a missing primary file. Operations reject a newer
schema instead of downgrading it, and invalid stores report an actionable
error rather than silently replacing user data with defaults.

Legacy migration is explicitly invoked with
`MigrateFromLegacySettingsAsync(ApplicationSettingsBase)`. It imports only
user-scoped values, does not remove the generated settings store, and does not
run automatically. The `Changed` event is raised after a successful
save/reset/upgrade/migration and includes an immutable settings snapshot.
Configuration projection is opt-in:
`AddUserSettings(settingsService, includeInConfiguration: true,
settingKeys: ["Theme"])` exposes only selected values under `UserSettings` and
publishes configuration reload notifications after committed changes. Do not
store secrets in settings or project sensitive preferences to configuration.

## Logging and exception integration (#14959)

`WinFormsApplicationBuilder` exposes the Generic Host `ILoggingBuilder`, and
the host provides the standard `ILogger<T>` services to application services
and Forms. Configure providers and filters through `builder.Logging`; no file
or telemetry provider is added automatically. Applications may add the Debug
provider when its package is referenced, or choose a file provider explicitly.
For file logging, use an established provider and configure a per-user location,
restrictive file permissions, rotation, and retention. Exception messages and
stack traces can contain application data, so provider access and retention
remain the application's responsibility.

Call `builder.EnableExceptionLogging()` to opt in to routing
`Application.ThreadException`, `AppDomain.UnhandledException`, and
`TaskScheduler.UnobservedTaskException` to `ILogger<WinFormsApplication>`.
When enabled, a UI-thread exception is logged and treated as handled; WinForms'
default exception dialog is not shown. Unobserved task exceptions are logged
without calling `SetObserved`, and terminating CLR exceptions are logged as
critical without changing process termination. The integration is disabled by
default; applications that require a user-facing UI exception flow should
install their own `Application.ThreadException` handler.

Startup/runtime failures are logged when exception logging is enabled and are
still rethrown after cleanup. The host is stopped and disposed after the
message loop exits, releasing host-owned logging providers so their normal
`Dispose` flush behavior runs; disposing an application before `Run` also
releases the host. Providers supplied as preconstructed instances remain the
caller's responsibility when their registration does not transfer ownership.
Provider-specific asynchronous flushing is not guaranteed by `ILogger` or
`IHost.Dispose`.

## Lifetime contract

The application exposes one lifetime object with `ApplicationStarted`,
`ApplicationStopping`, and `ApplicationStopped` events. Internal notification
methods are one-shot and retain the required ordering; a stop after failed
startup does not synthesize an `ApplicationStarted` notification. Event-handler
exceptions propagate to the runtime coordinator, which must preserve cleanup
and failure semantics when it is implemented.

When a host is configured, it starts before the message loop and is stopped
when the loop exits. The application owns the host and disposes it after
shutdown; disposing the application before `Run` also releases the host.

## Hosted services and health diagnostics (#14960)

Register ordinary `IHostedService` and `BackgroundService` implementations
through `builder.Services`; the Generic Host controls their startup, reverse
order shutdown, and cancellation. Startup callbacks run off the UI thread and
must return promptly. Long-running work belongs after the first incomplete
`await` in `BackgroundService.ExecuteAsync`, and must observe its stopping
token. Shutdown remains asynchronous while the WinForms message loop pumps
messages. Do not block the UI thread waiting for service work or access
controls from a hosted service.

For explicit UI interaction, capture the WinForms synchronization context after
it is installed and post work asynchronously; do not use synchronous
cross-thread marshaling or wait for the UI thread from a service. These APIs
do not install a second UI thread, create a custom message pump, or marshal
arbitrary service callbacks automatically.

Health checks are optional: reference
`Microsoft.Extensions.Diagnostics.HealthChecks`, register checks through
`builder.Services.AddHealthChecks()`, and resolve `HealthCheckService` from
the host to call `CheckHealthAsync` where the application chooses. No checks
are polled automatically, no HTTP endpoint is opened, and a failed report
does not stop the application or show modal UI. Pass cancellation tokens to
checks and run potentially blocking checks away from the UI thread. The
application may log, display, or export reports through an explicitly chosen
integration.

## Deferred to issue #14943

The runtime added by #14943 runs on the calling UI thread, installs the
WinForms synchronization context before creating a deferred startup form, and
uses the existing `Application.Run(ApplicationContext)` message loop. A
configured `IHost` is started before the WinForms started notification and is
stopped before an intercepted thread exit is allowed to unwind the loop. The
application owns the host passed to `UseHost`, stopping and disposing it after
the message loop exits. Host-originated
stopping notifications are marshalled to the UI thread while the coordinator
is active.

Application-context exit deferral is an internal hook used to keep the message
pump responsive while asynchronous host stop callbacks finish. Ordinary
WinForms contexts without a configured host retain their existing synchronous
exit behavior. A host stop is terminal: after the host has begun stopping, an
application shutdown request is not canceled by a form-close veto.

The lifetime bridge introduced by #14943 continues to accept an already-built
`IHost`; #14957 also lets the application builder create and own the host
through `IHostApplicationBuilder`. Existing C# and Visual Basic examples using
an externally built host remain in [samples](samples/README.md). A standalone
lifecycle and resource benchmark harness is documented in
[benchmarks](benchmarks/README.md).

## Scope-aware Form activation

Issue #14950 adds a service-aware startup Form factory and
`WinFormsApplication.CreateForm` / `ShowDialog` activation methods. Each
activated Form uses a host-created service scope when a Generic Host is
configured; without a host, the factory receives an empty service provider.
The Form's `Disposed` event ends its scope, so a canceled close keeps scoped
services alive. Modal activation disposes the dialog and scope before
returning, including when display or disposal throws. When the application
message loop exits, it disposes any activated modeless Forms that remain open.

UserControls and components remain Designer-constructed. A Form factory can
explicitly assign dependencies to these descendants; the framework does not
inject an ambient provider or create descendant scopes. Automatic Designer
integration remains assigned to #14951. The new API signatures are unshipped
and require API review before release.

## Alternatives considered

- **A separate hosting assembly:** deferred. The proposal recommends the
  single-assembly option for the core types, and this prototype has no
  independent package dependency that would justify a second assembly.
- **Implementing a message loop or a host adapter here:** rejected. WinForms
  already owns message-loop behavior, and implementing runtime coordination
  here would overlap #14943.
- **Adding dependency-injection or application-configuration APIs now:**
  rejected. Those APIs and application-specific initialization semantics are
  outside the minimal contract prototype.
