# Microsoft.Extensions.WinForms.Hosting

Opt-in configuration defaults and typed user settings for the WinForms Application Builder. This is a separate application
NuGet dependency, not an addition to the WindowsDesktop shared framework. Existing WinForms
applications and the hostless Application Builder do not acquire configuration providers.

This package and its public API are prototypes tracked by
[dotnet/winforms#14957](https://github.com/dotnet/winforms/issues/14957) and
[dotnet/winforms#14958](https://github.com/dotnet/winforms/issues/14958), pending API/package review.
It targets the repository's current framework and requires the matching Application Builder
prototype for WinForms lifetime integration.

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
`Build`, `Run`, or `RunAsync` on that host. This package supplies configuration only: it does not
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
