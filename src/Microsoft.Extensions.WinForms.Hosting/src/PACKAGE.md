# Microsoft.Extensions.WinForms.Hosting

Opt-in configuration defaults, typed user settings, exception logging, and health diagnostics for the WinForms Application Builder. This is a separate application
NuGet dependency, not an addition to the WindowsDesktop shared framework. Existing WinForms
applications and the hostless Application Builder do not acquire configuration providers.

This package and its public API are prototypes tracked by
[dotnet/winforms#14957](https://github.com/dotnet/winforms/issues/14957),
[dotnet/winforms#14958](https://github.com/dotnet/winforms/issues/14958),
[dotnet/winforms#14959](https://github.com/dotnet/winforms/issues/14959), and
[dotnet/winforms#14960](https://github.com/dotnet/winforms/issues/14960), pending API/package review.
It targets the repository's current Windows framework and declares the WindowsDesktop WinForms
framework reference required by the lifetime-bound logging API. It requires the matching Application
Builder prototype in the framework/runtime, not just this package. The framework's WinForms assembly
is referenced privately for compilation; it is not bundled or added as a fictitious NuGet dependency.

## Configure an application

Call `ConfigureWinFormsConfiguration` once on a fresh `HostBuilder`, before customizations. Do not
combine it with `Host.CreateDefaultBuilder` or `ConfigureDefaults`: those already install providers.
An application-supplied host builder passed directly to `UseHost` is unchanged.

```csharp
IHostBuilder hostBuilder = new HostBuilder()
    .ConfigureWinFormsConfiguration(args)
    .ConfigureServices((context, services) =>
    {
        services.AddOptions<DisplayOptions>()
            .Bind(context.Configuration.GetSection("Display"))
            .Validate(options => options.RefreshSeconds > 0,
                "'Display:RefreshSeconds' must be greater than zero.")
            .ValidateOnStart();
    });

using WinFormsApplication application = WinFormsApplication.CreateBuilder()
    .UseStartupForm<MainForm>()
    .UseHost(hostBuilder)
    .Build();
application.Run();
```

Import `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.DependencyInjection`, and
`Microsoft.Extensions.WinForms`. `DisplayOptions` and `MainForm` are application-defined types.
The same standard APIs are available in VB:

```vb
Dim hostBuilder As IHostBuilder = New HostBuilder().
    ConfigureWinFormsConfiguration(args).
    ConfigureServices(
        Sub(context, services)
            services.AddOptions(Of DisplayOptions)().
                Bind(context.Configuration.GetSection("Display")).
                Validate(Function(options) options.RefreshSeconds > 0,
                         "'Display:RefreshSeconds' must be greater than zero.").
                ValidateOnStart()
        End Sub)
```

Pass the **unbuilt** host builder to `WinFormsApplicationBuilder.UseHost`. Do not separately call
`Build`, `Run`, or `RunAsync` on that host. The configuration extension supplies configuration only: it does not
install console lifetime hooks, logging destinations, exception handlers, UI activation, a new
container, or health polling. Configure those separately with their owning APIs. Container scope
validation is an explicit application choice through `UseDefaultServiceProvider`.

## Files, environment, and precedence

The default content root is `AppContext.BaseDirectory`, not the current working directory.
`DOTNET_` environment variables and command-line host settings override that default; a subsequent
explicit `UseContentRoot` or `UseEnvironment` wins. Production is the default environment.
`ASPNETCORE_ENVIRONMENT` is not a desktop environment selector.

Application providers are ordered from lowest to highest priority:

1. Host configuration fallback.
2. `appsettings.json`.
3. `appsettings.{Environment}.json`.
4. `{ApplicationName}.settings.json`.
5. `{ApplicationName}.settings.{Environment}.json`.
6. Development User Secrets, when the application assembly has a User Secrets ID.
7. Unprefixed environment variables (`__` separates nested keys).
8. Command-line arguments.
9. Additional providers appended using `ConfigureAppConfiguration`.

These are deployment JSON files, not mutable user settings. Copy them to build/publish output,
for example with `<None Update="appsettings*.json" CopyToOutputDirectory="PreserveNewest"
CopyToPublishDirectory="PreserveNewest" />`. Include the application-name files if used. Do not
store sensitive values in deployment JSON or write preferences to the installation directory.
User Secrets are a development convenience, not an encrypted production secret store.

Missing optional files are allowed; malformed JSON and other provider failures retain their
original exceptions and paths. Like Generic Host, default User Secrets are skipped when the
application assembly cannot be found. Malformed secrets files still fail. JSON reload uses standard
change tokens; disable file watchers with `--hostBuilder:reloadConfigOnChange=false` or the equivalent
host configuration. No provider order is cleared or rewritten. Register defaults before application
providers; deliberately appended providers override command-line keys.

## Options, validation, and reload

Use the standard `IOptions<T>`, `IOptionsMonitor<T>`, named options, `IValidateOptions<T>`, and binding
APIs. `IOptionsSnapshot<T>` is scoped: resolve it inside an actual UI/service scope, not from a
singleton. The package does not introduce wrapper options or a global service provider.

`ValidateOnStart` makes required options fail during real host startup, before deferred Form/context
construction, dispatcher installation, or the message loop. Lazy options are not all automatically
validated. Hosted-service constructors and validators must be UI-free. A caller-created Form
necessarily exists before this validation; use deferred activation when validation must precede it.

Catch startup failures at the application entry point to select error presentation and exit code.
Preserve `OptionsValidationException` and binding/provider details. Validation messages should name
the section/property and required constraint, not include sensitive values. The WinForms runner owns
cleanup of the failed startup; do not start a second host lifecycle to report the error.

`IOptionsMonitor<T>` notifications have no UI-thread guarantee. Marshal UI updates asynchronously
and dispose subscriptions before their owner is destroyed. Invalid reload throws rather than
publishing invalid options; the standard monitor evicts its old cache and does not guarantee
last-known-good retention. Keep an explicitly accepted snapshot if that policy is required.

The related [service boundary design](../../../docs/design/winforms-application-builder-services.md)
records ownership, alternatives, and the later settings/logging/health/sample/benchmark stages.

## Typed user settings (prototype, dotnet/winforms#14958)

Register one replaceable `IUserSettingsService<T>` per settings model. A stable application ID,
positive schema version, explicit JSON serialization metadata, and a fresh-defaults factory are
required. Registration and service resolution do not touch storage.

```csharp
services.AddJsonUserSettings(
    "Contoso.Editor",
    schemaVersion: 1,
    PreferencesJsonContext.Default.Preferences,
    createDefaults: () => new Preferences(),
    configure: options =>
    {
        options.Validate = preferences =>
        {
            if (preferences.FontSize < 8)
            {
                throw new InvalidDataException("'FontSize' must be at least 8.");
            }
        };
    });

public sealed class Preferences
{
    public int FontSize { get; set; } = 12;
}

[JsonSerializable(typeof(Preferences))]
public partial class PreferencesJsonContext : JsonSerializerContext
{
}
```

Import `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.WinForms`,
`System.Text.Json.Serialization`, and `System.IO`. The service requires a non-null JSON object
model. Prefer source-generated metadata: the package does not invoke a reflection-based serializer
or introduce a trimming-unsafe generic binder. VB can consume a shared C# model/context library:

```vb
services.AddJsonUserSettings(Of Preferences)(
    "Contoso.Editor", 1, PreferencesJsonContext.Default.Preferences,
    Function() New Preferences())
```

The default file is `<LocalApplicationData>\WinForms\<ApplicationId>\settings.json`, not the
installation directory, current working directory, or a version-specific executable directory.
An explicit absolute `StorageDirectory` overrides it. The default contention timeout is ten seconds;
`LockTimeout` changes it. Options and migrations are snapshotted during registration.

### Loading, saving, and resetting

```csharp
Preferences preferences = await settings.LoadAsync(cancellationToken);
preferences.FontSize = 14;
UserSettingsCommit commit = await settings.SaveAsync(preferences, cancellationToken);
await commit.NotificationCompletion;
```

Supply `settings` through the existing typed runtime composition boundary, not a static provider.
`LoadAsync` returns fresh validated defaults only when no primary or recovery backup exists.
`ResetAsync` explicitly commits fresh defaults, but does not erase corrupt, older, or newer input.
Defaults, validators, migrations, and import callbacks execute off the caller's UI thread and must
be UI-free; they must not reenter the service while its storage lock is held.

The envelope has exactly `schemaVersion`, `revision`, and `settings`. Save serializes and validates
the typed model, flushes a same-directory temporary file, and atomically replaces the primary,
keeping the previous valid document in `settings.json.bak`. A persistent `settings.json.lock` file
coordinates instances/processes using exclusive sharing; do not delete it to bypass contention.
Every operation releases its file handles. There is no file watcher, settings configuration
projection, network sync, implicit health check, or logging of settings payloads.

Cancellation is honored before atomic replacement. Once replacement succeeds, cancellation cannot
undo the durable change. Timeout, caller cancellation, parse/type/validation failures, permissions,
and unrelated I/O failures surface separately. Whole-document edits are not merged: an application
must coordinate read-modify-write semantics if lost edits are unacceptable.

### Explicit schema upgrade and recovery

Inspect `GetSchemaVersionAsync` before choosing upgrade policy. `LoadAsync`, save, and reset reject
an incompatible schema. A newer schema is never replaced. To advance from version 1 to version 2,
register schema 2 and `options.Migrations.Add(1, migrate)`, where `migrate` accepts an immutable
`JsonElement` and returns a detached JSON object for the next version. Each migration advances
exactly one version; every required step must be registered. Migrations and typed validation must
succeed before the primary changes. `UpgradeAsync` is idempotent at the current version.

Malformed JSON, invalid envelopes, and unacceptable typed data never silently reset. Explicit
`RecoverAsync` first validates a compatible backup. It preserves the damaged primary as
`settings.json.corrupt.<unique-id>`, restores the backup atomically, and returns an observable commit.
It refuses to replace an already-valid or newer primary. If the backup is absent, incompatible, or
invalid, recovery fails without replacing either input. Validators should throw
`InvalidDataException` for invalid model state so explicit recovery recognizes that rejection.
An application-defined validator failure of another type propagates rather than being presumed
to indicate corruption.

The primary, backup, and quarantine files contain plaintext preferences under the selected user's
directory. This is not encrypted secret storage or an ACL-management service. Do not store tokens,
passwords, or personal data requiring stronger protection. Custom directories and filesystem
permissions are the application's responsibility. No data is sent to a remote destination.

### Ordered change notifications

`Changed` supplies immutable JSON snapshots and increasing revisions within one service instance.
Callbacks run serially off the UI thread, after the corresponding storage locks are released.
Concurrent/reentrant saves may commit before earlier callbacks finish, but callbacks do not regress
to an older revision. An observer may initiate and await a save; it must **not** await that save's
`NotificationCompletion` from inside a change handler because delivery is serialized.

Storage success and subscriber failure are deliberately separate: `SaveAsync` returns the committed
snapshot; `NotificationCompletion` reports delivery errors after persistence has succeeded. Every
captured subscriber is attempted even if one throws. Observe notification failures without retrying
the already-committed write. UI consumers must marshal changes asynchronously and unsubscribe before
their owning UI is destroyed. No cross-process notification or UI-thread guarantee is implied.

Applications must track and await pending writes and notification tasks before final shutdown.
Do not block the UI thread or rely on untracked `async void` event handlers to finish persistence.
The service uses operation-owned file handles and short-lived notification work, not a second
application/host lifetime driver.

### Optional migration from generated settings

Existing `Properties.Settings`, `My.Settings`, generated Designer code, and legacy stores remain
unchanged and need no migration. If migration is wanted, explicitly map **only user-scoped** values:

```csharp
UserSettingsCommit? imported = await settings.ImportAsync(() => new Preferences
{
    FontSize = Properties.Settings.Default.FontSize
}, cancellationToken);
if (imported is not null)
{
    await imported.NotificationCompletion;
}
```

VB uses the same mapping with `Function() New Preferences With {.FontSize = My.Settings.FontSize}`.
The callback reads existing generated settings; it must not save, delete, or rewrite their store.
Applications choose whether to run the legacy generated `Upgrade()` operation beforehand.
Do not import application-scoped values, secrets, or every property through reflection.

`ImportAsync` holds the modern-store lock, invokes the mapping only when neither primary nor backup
exists, validates the mapped model, and commits it atomically. Repeated imports return null without
invoking the mapping or overwriting preferences. A damaged existing modern store is an error,
not permission to import again. This explicit typed mapping avoids adding a
`System.Configuration.ConfigurationManager` dependency to the optional package.

## Logging providers and explicit exception routing

Use ordinary `ILogger<T>`, `ILoggerFactory`, category filters, structured message templates, and
`BeginScope` in application services. No WinForms logger, file sink, exporter, or global provider is
introduced. `ConfigureWinFormsConfiguration` does not add destinations. For example, select Debug
explicitly with `hostBuilder.ConfigureLogging(logging => logging.ClearProviders().AddDebug())`;
Console is also an explicit standard provider choice. Debug visibility depends on attached
debugger/listener support; it is not a durable file or crash-reporting guarantee.

`Host.CreateDefaultBuilder` selects its usual providers, including Console, Debug, EventSource, and
Windows EventLog in the selected Hosting implementation. Clear/select them explicitly when using
that alternative. The privacy defaults below apply to this adapter's messages, **not** to arbitrary
application, Generic Host, or third-party logs.

`WinFormsExceptionLogging.Attach` is a separate opt-in. It requires an application UI exception
handler and an independent logging-failure fallback. Attach before `Run`, and dispose the returned
scope even if the application never runs. One undisposed scope is allowed per application.

- UI routing is installed on the owning thread at `ApplicationStarted`, after startup UI activation,
  and removed at `ApplicationStopping`, before UI cleanup. Subscribing to `Application.ThreadException`
  **replaces** both its previous handler and the built-in exception UI; this is not passive observation.
  Supply the application's complete policy, including any existing handler logic. The scope does not
  restore an unknown previous handler, show a dialog, select continue/recovery, change exception mode,
  or independently stop the application. Do not install competing UI handlers during its run.
  Startup UI paths occurring before ApplicationStarted retain the pre-existing application policy;
  configure that policy explicitly before Run when needed. LogRunFailure reports propagated errors,
  not startup exceptions that another handler has already handled.
- CLR and unobserved-task subscriptions are separate options, disabled by default. When enabled they
  begin at attachment and end at `ApplicationStopped` (before owned host disposal), or earlier disposal.
  They are process-wide: overlapping applications each receive the same notification; a thread ID
  identifies the reporting thread, not ownership of its exception by a particular application.
- The adapter preserves CLR `IsTerminating` and never calls `SetObserved`. Neither reporting path
  claims recovery, changes process termination, or restarts a worker. Task notifications may occur
  during finalization and are not a substitute for observing tasks when executing them.
- Lifecycle messages use Information; exception notifications use Error. Event IDs 1000-1006 identify
  `WinFormsDiagnosticSource` values. Structured state contains `Source`, the last observed lifetime
  `Phase`, `IsTerminating` (only meaningful for CLR notifications), and `ManagedThreadId`.
- `IncludeExceptionDetails` defaults to false: the adapter passes no exception object, exception
  message, stack trace, exception data, settings, or command-line values to the logger. Enabling it
  explicitly forwards the original exception, which can contain private information.

The logger/fallback are synchronous and must not wait for UI work or shutdown. Calls are serialized
within a scope; disposal drains an in-flight report before releasing its subscriptions. A blocked
provider can therefore delay disposal; this is not a timeout or forced-interruption mechanism.
Ordinary filter/provider failures go to `reportLoggingFailure`, not back through the failed logger.
The fallback must not throw, recursively use that logger, or automatically dump sensitive data.
If it nevertheless fails, disposal faults the separate `DiagnosticCompletion` task with both failures.
An ordinary logging/fallback failure cannot replace the original UI exception or escape a CLR/task
callback. Critical failures and forced process termination cannot provide reliable diagnostics.
Observe the completion task outside callbacks, and do not await it from inside a report.

### Factory ownership and run failures

Keep logging usable through startup, stopping, provider teardown, and failure reporting. A factory
created by the host is host-owned and is disposed after `ApplicationStopped`. If logging an exception
propagated from `Run`, create a caller-owned factory and register **that instance** with the host;
keep it alive outside the application. `LogRunFailure` explicitly reports the Run boundary after
subscriptions have ended; it neither catches nor reclassifies the original failure.

```csharp
using ILoggerFactory factory = LoggerFactory.Create(logging => logging.ClearProviders().AddDebug());
ILogger logger = factory.CreateLogger("ExampleApp");
IHostBuilder hostBuilder = new HostBuilder()
    .ConfigureWinFormsConfiguration(args)
    .ConfigureServices(services => services.AddSingleton(factory));
using WinFormsApplication application = WinFormsApplication.CreateBuilder()
    .UseStartupForm<MainForm>()
    .UseHost(hostBuilder)
    .Build();
using WinFormsExceptionLogging diagnostics = WinFormsExceptionLogging.Attach(
    application, logger, HandleUiException, ReportLoggingFailure);

ExceptionDispatchInfo? runFailure = null;
try
{
    application.Run();
}
catch (Exception exception)
{
    diagnostics.LogRunFailure(exception);
    runFailure = ExceptionDispatchInfo.Capture(exception);
}

diagnostics.Dispose();
if (diagnostics.DiagnosticCompletion.Exception is { } diagnosticFailure)
{
    if (runFailure is not null)
    {
        throw new AggregateException(runFailure.SourceException, diagnosticFailure);
    }

    throw diagnosticFailure;
}

runFailure?.Throw();
```

Import `Microsoft.Extensions.Logging` and `System.Runtime.ExceptionServices` in addition to the
configuration example's namespaces. `MainForm`, `HandleUiException`, and `ReportLoggingFailure`
are application-defined. The UI handler receives the original sender and `ThreadExceptionEventArgs`
and selects the application's response. The fallback receives `WinFormsLoggingFailure`, whose
exception is the **logging** failure, not the original UI/CLR/task exception. Error handling above
observes fallback failures after disposal and preserves any original run failure when combining them.

The same registration and opt-in switches work in VB:

```vb
Dim factory As ILoggerFactory = LoggerFactory.Create(
    Sub(logging) logging.ClearProviders().AddDebug())
Dim logger As ILogger = factory.CreateLogger("ExampleApp")
hostBuilder.ConfigureServices(Sub(services) services.AddSingleton(factory))
Dim diagnostics As WinFormsExceptionLogging = WinFormsExceptionLogging.Attach(
    application, logger, AddressOf HandleUiException, AddressOf ReportLoggingFailure,
    Sub(options)
        options.LogAppDomainExceptions = True
        options.LogUnobservedTaskExceptions = True
        options.IncludeExceptionDetails = False
    End Sub)
```

Create/register the factory before building the application. Apply the same caller ownership,
Run-failure reporting, disposal, and completion-task observation as the C# example. Do not put a
completion-task wait in a `Finally` block where its exception would replace an in-flight Run failure.

`ILogger` has no universal flush API. Dispose owned factories/providers only after the application
and diagnostic scope have finished. Caller-supplied provider instances remain caller-owned. A
provider with an asynchronous flush API needs an explicit, awaited application shutdown participant;
the existing synchronous host-disposal path does not automatically await it. Abnormal termination
can lose logs, regardless of the notification's severity or `IsTerminating` value.

### Optional file-provider guidance

Select an established provider in the application, not in this package. For example,
[`Serilog.Extensions.Logging`](https://github.com/serilog/serilog-extensions-logging) supplies
`ILoggingBuilder.AddSerilog(fileLogger, dispose: false)`, and
[`Serilog.Sinks.File`](https://github.com/serilog/serilog-sinks-file) supplies `WriteTo.File`.
Select versions compatible with the application's Microsoft.Extensions dependencies. The optional
WinForms package adds neither dependency nor a global `Serilog.Log.Logger`.

Choose an absolute, writable log directory under the application's per-user Local Application Data
folder, not the executable directory. Surface directory/permission failures; do not fall back to the
working directory. Configure `rollingInterval`, `fileSizeLimitBytes`, `rollOnFileSizeLimit`, and
`retainedFileCountLimit` deliberately. A size limit without size rolling can stop further output
until the next roll. Retention is not an encryption or access-control guarantee.

Multiple processes of the same user still need a policy: use documented shared-file support or
independent paths with a retention policy covering the complete set. Other users should have separate
profile roots. Review the provider's buffering, flush/disposal, full-disk, read-only, and internal
diagnostic behavior. A sink can report failure through its own diagnostics instead of throwing from
`ILogger`; the adapter's fallback cannot detect silent sink loss. Connect the provider's supported
diagnostic mechanism to a separate application fallback without taking over another application's
process-global diagnostic hook. Verify shutdown flushing and failure visibility with the selected
provider. Real file-provider/deployment examples and expected manual results belong to
[dotnet/winforms#14962](https://github.com/dotnet/winforms/issues/14962).

## Hosted services and desktop health (prototype, dotnet/winforms#14960)

Use the ordinary Generic Host registrations for `IHostedService`, `IHostedLifecycleService`, and
`BackgroundService`. Pass the **unbuilt** host builder to `UseHost`; do not create another host,
message loop, scheduler, or static provider. Host startup and eager validation precede deferred UI
activation and run off the owner STA. In the selected Hosting 10.0.10 implementation,
`BackgroundService.ExecuteAsync` runs entirely through `Task.Run`: it is not a readiness gate.
Required readiness belongs in an explicit hosted `StartAsync`, and cannot wait for UI dispatch.

`AddWinFormsHealthDiagnostics` returns the normal `IHealthChecksBuilder` and preserves existing
`HealthCheckService` registrations. It adds a singleton `WinFormsHealthDiagnostics`, but executes
no check. Health checks use the normal `Microsoft.Extensions.Diagnostics.HealthChecks` package;
there is no ASP.NET Core shared-framework requirement or HTTP endpoint.

```csharp
IHostBuilder hostBuilder = new HostBuilder()
    .ConfigureLogging(logging =>
        logging.AddFilter("Microsoft.Extensions.Diagnostics.HealthChecks", LogLevel.None))
    .ConfigureServices((_, services) =>
    {
        services.AddWinFormsHealthDiagnostics()
            .AddCheck<LocalStoreCheck>("local-store", tags: ["startup"])
            .AddCheck<RemoteDependencyCheck>("remote", failureStatus: HealthStatus.Degraded,
                tags: ["diagnostic"], timeout: TimeSpan.FromSeconds(5));
        services.AddHostedService<LocalReadinessService>();
        services.AddHostedService<SyncWorker>();
    });

WinFormsApplication? application = null;
using (application = WinFormsApplication.CreateBuilder()
    .UseApplicationContext(() => new ApplicationContext(new MainForm(
        application!.Host!.Services.GetRequiredService<WinFormsHealthDiagnostics>())))
    .UseHost(hostBuilder)
    .Build())
{
    application.Run();
}
```

Import `System.Windows.Forms`, `Microsoft.Extensions.Diagnostics.HealthChecks`, `Microsoft.Extensions.Logging`, and the
hosting, dependency-injection, and WinForms namespaces. Check, worker, and Form types are
application-defined. Implement `IHealthCheck.CheckHealthAsync` using typed, UI-free dependencies
and cooperative cancellation. No remote check executes merely because it was registered.

The standard health service can log descriptions, exceptions, and arbitrary report data, even when
the WinForms exception-logging scope has `IncludeExceptionDetails = false`. The explicit category
filter above disables that standard health logging. Application configuration/provider-specific
rules may override filters; verify the effective filter before using private checks. Show or log only
allowlisted status/name fields, and deliberately opt into other contents after privacy review.
`WinFormsHealthDiagnostics` neither changes logging filters nor adds report logging of its own.

Standard `AddHealthChecks` installs publisher infrastructure. Without registered
`IHealthCheckPublisher` instances it starts no publisher timer; explicitly registering publishers
opts into the ecosystem's scheduling and `HealthCheckPublisherOptions`, not a WinForms scheduler.
Do not combine publishers and worker/manual checks accidentally when double execution is unwanted.

### Explicit local readiness, not mandatory network connectivity

Select the checks needed for startup. An empty selection is not proof of readiness; the following
application policy rejects both an empty report and a non-Healthy local report. A different
application may deliberately accept Degraded local state. Optional remote failure must not
automatically prevent a usable offline UI from starting.

```csharp
public sealed class LocalReadinessService(WinFormsHealthDiagnostics diagnostics) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        HealthReport report = await diagnostics.CheckHealthAsync(
            registration => registration.Tags.Contains("startup"), cancellationToken);
        if (report.Entries.Count == 0 || report.Status != HealthStatus.Healthy)
        {
            throw new InvalidOperationException("Required local startup readiness failed.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

The equivalent VB registrations and explicit readiness policy are:

```vb
Dim hostBuilder As IHostBuilder = New HostBuilder().
    ConfigureLogging(
        Sub(logging)
            logging.AddFilter("Microsoft.Extensions.Diagnostics.HealthChecks", LogLevel.None)
        End Sub).
    ConfigureServices(
        Sub(context, services)
            services.AddWinFormsHealthDiagnostics().
                AddCheck(Of LocalStoreCheck)("local-store", tags:={"startup"}).
                AddCheck(Of RemoteDependencyCheck)(
                    "remote", failureStatus:=HealthStatus.Degraded,
                    tags:={"diagnostic"}, timeout:=TimeSpan.FromSeconds(5))
            services.AddHostedService(Of LocalReadinessService)()
            services.AddHostedService(Of SyncWorker)()
        End Sub)

Public NotInheritable Class LocalReadinessService
    Implements IHostedService

    Private ReadOnly _diagnostics As WinFormsHealthDiagnostics

    Public Sub New(diagnostics As WinFormsHealthDiagnostics)
        _diagnostics = diagnostics
    End Sub

    Public Async Function StartAsync(token As CancellationToken) As Task _
        Implements IHostedService.StartAsync
        Dim report As HealthReport = Await _diagnostics.CheckHealthAsync(
            Function(registration) registration.Tags.Contains("startup"), token)
        If report.Entries.Count = 0 OrElse report.Status <> HealthStatus.Healthy Then
            Throw New InvalidOperationException("Required local startup readiness failed.")
        End If
    End Function

    Public Function StopAsync(token As CancellationToken) As Task _
        Implements IHostedService.StopAsync
        Return Task.CompletedTask
    End Function
End Class
```

Compose the typed runtime Form through the public deferred context factory, not an internal
prototype activation API:

```vb
Dim application As WinFormsApplication = Nothing
application = WinFormsApplication.CreateBuilder().
    UseApplicationContext(
        Function()
            Dim diagnostics = application.Host.Services.GetRequiredService(Of WinFormsHealthDiagnostics)()
            Return New ApplicationContext(New MainForm(diagnostics))
        End Function).
    UseHost(hostBuilder).
    Build()
Using application
    application.Run()
End Using
```

`MainForm` has an application-defined constructor receiving the diagnostics service. Retain any
parameterless constructor needed by its Designer; existing controls require no migration. No
configuration default, readiness check, worker, endpoint, or network policy is implicitly selected.

### Responsive, tracked UI diagnostics

Pass `WinFormsHealthDiagnostics` to runtime-created UI through explicit composition as above,
without a static provider. Request diagnostics after UI dispatch exists, for
example through a caller-owned task launched after `Shown`. The service defers health-service
resolution, options callbacks, selection predicates, checks, and operation-scope disposal away
from the UI thread. Replacements registered through `HealthCheckService` are still used.

These example methods belong to the owning Form. `_diagnostics` is its typed runtime dependency,
`statusLabel` is its UI field, and `ownerToken` is canceled when that owner stops accepting work:

```csharp
private async Task RefreshHealthAsync(CancellationToken ownerToken)
{
    HealthReport report = await _diagnostics.CheckHealthAsync(
        registration => registration.Tags.Contains("diagnostic"), ownerToken);
    ownerToken.ThrowIfCancellationRequested();
    if (!IsDisposed && !Disposing)
    {
        statusLabel.Text = report.Status.ToString();
    }
}
```

```vb
Private Async Function RefreshHealthAsync(ownerToken As CancellationToken) As Task
    Dim report As HealthReport = Await _diagnostics.CheckHealthAsync(
        Function(registration) registration.Tags.Contains("diagnostic"), ownerToken)
    ownerToken.ThrowIfCancellationRequested()
    If Not IsDisposed AndAlso Not Disposing Then
        statusLabel.Text = report.Status.ToString()
    End If
End Function
```

Calling these methods on the installed WinForms context allows ordinary `await`/`Await` to resume
on the owner STA; do not use `ConfigureAwait(false)` before updating Controls. Preserve the returned
task, surface infrastructure faults through the application's error policy, and treat
`OperationCanceledException` as cancellation rather than a fabricated Unhealthy report. Dependency
exceptions thrown inside a check retain the standard service's `HealthReportEntry` failure behavior.
Factory, selection, provider, and disposal failures can instead fault the operation.

Background workers must not retain or access Controls. If UI updates are needed, obtain an
application-owned `IProgress<HealthStatus>` or owning synchronization context **after** UI activation
through a cancellable asynchronous handoff in `ExecuteAsync`, never by waiting for UI in `StartAsync`.
Create `Progress<T>` on the owning UI thread; its callback must recheck owner disposal and stopping.
Use `Post`/`Progress<T>.Report`, not `Send`, `.Wait()`, or `.Result`. Stop posting as soon as
`IHostApplicationLifetime.ApplicationStopping` is signaled, and recheck that signal inside the posted
callback. No new dispatcher is supplied here.

### Cancellation and shutdown ownership

Each request links its caller token with `IHostApplicationLifetime.ApplicationStopping`.
Canceling one request does not stop the application. A canceled operation is not returned as a
successful report, even when a non-cooperative check returns after cancellation or cancellation
arrives during asynchronous scope disposal. The task waits for its service/check work and
operation-scope disposal; it does not abandon those owners on cancellation.

Registration timeouts signal cancellation to the check. The standard service reports a cooperative
timeout using the registration's `FailureStatus`; that differs from caller/host cancellation, which
propagates. A check ignoring its timeout can run indefinitely or return its own result after the
timeout: no forced interruption or replacement result is promised.

Confirm close/veto decisions before committing host stop. Pending owner tasks must be canceled and
awaited **before** disposing their dependencies or committing the owner's shutdown; a canceled
token alone is not proof they completed. The host does not discover arbitrary UI/event-handler
tasks, and this service is not another shutdown coordinator. Apply the same ownership rule to
error-driven exits and other stop origins.

At committed stop the runtime signals host stopping, exits and disposes owned UI/scopes, completes
the UI barrier, then invokes hosted lifecycle stopping/service `StopAsync`. Those callbacks must not
require live Forms or UI dispatch. Link UI-posting workers to `ApplicationStopping`: their
`BackgroundService` stopping token is canceled only in the later service-stop phase.

Generic Host timeout/cancellation does not forcibly stop workers. In particular, the standard
`BackgroundService.StopAsync` can stop waiting when its timeout expires while `ExecuteTask` remains
active. An application participant with dependency-owned pending work must explicitly drain that
work before its dependency owner is disposed; cancellation of a wait is not a safe disposal barrier.
Non-cooperative work may therefore delay an application-owned drain indefinitely. The existing
WinForms UI barrier likewise remains mandatory on timeout and is not weakened by this package.
Full deployment samples, manual offline/failure/resource checks, and budgets remain follow-up work
in [dotnet/winforms#14962](https://github.com/dotnet/winforms/issues/14962) and
[dotnet/winforms#14963](https://github.com/dotnet/winforms/issues/14963).
