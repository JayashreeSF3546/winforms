# Company.WinFormsBuilderApp1

## Prototype prerequisites

This is an Application Builder prototype, not a template supported by an unmodified public SDK.
It targets `net12.0-windows` and pins `Microsoft.Extensions.WinForms.Hosting` to `12.0.0-dev`
and Microsoft.Extensions dependencies to `10.0.10`. The hosting package alone does not provide
the in-development builder APIs in the WindowsDesktop reference pack/runtime.

Obtain matching approved framework/reference/runtime packs and the hosting package before restore,
build, run, or publish. Do not copy repository DLLs or development-only framework overrides into
this application to make it appear generally supported. A successful project-creation message is
not proof that prerequisites were restored. Older target frameworks are not supported by this
prototype. Shipping package/API/SDK approval is still required.

## Startup and ownership

The entry point follows the startup sample in commit `63577553`: it creates a builder with
`UseHost(New HostBuilder())` and registers settings and exception services through `builder.Services`.
`builder.ConfigureLogging(Sub(logging) logging.UseWinFormsLogging())` is the VB equivalent of
the C# `builder.Logging.UseWinFormsLogging()` extension-property call.

`UseStartupForm(Of Form1)()` defers the parameterless form constructor until startup.
The fluent chain selects DPI, color, text rendering, visual styles, reveal mode, font, and exception
routing before `Build` and `Run`. Do not also initialize these defaults through `Application`.
The runner owns the form, host, and container-created services. Do not separately build/run the host.

The form remains blank and Designer-compatible. No host or settings work runs in `InitializeComponent`.
This is a native VB project with explicit `Sub Main`, not the VB Application Framework.
There is no application-context factory, constructor injection, options model, or `appsettings.json`.
The plain `HostBuilder` does not add JSON, environment-variable, or command-line configuration defaults.

Debug logging requires an attached debugger. The explicit UI-exception handler writes the exception
type to standard error and exits the UI thread; logging failures also write to standard error.
A `WinExe` does not normally have a visible console. Durable logging, error dialogs, and custom exit
codes are application choices, not additional template behavior.

## User settings

`StartupPreferences.UseCompactLayout` defaults to `True`, matching the reference sample.
It is registered with explicit reflection-based
`JsonTypeInfo(Of StartupPreferences)` from `DefaultJsonTypeInfoResolver`. No C# helper project is
required. Registration creates no directory or file and does not load, apply, or save preferences.
Resolve `IUserSettingsService(Of StartupPreferences)` in the composition root and pass it to the UI
when implementing explicit asynchronous user commands. Keep the settings service host-owned.

Store data is under `%LOCALAPPDATA%\WinForms\<SettingsApplicationId>`, unless deliberately configured
otherwise. Keep the generated `SettingsApplicationId` literal stable across project renames,
releases, and template updates. Schema version 1 is independent of executable/package version.
Settings are not encrypted; do not store secrets in settings or deployment JSON.

Await load/save operations, report errors, and own/cancel/join any pending operations before their
dependencies are disposed. Do not add fire-and-forget autosave or synchronous UI waits during
shutdown. Migration, validation, recovery, and durable logging belong to deliberate app policies,
not implicit template behavior.

## Build, publish, and upgrade

After the prerequisites above are available, use normal Visual Studio restore/build/run/publish
or `dotnet restore`, `dotnet build`, `dotnet run`, and `dotnet publish`.
For example, framework-dependent publishing uses
`dotnet publish -c Release -r win-x64 --self-contained false`; self-contained publishing uses
`dotnet publish -c Release -r win-x64 --self-contained true` and requires matching runtime packs.
This reflection-based VB prototype makes no trimming, single-file, or NativeAOT support claim.

Generated files are application-owned. Updating the installed template affects future projects,
not this source or its package references. Review package/SDK changes and migration diffs explicitly;
never regenerate over your application with `--force` as an upgrade. Do not reset the settings
identity or discard existing user data when changing assembly versions.
