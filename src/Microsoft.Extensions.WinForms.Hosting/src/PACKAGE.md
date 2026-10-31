# Microsoft.Extensions.WinForms.Hosting

Opt-in configuration defaults for the WinForms Application Builder. This is a separate application
NuGet dependency, not an addition to the WindowsDesktop shared framework. Existing WinForms
applications and the hostless Application Builder do not acquire configuration providers.

This package and its public API are prototypes tracked by
[dotnet/winforms#14957](https://github.com/dotnet/winforms/issues/14957), pending API/package review.
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
