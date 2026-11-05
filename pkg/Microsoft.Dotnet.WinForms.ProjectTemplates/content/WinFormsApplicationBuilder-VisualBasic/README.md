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

`Program.vb` initializes the STA UI, configures one host, and passes the unbuilt host builder to
the WinForms runner. The deferred context factory constructs the Form only after host startup
and options validation. The runner owns the context, Form, host, and container-created services.
Do not separately build/run that host or create another service provider.

This is a native VB project with explicit `Sub Main` selection in both CLI and IDE hosts.
It does not use the VB Application Framework, single-instance support, or splash-screen startup.
The public parameterless Form constructor is for ordinary UI/Designer construction. Runtime
dependencies are passed through its typed constructor, outside Designer-managed code.
No settings, host, or network work runs in `InitializeComponent`. The Designer creates a `.resx`
when resources are needed; it owns that resource file and `Form1.Designer.vb`.

Configuration starts at the application directory, not the current working directory.
`appsettings.json` is copied to build/publish output. Environment variables such as
`Application__Title` and command-line arguments such as `--Application:Title="My application"`
override deployment JSON. `Application:Title` must not be empty. Configuration file watchers
are disabled by default; enabling reload requires deliberate UI-thread marshaling.

The host uses configuration-filtered debug logging. View it with an attached debugger; no file,
console, or remote logging destination is implied. Startup/run/shutdown failures are traced,
shown to the user, and return a nonzero exit code. Unexpected UI exceptions are not silently
continued. Additional logging providers and exception policies are explicit application choices.

## User settings

`UserPreferences` is an example model, registered with explicit reflection-based
`JsonTypeInfo(Of UserPreferences)` from `DefaultJsonTypeInfoResolver`. No C# helper project is
required. Registration creates no directory or file and does not load, apply, or save preferences.
Resolve `IUserSettingsService(Of UserPreferences)` in the composition root and pass it to the UI
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
