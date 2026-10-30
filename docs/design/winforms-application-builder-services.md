# WinForms Application Builder: service infrastructure boundaries

Status: architecture recommendation for
[dotnet/winforms#14956](https://github.com/dotnet/winforms/issues/14956),
pending API and architecture review. This issue documents decisions, not implemented services.

Parent: [dotnet/winforms#14955](https://github.com/dotnet/winforms/issues/14955).
Proposal: [dotnet/winforms#14082](https://github.com/dotnet/winforms/issues/14082).
Dependencies: the [host/lifetime contract](winforms-application-builder-lifetime.md)
and [DI/UI-scope contract](winforms-application-builder-di-scopes.md).

## Scope and recommendation

Reuse Microsoft.Extensions configuration, options, logging, hosting, and health-check contracts.
WinForms owns desktop composition defaults, a replaceable user-settings service, opt-in exception
routing, and the connection to the existing UI lifetime. Do not build another container, binder,
logger factory, background-service framework, message pump, or global service locator.

Infrastructure is opt-in. The parameterless, hostless builder and applications using legacy
`Application.Run` retain their existing behavior. No configuration files, settings directories,
file watchers, health polling, or exception subscriptions are created merely by constructing a
service-free application. Designer construction does not start a host or load runtime settings.

This stage adds no public APIs, package references, runtime behavior, tests, samples, or benchmarks.
The decisions below are requirements for the owning child issues, not claims that those issues or
the parent feature are complete.

## Research baseline and current integration seam

The local baseline is `9cb0888c7`, including the host/lifetime and DI/scopes streams.
[Directory.Packages.props](../../Directory.Packages.props) selects Hosting 10.0.10 for tests.
The product currently uses framework-provided hosting/DI abstractions; a full provider implementation
is not automatically a justified new dependency of `System.Windows.Forms.dll`.

- [WinFormsApplicationBuilder.cs](../../src/System.Windows.Forms/Microsoft/Extensions/WinForms/WinFormsApplicationBuilder.cs),
  `UseHost` and `Build`: accepts an **unbuilt `IHostBuilder`**, consumes it once, and builds the
  coordinated host without creating deferred startup UI. A supplied startup Form transfers
  ownership only after successful build.
- [WinFormsApplicationHost.cs](../../src/System.Windows.Forms/Microsoft/Extensions/WinForms/WinFormsApplicationHost.cs),
  `Build`, `StartAsync`, and `StopCoreAsync`: decorates the standard singleton `IHost` factory,
  exposes the same adapter through DI, and serializes start/stop operations. Stop requests signal
  the real `IHostApplicationLifetime` and await UI completion before the underlying host stops.
- [WinFormsApplication.cs](../../src/System.Windows.Forms/Microsoft/Extensions/WinForms/WinFormsApplication.cs),
  `Run` and `CoordinateHostAsync`: starts the host off the UI thread **before** installing the
  WinForms synchronization context and activating deferred startup UI. UI resources and scopes
  are cleaned up before host teardown and provider disposal.
- [WinFormsHostStopBarrier.cs](../../src/System.Windows.Forms/Microsoft/Extensions/WinForms/WinFormsHostStopBarrier.cs):
  waits for UI completion, not total run completion, and does not release that guarantee on
  shutdown cancellation. It is not a new extension point owned by this stream.
- [WinFormsUiScope.cs](../../src/System.Windows.Forms/Microsoft/Extensions/WinForms/WinFormsUiScope.cs):
  borrows the host root and owns application/Form scopes. The internal prototype requires
  synchronous service disposal and base-last Form disposal. Infrastructure must not turn these
  internal APIs into an implied approved public activation contract.

Preserve `UseHost(IHostBuilder)` as the supported composition seam. Prefer opt-in public extensions
on `IHostBuilder`, `IServiceCollection`, `IConfigurationBuilder`, and `ILoggingBuilder` for this
stream. Exact names, signatures, assembly placement, and provider-package dependencies need review
in the relevant implementation issue. Third-party providers must not need WinForms internals.

`HostApplicationBuilder`/`IHostApplicationBuilder` offer a useful property-based configuration
experience, but `HostApplicationBuilder` does not publicly implement `IHostBuilder`; its adapter is
internal in the inspected Hosting source. Do not use reflection to obtain it, accept an already-built
host that bypasses coordination, or build a second provider to expose those properties. A future
property-based WinForms builder must first reconcile the existing host-factory/barrier integration
with the lifetime owners and be separately API-reviewed. It is not required for initial provider
integration.

## Ownership and extension boundaries

- **Configuration:** Generic Host owns its configuration root, providers, reload registrations, and
  disposal. WinForms supplies explicit desktop defaults where selected, not a second root. Consumers
  read effective configuration; persistence is not performed through the `IConfiguration` indexer.
- **Options:** Microsoft.Extensions.Options owns binding, named options, caches, validation, and change
  tokens. Host startup owns eager validation. Forms borrow options through the existing typed
  activation boundary; this stream does not create UI scopes.
- **Logging:** the host owns its `ILoggerFactory` and container-created providers. Consumers borrow
  `ILogger<T>` or create an `ILogger` through the factory; do not assume an unqualified `ILogger`
  registration exists. Caller-supplied instances retain the container's documented ownership rules.
- **Hosted services:** Generic Host owns `IHostedService`, `BackgroundService`, lifecycle callbacks,
  cancellation, start/stop order, and failure policy. WinForms retains the existing host adapter and
  UI-completion barrier rather than registering another application driver.
- **Health diagnostics:** standard health-check services own registration and execution. Applications
  select when to request or display reports. A health report does not own application lifetime.
- **User settings:** the proposed `IUserSettingsService` is a replaceable per-application singleton
  for the current user's store, not a process-wide static. The host owns factory-created instances;
  Form consumers borrow them. Store operations have explicit asynchronous completion and errors.
- **UI:** the caller owns its STA thread; the WinForms runner owns its message loop, dispatcher,
  context, and tracked UI cleanup. No infrastructure service may dispose another owner's UI,
  borrowed dependencies, or the host root.

## Patterns adopted from other workloads

- **Generic Host / Worker Service:** reuse service composition, environment identity, configuration,
  logging, options, lifecycle services, cooperative cancellation, and host disposal. Do not call
  `Host.Run`/`RunAsync` as a second driver alongside `WinFormsApplication.Run`.
- **ASP.NET Core:** reuse providers, options validation, `IHealthCheck`, and structured diagnostics.
  Do not import middleware, HTTP listeners, web roots, request scopes, or readiness endpoints into
  a desktop process. A Form lifetime, not a web request, is the relevant scoped UI boundary.
- **WPF hosting:** the Microsoft example composes a Generic Host, starts it before showing a resolved
  window, and stops/disposes it at application exit. Adopt composition and ownership, not WPF's
  `Application.Startup`, `Exit`, `Dispatcher`, or `StartupUri`. Copying its `async void` exit handler
  would not make WinForms wait for asynchronous shutdown; the existing coordinator must own that wait.
- **MAUI hosting:** adopt Microsoft.Extensions registration and typed dependency supply. Its window
  activation, deactivation, stopped/resume, and platform-specific lifecycle are not equivalent to
  WinForms root-loop completion. Do not infer Form scopes or Generic Host shutdown from MAUI's
  builder shape or add suspend/resume behavior here.

## Configuration and environment

### Construction and desktop defaults

The opt-in desktop-default composition should use an unbuilt Generic Host and standard providers.
Applications that supply a custom host builder keep their selected defaults and provider order;
`UseHost` alone must not rewrite them.

For the desktop-default path, select `AppContext.BaseDirectory` as the default content root before
loading JSON. Shell shortcuts, Terminal Services, and installers can supply an unrelated current
directory. Allow an explicit content-root override and honor it. Copy configuration files to build
and publish output; do not write into the installation directory. Packaged deployments require
validation of their actual application directory and per-user writable store.

Use `IHostEnvironment` as the only environment identity, with Production as the default. Reuse
`DOTNET_` host configuration and command-line host settings; explicitly configured host settings
applied after defaults take precedence. `ASPNETCORE_ENVIRONMENT` is not a WinForms-specific selector.
Resolve environment and content root before choosing environment-specific providers; changing an
application configuration key later does not reconstruct the hosting environment.

Optional missing JSON files are allowed. Malformed files, inaccessible required sources, invalid
arguments, and provider setup failures must surface with their original diagnostics, not be treated
as missing files. JSON reload uses the provider's standard change tokens and its supported opt-out;
WinForms adds no polling loop. A provider without reload support remains non-reloadable.

### Default precedence

Lowest to highest, with the last provider containing a key winning:

1. Host configuration carried into application configuration as fallback.
2. `appsettings.json`.
3. `appsettings.{Environment}.json`.
4. `{ApplicationName}.settings.json`, when added by the selected Generic Host version.
5. `{ApplicationName}.settings.{Environment}.json`, under the same condition.
6. Development User Secrets, when the application assembly has a User Secrets ID.
7. Unprefixed application environment variables; use the standard `__` hierarchy separator.
8. Command-line application arguments.
9. Additional providers deliberately appended after these defaults.

The inspected Hosting 10.0.10 `ApplyDefaultAppConfiguration` includes the application-name JSON
pair after the appsettings pair. These are deployment configuration, **not** generated
`Settings.settings`, mutable user preferences, or a new WinForms persistence store. Preserve the
selected host's actual defaults and test the complete source order in dotnet/winforms#14957;
recheck version-dependent sources when the hosting package changes.

Host configuration is a separate phase. The standard `DOTNET_` environment source and host
command-line source establish environment/application/content-root settings before application
configuration; later application providers must not be used to silently reselect that phase.

Additional providers use public `IConfigurationBuilder` APIs. Appending intentionally makes them
highest priority; applications requiring command-line values to win must insert a provider before
that source. The desktop-default extension must not clear, duplicate, or unexpectedly reorder
application-added sources.

Mutable user settings do not participate in this default precedence. No built-in settings-backed
configuration provider is required for the initial settings service. An application can select an
additional read-only projection provider and its explicit position, but that is not permission to
write through configuration or flatten private preferences into it automatically. Any such provider
must preserve selected-key boundaries, including array paths, and never publish stale snapshots.

## Options and startup validation

- Use `IOptions<T>` for cached default options; `IOptionsMonitor<T>` for current/named values and
  change notifications; and `IOptionsSnapshot<T>` only inside a real DI scope. A Form/dialog scope
  can hold a snapshot; a singleton hosted service cannot capture a scoped snapshot.
- Reuse binding, `IValidateOptions<T>`, data-annotation validation when selected, named options,
  and `ValidateOnStart`/`AddOptionsWithValidateOnStart`. Options required for startup must be
  explicitly registered for eager validation; do not promise every lazy options registration is
  validated automatically.
- Provider construction/binding errors may occur during build. Eager validation runs in the real
  host's `StartAsync` before the WinForms runner invokes a deferred startup Form/context factory,
  installs its dispatcher, or enters the message loop. Do not run validators twice.
- The Generic Host resolves hosted-service instances before calling its startup validator in the
  inspected source. Their constructors, factories, and validators must be UI-free and must not wait
  for UI dispatch. A caller that pre-constructs its startup Form cannot expect validation to precede
  that caller-owned construction; validation still precedes runner-driven startup and display.
- Preserve `OptionsValidationException`, provider/parse exceptions, and the existing runner's
  cleanup aggregation. Identify options name/type, section/property, environment, source, and phase
  where available without including values. Do not mask the primary failure with logging/cleanup
  failure, substitute defaults, or display an unsolicited modal error dialog.
- Failed startup prevents deferred UI activation and completes owned host cleanup. Error
  presentation and process exit-code selection remain the application's entry-point policy.
- Reload is not startup validation again. The standard `OptionsMonitor<T>` removes its cached
  value before creating/validating a replacement; invalid reload can throw and does **not** promise
  last-known-good retention. Do not publish an invalid options instance or silently claim recovery.
  An application needing last-valid state must explicitly retain an accepted snapshot and observe
  validation errors. The monitor callback has no UI-thread guarantee; UI consumers marshal changes
  asynchronously and dispose subscriptions before their owning UI is destroyed.

## User settings model

`IUserSettingsService` is a proposed contract name, not an API approved by this document. It must
support load, save, reset, upgrade, schema-version inspection, optional migration, and committed
change notifications, with cancellation and actionable errors. Prefer one JSON store and explicit
models over a competing configuration system.

- **Location and identity:** default to an application-specific directory under the current user's
  Local Application Data folder. Use a stable product identifier across executable upgrades and
  allow an explicit store/path override. Do not derive the only copy's location from the binary
  version or current working directory. No roaming/network store, encryption, or secret-storage
  guarantee is implied.
- **Versioning:** persist a schema version with the JSON settings document, independently of
  assembly/marketing versions. Apply explicit sequential migrations; reject unknown newer schemas
  without overwriting them. Missing migrations and ambiguous chains are errors.
- **Load:** a genuinely missing store may return caller-selected defaults. Invalid JSON, invalid
  document structure, or failure to deserialize/validate the requested model is not a missing file.
  Report the failure; never silently reset user data.
- **Save and reset:** serialize/validate before replacing durable data. Write a temporary file in
  the same directory and atomically commit it, retaining the previous valid document as recovery
  data. Reset is an explicit write of defaults, not automatic corruption handling.
- **Upgrade:** transform a copy, deserialize/validate the resulting model where applicable, and
  commit the new schema only after success. A migration or typed-load failure must leave the
  primary file and its schema unchanged. Do not mark an unreadable migration successful.
- **Concurrency:** serialize competing operations and coordinate processes that target the same
  profile/store. Use bounded, cancellable contention waits and surface permission, read-only,
  timeout, and I/O errors distinctly. Complete-document atomic writes are required; automatic
  merging of separate read-modify-write edits is not promised.
- **Recovery:** preserve corrupt input for diagnosis. Restore a validated compatible backup when
  available and report that recovery occurred; otherwise fail without replacing either input.
  A recovery copy is not valid merely because its JSON parses. Keep cancellation and commit
  boundaries explicit: cancellation before commit must not publish a successful change.
- **Notifications:** publish immutable committed snapshots in revision order within an instance;
  observers must not regress to an older value after concurrent or reentrant writes. Do not invoke
  subscribers while holding storage locks or require a subscriber to finish before a reentrant
  operation can commit. Subscriber failure must be distinguishable from persistence failure after
  commit. No UI-thread, file-watcher, or cross-process notification guarantee is implicit.
- **Legacy migration:** importing user-scoped generated `Settings.settings` values is explicit and
  optional. Keep the original store, exclude application-scoped values, and do not overwrite an
  existing modern store on repeated import. Applications choose whether to invoke legacy upgrade
  before import; never force existing applications to migrate.

Do not perform synchronous disk I/O or wait on settings tasks from a UI event handler. Applications
must track outstanding saves and finish or explicitly cancel them before committing application
shutdown. An untracked `async void` handler is not a lifetime participant. The reference samples must
demonstrate this without adding a second host/loop or relying on a closing process to finish writes.

## Logging and exception boundaries

Reuse standard `ILogger<T>`, `ILoggerFactory`, filters, structured templates, scopes, and provider
extensions. Debug and Console providers are supported through ordinary registration. File logging
uses an established provider with application-selected path, permissions, rotation, retention, and
failure policy; do not invent a WinForms file logger or assume the installation directory is writable.

An application explicitly selecting `Host.CreateDefaultBuilder` also selects its standard providers:
Console, Debug, EventSource, and Windows EventLog in the inspected version. They are not all
non-persistent. Document how to clear/select providers for privacy-sensitive deployments. The
WinForms layer adds no file/network/telemetry provider, remote exporter, exception collection,
or settings/command-line dumps automatically.

Exception integration belongs to dotnet/winforms#14959 and is disabled by default:

- `Application.ThreadException` is associated with the UI thread's context. Subscribing replaces
  the default no-handler exception presentation in
  [Application.ThreadContext.cs](../../src/System.Windows.Forms/System/Windows/Forms/Application.ThreadContext.cs),
  `OnThreadException`. A logging adapter therefore needs an explicit documented handling policy;
  it cannot claim to be a behavior-neutral observer. Do not change `UnhandledExceptionMode`
  implicitly or silently suppress an application's existing error UI.
- Install/remove a UI-thread handler on its owning thread for the opted-in run only. CLR
  `AppDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException` handlers are process-wide;
  manage their lifetime explicitly, including overlapping application runs and repeated start/stop.
- Treat `IsTerminating` as authoritative, do not claim recovery from terminating exceptions, and
  do not call `SetObserved` for tasks automatically. Do not synchronously marshal a failing
  background thread to UI, create a modal error dialog, or recursively throw a logger failure
  from an unhandled-exception callback. Provider failure needs an explicit diagnostic fallback.
- Startup/runner failures still propagate through the existing runner contract. Generic Host logs
  and applies its `BackgroundServiceExceptionBehavior`; logging a worker failure does not imply
  the exception is rethrown from `Run` or that a failed worker restarts.
- Exception details can contain personal/application data. Forwarding them is an explicit opt-in;
  default infrastructure messages contain source/category/phase, not credentials, settings
  contents, command-line values, or automatic object serialization.

Keep providers alive for startup failures, service shutdown, and disposal diagnostics. Owned
providers are released with host disposal after UI cleanup and host stop. `ILogger` has no universal
flush API: use the selected provider's documented flush/dispose semantics. Do not claim asynchronous
flush is awaited by the current synchronous host-disposal path or promise delivery after forced
process termination. Caller-owned provider instances remain caller-owned.

## Hosted services and health diagnostics

### Startup, cancellation, and shutdown

Register ordinary `IHostedService`, `IHostedLifecycleService`, and `BackgroundService` implementations
through the host's public service collection. The real host controls ordering/concurrency and
`HostOptions`. Required startup readiness belongs to `StartAsync`/startup validation, must complete
before UI activation, and must not wait for a dispatcher that has not been installed.

In the selected .NET 10 hosting implementation, **all** of `BackgroundService.ExecuteAsync` runs
through `Task.Run`, unlike earlier releases. It is not a synchronous readiness gate. Constructors
and an overridden `StartAsync` can still block startup. Do not add a competing scheduler or assume
arbitrary hosted-service code is automatically UI-safe.

Preserve the existing ordering:

```text
build configured host without deferred UI
host startup and eager validation off the UI thread
install WinForms synchronization context -> activate startup UI -> run existing message loop
committed stop -> notify host stopping -> exit/clean owned UI and UI scopes
signal UI completion -> stop underlying host services -> host stopped
WinForms stopped -> dispose owned host/providers -> return or propagate failures
```

Services must not assume Forms remain available inside their host `StopAsync`: the current adapter
waits for UI completion first. Workers that post UI updates must stop posting on
`IHostApplicationLifetime.ApplicationStopping`, not wait only for the `BackgroundService` stopping
token (canceled during later service stop). UI-owned pending saves or checks must be joined/canceled
before their owner is disposed; host shutdown does not track arbitrary event-handler tasks.

Reuse cooperative service cancellation and the Generic Host timeout. A token passed to the exposed
host's `StopAsync` cancels that caller's wait, not shared cleanup. The existing UI barrier deliberately
does not complete on timeout/cancellation, so a non-cooperative UI can delay shutdown indefinitely.
Do not report completed shutdown, dispose providers early, or change that stream-A contract here.
Committed host stop cannot be undone by a Form close veto; complete user confirmation before it.

### UI interaction

Background services receive typed dependencies, not Controls or an ambient provider. Capture/use
the owning WinForms synchronization context only after it exists, and post work asynchronously.
Do not call `Send`, synchronously wait for a UI post from startup/stop, or assume host notifications
execute on the STA. Code posted during active dispatch must recheck its UI owner's lifetime.
Race-safe dispatcher acquisition/shutdown belongs to the existing runtime; extending it requires
agreement with its owner, not a parallel dispatcher in this stream.

### Health checks

Reuse `AddHealthChecks`, `IHealthCheck`, `HealthCheckService.CheckHealthAsync`, `HealthReport`, and
Healthy/Degraded/Unhealthy status. Registration and execution are opt-in. Applications may show,
log, or export a report; WinForms adds no HTTP endpoint, timer, publisher, automatic network probe,
or shutdown-on-unhealthy policy.

Run potentially blocking checks away from the UI thread. Pass caller/lifetime cancellation and
configure check timeouts; do not promise a timeout forcibly interrupts non-cooperative code.
Distinguish a reported dependency failure from cancellation, and surface useful diagnostics without
logging report data/secrets automatically. Readiness checks required to start must be explicitly
selected as startup work, not inferred from every registered check. Offline dependency failure may
be diagnostic/degraded state rather than preventing a local desktop application from starting.

## Rejected alternatives and trade-offs

- **Implement `IHostApplicationBuilder` immediately or accept any prebuilt host:** attractive API
  symmetry, but bypasses the selected unbuilt-host factory/barrier contract. Retain the public
  composition seam; review any new adapter with lifetime owners first.
- **Duplicate Microsoft.Extensions implementations:** unnecessary container/binder/logger/worker
  code increases compatibility and startup cost. Add only desktop-specific policy and settings.
- **Write settings through configuration or deployment JSON:** loses persistence/recovery boundaries
  and fails in read-only deployments. Keep one explicit mutable settings owner.
- **Automatic service/property injection or Designer host activation:** violates the DI/Designer
  contract and expands this stream into tooling work. Keep typed, explicit runtime composition.
- **Automatically catch/log all exceptions or add a file/telemetry destination:** changes UI policy,
  privacy, and I/O behavior. Exception routing and additional destinations are explicit opt-ins.
- **Copy WPF async exit or MAUI suspend/resume:** neither replaces the existing deterministic
  WinForms UI-completion protocol.
- **HTTP health endpoints, mandatory network readiness, automatic settings sync:** unnecessary server
  assumptions for offline desktop apps; applications can opt into ecosystem integrations.
- **Unconditional eager infrastructure for small applications:** makes the hostless path pay for
  providers and background resources it does not use. Measure opt-in features separately.

## Child-issue ownership and validation requirements

- **dotnet/winforms#14957:** implement reviewed public configuration/options composition using the
  existing host seam. Test actual default source order, environment/content-root selection, missing
  versus malformed files, Development-only default User Secrets, explicit provider overrides,
  binding/named options, eager validation before deferred UI creation, and valid/invalid reload.
- **dotnet/winforms#14958:** implement/review the settings contract and JSON store. Test default
  per-user identity, save/load/reset, schema chains, incompatible typed migration without mutation,
  future versions, corrupt primary/backup recovery, real cross-process contention, cancellation/
  lock timeout, permissions, notification ordering/reentrancy, and optional idempotent legacy import.
- **dotnet/winforms#14959:** implement reviewed logging/exception integration and provider guidance.
  Test opt-out behavior, UI policy, process-wide subscription teardown, overlapping/repeated runs,
  provider failures, original-error preservation, structured/redacted diagnostics, and owned-provider
  shutdown/flush behavior.
- **dotnet/winforms#14960:** integrate standard hosted services and desktop health diagnostics without
  replacing the runtime. Test readiness/start failure, worker failure policy, cancellation, timeout,
  UI posting and teardown ordering, explicit health execution, unhealthy reports, and offline behavior.
- **dotnet/winforms#14961:** consolidate the preceding focused regressions and third-party extension/
  custom-container coverage; run affected suites locally and in existing remote CI architectures/
  configurations. Use deterministic coordination, not sleeps or machine-dependent timing thresholds.
- **dotnet/winforms#14962:** provide C#/VB reference samples and manual expected results, including
  tracked pending-operation shutdown, configuration changes, settings upgrade/recovery, multi-user
  isolation, offline operation, read-only installation, Terminal Services, and representative
  unpackaged/framework-dependent/self-contained or packaged deployments. Record actual outcomes.
- **dotnet/winforms#14963:** compare equivalent conventional and proposed infrastructure workloads,
  plus the hostless baseline. Measure configuration/options/settings, logging and provider flush,
  host/service startup/shutdown, health execution, allocations, working set/private/managed memory,
  threads, file/kernel/GDI/USER handles as applicable, and retention under repeated failure/recovery.
  Align timing and allocation boundaries or label their differences. Record complete run metadata
  and baseline/proposed numbers; owner-approved budgets are distinct from CI correctness.

The [existing lifetime measurements](../../src/test/integration/WinFormsApplicationLifetimePerformance/README.md)
report unresolved repeated-application component-manager resource growth and a hosted retaining root.
The new infrastructure stream neither resolves nor supersedes that evidence. Scope-turnover tests
are not a replacement for repeated complete infrastructure-enabled application lifetimes.

API/architecture agreement, packaging/dependency review, actual OOP Designer acceptance inherited
from the DI stream, remote CI, native ARM/Terminal Services/manual deployment evidence, and resource
budgets must remain explicitly open until obtained. This document does not approve any of them.
Template creation, Designer DI infrastructure, VB single-instance/splash behavior, and application-
framework parity remain outside this stream.

## Sources reviewed

Repository links above describe the current branch; external implementation links are pinned to
the selected Hosting version. Microsoft Learn pages describe broader workload patterns and are not
proof that a WinForms implementation or manual acceptance run exists.

- [Parent infrastructure story](https://github.com/dotnet/winforms/issues/14955) and
  [service boundary objective](https://github.com/dotnet/winforms/issues/14956).
- [Generic Host](https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host).
- [Hosting defaults, v10.0.10](https://github.com/dotnet/runtime/blob/v10.0.10/src/libraries/Microsoft.Extensions.Hosting/src/HostingHostBuilderExtensions.cs),
  `ApplyDefaultAppConfiguration` and `AddDefaultServices`.
- [HostApplicationBuilder, v10.0.10](https://github.com/dotnet/runtime/blob/v10.0.10/src/libraries/Microsoft.Extensions.Hosting/src/HostApplicationBuilder.cs),
  initialization, `Build`, and internal `AsHostBuilder`.
- [Host startup/shutdown, v10.0.10](https://github.com/dotnet/runtime/blob/v10.0.10/src/libraries/Microsoft.Extensions.Hosting/src/Internal/Host.cs),
  `StartAsync`, `StopAsync`, and background-service failure policy.
- [Configuration providers](https://learn.microsoft.com/en-us/dotnet/core/extensions/configuration-providers).
- [Options, binding and validation](https://learn.microsoft.com/en-us/dotnet/core/extensions/options).
- [OptionsMonitor, v10.0.10](https://github.com/dotnet/runtime/blob/v10.0.10/src/libraries/Microsoft.Extensions.Options/src/OptionsMonitor.cs),
  `InvokeChanged`.
- [.NET 10 BackgroundService scheduling](https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/10.0/backgroundservice-executeasync-task).
- [Logging providers](https://learn.microsoft.com/en-us/dotnet/core/extensions/logging/providers).
- [ASP.NET Core health checks](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0).
- [WPF Generic Host composition](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/app-development/how-to-use-host-builder).
- [MAUI dependency injection](https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/dependency-injection?view=net-maui-10.0)
  and [window lifecycle](https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/app-lifecycle?view=net-maui-10.0).
