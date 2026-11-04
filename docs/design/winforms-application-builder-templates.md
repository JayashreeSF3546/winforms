# WinForms Application Builder: template and sample matrix

Status: architecture recommendation for
[dotnet/winforms#14965](https://github.com/dotnet/winforms/issues/14965),
pending architecture, API, packaging, and tooling-owner review.

Parent: [dotnet/winforms#14964](https://github.com/dotnet/winforms/issues/14964).
Proposal: [dotnet/winforms#14082](https://github.com/dotnet/winforms/issues/14082).
Dependencies: the [host/lifetime](winforms-application-builder-lifetime.md),
[DI/UI-scope](winforms-application-builder-di-scopes.md), and
[service infrastructure](winforms-application-builder-services.md) contracts.

## Scope and recommendation

Introduce one opt-in Application Builder project family with equivalent C# and VB output,
and one C# Blazor Hybrid project family. Preserve the existing classic templates. Services
are part of the basic builder composition, not a separate "with services" template.
Migration, enterprise, tray, and backend architectures belong in focused samples.

These are recommended delivery targets, not already available or approved shipping templates.
This child documents the matrix, ownership, prerequisites, and validation contract. It does
not implement template payloads, change packages or public APIs, or complete later children.
In particular, Hybrid delivery is gated on demonstrating coordinated asynchronous cleanup
with supported public contracts.

## Research baseline

The local baseline is `7f915492c` on `Application_Builder_14964_Templates`.
The following observations were checked on 2026-11-03.

| Workload | Observed strategy | Decision for WinForms |
|---|---|---|
| Current WinForms | App, class-library, and control-library families have C#/VB variants. App templates share `winforms`, a group identity, localized metadata, and host-dependent primary outputs/post-actions. Classic startup uses `Application.Run`. | Keep these families and their behavior unchanged. Reuse their naming, localization, Designer-file, and host-integration conventions. |
| WPF | C# project metadata groups language/framework variants. `App.xaml` supplies `StartupUri`; its code-behind derives from WPF `Application`, without introducing a Generic Host. | A desktop template need not impose hosting on existing apps. Preserve the classic reference path rather than changing its defaults. |
| ASP.NET Core | The minimal web template creates, builds, and runs a `WebApplication`. Host construction and application ownership are visible in generated source. | Keep composition discoverable, but do not copy Kestrel, HTTP endpoints, HTTPS certificates, or web lifetime drivers. |
| Generic Host / Worker | The Worker template registers services on `Host.CreateApplicationBuilder`, builds once, then runs the host. | Reuse standard services and ownership, not `Host.Run` alongside a WinForms runner. The current WinForms seam accepts `IHostBuilder`, not `HostApplicationBuilder`. |
| MAUI | The Hybrid template registers Blazor services on `MauiApp.CreateBuilder`, then builds one application. Its metadata includes platform/workload-specific choices. | Reuse the single-composition-root principle, not MAUI application/window lifetime or its multi-platform workload requirements. |
| Blazor | Blazor Web App metadata can conditionally generate a client project/solution. The WinForms Hybrid tutorial instead composes a Razor-enabled WinForms project and a WebView. | Choose a single-project Hybrid baseline. Do not copy the tutorial's Form-local `BuildServiceProvider`, or substitute a Blazor server/WebAssembly application for desktop Hybrid. |

The [local template package](../../pkg/Microsoft.Dotnet.WinForms.ProjectTemplates/Microsoft.Dotnet.Winforms.ProjectTemplates.csproj)
is content-only and currently has `IsShipping=false`. Its project does not explicitly declare
`PackageType=Template`; the modern authoring guidance requires that metadata for
`dotnet new search` discovery. Existing packaging is not evidence of a shipping/discoverable
Application Builder package.

Both checked-in and installed classic app templates currently offer `net8.0`, `net9.0`,
`net10.0`, and `net11.0`, defaulting to `net11.0`. The repository SDK is a .NET 12 preview.
Neither that SDK version nor the classic framework choices establishes availability of the
prototype builder APIs in an installed WindowsDesktop reference pack.

## Template, sample, and documentation matrix

Names and short names below are proposed public names; check collisions and obtain tooling-owner
approval before publishing. "Project" describes the template-engine type, not a new solution format.

| Experience | Delivery | Language | Default contents and boundary | Owner |
|---|---|---|---|---|
| Windows Forms App; `winforms` | Existing project template | C#, VB | Existing hostless startup and current framework choices. No automatic migration to the builder. | Existing template owners |
| Windows Forms class/control libraries | Existing project templates | C#, VB | Unchanged reusable UI/library scaffolding; no application host. | Existing template owners |
| Windows Forms App (Application Builder); `winforms-builder` | New project family | C#, VB | Builder, one Generic Host, DI, configuration/options, typed user-settings registration, and logging. One Designer-compatible main Form. | [dotnet/winforms#14966](https://github.com/dotnet/winforms/issues/14966) |
| Windows Forms Blazor Hybrid App (Application Builder); `winforms-blazor` | New project family, gated | C# | Basic composition plus Razor components and `BlazorWebView`, sharing the host root. No web server or separate container. | [dotnet/winforms#14967](https://github.com/dotnet/winforms/issues/14967) |
| WinForms + Services | Documentation and basic-template walkthrough | C#, VB | Explain the services already included in the basic family; do not duplicate its payload under another name. | Basic template documentation |
| Incremental migration | Focused sample and guide | C#, VB entry points | Preserve an existing Form, move composition to the builder, then adopt services incrementally. | [dotnet/winforms#14968](https://github.com/dotnet/winforms/issues/14968) |
| Layered enterprise composition | Focused sample, potentially multi-project | C#, VB frontends where applicable | Service layer, validated configuration, logging, and lifetime. Shared C# infrastructure is disclosed, not described as an all-VB application. | dotnet/winforms#14968 |
| System tray / `ApplicationContext` | Focused sample | C#, VB entry points | Explicit context/NotifyIcon ownership, exit command, and host-origin stop; no hidden main-Form lifetime assumption. | dotnet/winforms#14968 |
| Backend/background integration | Focused sample | C#, VB entry points | Optional service/backend work, cancellation, diagnostics, and configuration. Default sample execution must not require cloud credentials. | dotnet/winforms#14968 |
| VB host with Blazor UI | Composition sample/guide, not a VB Razor template | VB host + C# Razor class library | Explicit mixed-language boundary and shared host; subject to the same Hybrid cleanup gate. No claim of VB Razor compilation. | Hybrid/adoption sample owners |
| Advanced settings, health checks, file logging, deployment | Documentation and focused samples | As applicable, with language limitations stated | Schema migrations, explicit checks, provider selection, offline deployment, and enterprise policy. No option-per-provider template explosion. | Services/adoption owners |

No separate solution template is required initially. Visual Studio can create its normal solution
around a project; CLI users can compose projects with the standard solution commands. Multi-project
adoption samples can check in a solution without becoming a maintained `dotnet new` product.
No new item templates are required; existing Visual Studio item-template ownership remains separate.

### Language and Designer contract

Basic C# and VB projects must have equivalent observable startup, configuration precedence,
settings behavior, logging, shutdown, and supported deployment modes. Equivalent does not mean
identical syntax or copying a C# program behind a nominal VB entry point.

Both use an explicit STA `Main`. C# retains the standard generated
`ApplicationConfiguration.Initialize` pattern; VB uses equivalent explicit UI initialization
following its existing direct-`Sub Main` template. Match effective DPI, visual-style, and text
rendering settings, not merely method names.

The new VB template always selects explicit `Sub Main` startup in both CLI and IDE hosts.
The existing VB template can select different application-model output depending on its host;
do not inherit that conditional behavior accidentally. Do not generate `MyApplication` startup,
single-instance behavior, splash screens, or a switch suggesting those features are integrated.
VB Application Framework integration belongs to
[dotnet/winforms#14972](https://github.com/dotnet/winforms/issues/14972).

Keep the public parameterless Form constructor UI-only and usable by the Designer. Typed runtime
constructors/binding may follow `InitializeComponent`; they must not be required for design-time
instantiation. No host start, provider lookup, settings I/O, network call, or browser initialization
belongs in field initializers, `InitializeComponent`, or the Designer constructor.

For settings, both languages supply explicit `JsonTypeInfo<T>` to the same registration contract.
C# should use a source-generated JSON context. A standalone VB project can obtain typed metadata
through an explicitly configured `DefaultJsonTypeInfoResolver`; verify that path rather than
quietly adding a C# helper project. Document reflection requirements. Neither the basic VB project
nor the Hybrid template promises trimming/NativeAOT support. C# source generation alone does not
establish that the entire WinForms application is trim-safe.

## Generated files and default composition

### File ownership

The following is a file-role contract, not source scaffolding delivered by this issue.
Use equivalent VB filenames/namespace conventions and the existing WinForms resource layout.

| Files | Owner after creation | Required purpose |
|---|---|---|
| Application `.csproj` / `.vbproj` | Application developer | `WinExe`, supported Windows target, `UseWindowsForms`, explicit compatible package versions, and correct entry-point/language settings. No repository imports. |
| `Program.cs` / `Program.vb` | Application developer | STA UI initialization, visible service composition, one runner, and explicit error reporting. |
| `Form1.cs` / `Form1.vb` | Application developer | UI behavior and typed runtime dependency boundary; service-free Designer constructor. |
| `Form1.Designer.cs` / `Form1.Designer.vb`, `Form1.resx` | Designer-managed application source | Standard partial class, resources, component disposal, and round-trip serialization. Runtime service wiring stays outside these files. |
| `appsettings.json`, a small options model | Application developer | Non-secret application defaults and startup validation. Copy required content to build/publish output. |
| A small user-preferences model; C# JSON context where used | Application developer | Fresh defaults, explicit metadata, stable application identity, and schema version independent of executable version. |
| Generated-project `README.md` | Application developer | Supported SDK/packages, launch/publish, settings location, ownership, failure behavior, and upgrade guidance. |
| Hybrid `.razor` files, imports, local host page/static assets | Application developer | A minimal local component and published content; Razor SDK configuration is Hybrid-only. |
| `.template.config`, localization files, host metadata | Template maintainers, not generated app content | Creation choices, naming transforms, exclusions, primary outputs, and trusted post-actions. |
| `obj`, `bin`, compiler-generated code, intermediate static-web-assets manifests | SDK/build tooling | Recreated by build; never copied from the author's machine into generated source. Required publish output is produced by the SDK. |

Generation must not emit template metadata, authoring projects, test logs, absolute repository paths,
credentials, package caches, machine-specific feeds, or stale lock/build files. Any intentionally
generated lockfile must instead be an explicit, documented reproducibility decision.

### Basic startup and services

Pass one unbuilt `IHostBuilder` through the public `UseHost` seam. Use the runner as the sole
message-loop/lifetime driver; do not call host `Run`, run a second `Application.Run`, or build a
second provider. The runner owns the coordinated host and its container-created services.

The current public generic `UseStartupForm<TForm>()` requires `new()`; it does not perform
constructor injection. Scoped startup/factory helpers in the prototype are internal. Generated
projects must not rely on friend access, reflection, new public factory APIs, or a Form registered
as a disposable root service merely to shorten startup.

Where typed runtime dependencies are needed, the existing public deferred
`UseApplicationContext(Func<ApplicationContext>)` seam permits explicit Form/context construction
on the owning UI thread after host startup. Resolve dependencies from the built application's
host only inside that composition boundary, then pass typed values to the Form. A local captured
application reference is not permission to expose a static/global service locator. Do not resolve
scoped services from the root; application/Form scope activation remains an approved-API dependency.

| Feature | Basic default | Explicit opt-in or sample |
|---|---|---|
| Host/DI | One standard host, one root, typed dependency boundary, coordinated ownership | Additional Forms/scopes only through supported contracts |
| Configuration/options | One host configuration root using the services stream's documented desktop precedence; validate the small bound model before showing UI | Additional providers and live reload |
| File watchers | Disable configuration reload-on-change before default providers are created | Deliberate reload policy with UI-thread marshaling |
| User settings | Register the existing JSON service with fresh defaults, explicit metadata, a stable generated application-ID literal, and schema version 1 | User-triggered asynchronous load/save, validation, migration, and policy-selected storage |
| Logging | One host pipeline with an explicitly selected debug provider and configuration filters; no unnecessary console/file/network providers | Durable file/event/remote providers and their retention/error policies |
| Background work/health | No generated worker, polling loop, health gate, or network dependency | Focused backend/health samples |
| Exceptions | Preserve the reviewed runtime policy; surface startup/run/shutdown failures with a non-success exit and useful user-visible diagnostics | Deliberate exception routing, never blanket "continue" handling |

Registration is not an implicit settings read or write. Do not autosave during Form disposal,
perform synchronous waits on UI settings operations, or swallow persistence failures. Persist the
generated application-ID literal across renames/releases; do not derive storage identity from
executable version or installation path. App configuration and per-user settings remain separate.
Any example that starts asynchronous operations must own, cancel, and join them before dependencies
are disposed; the richer infrastructure sample's worker/session machinery is not a basic-template
dependency.

Keep application code short and learnable. Do not generate a custom builder wrapper, base Form,
container adapter, settings engine, logger implementation, controller framework, or cloud bootstrap.

## Hybrid composition and release gate

Register `AddWindowsFormsBlazorWebView` on the same host service collection. At runtime, supply the
host provider to `BlazorWebView.Services`; the Form borrows it and must not dispose it. Renderer/page
scopes created from that root are expected: "one provider" means one independently built root, not
the absence of child scopes. A Form scope and a renderer scope are not automatically the same scope.
Do not promise shared scoped instances across those boundaries.

Use the Razor SDK and an explicitly pinned compatible
`Microsoft.AspNetCore.Components.WebView.WindowsForms` package. Start with one project, one local
host page, and one small component. No MAUI app model, ASP.NET server, HTTP listener, WebAssembly
project, browser-local service container, or redundant root configuration/logger factory is needed.
UI construction and browser startup must remain inert in the Designer.

### Asynchronous cleanup prerequisite

The inspected MAUI main revision and released `10.0.120` source both implement WinForms
`BlazorWebView.Dispose(bool)` by scheduling a dispatcher operation that awaits the internal
WebView manager and only then calls `base.Dispose`. The control's synchronous `Dispose` returns
without exposing that operation as a public completion task. The manager owns asynchronous
page/renderer-scope disposal. `TryDispatchAsync` dispatches scoped work; it is not a disposal join.

Consequently, a synchronous Form disposal call is not proof that components, scoped services,
or browser infrastructure have finished cleanup before the message loop and host disappear.
This is a researched integration risk, not a demonstrated failure of every application or a
decision that all future package versions have the same behavior.

Before dotnet/winforms#14967 can claim support, its owners must select exact package versions and
demonstrate a supported public cleanup protocol for user close, host-origin stop, startup failure,
and asynchronous component disposal. UI-required cleanup must complete while dispatch is available
and before host/provider teardown. Checking a disposed flag or only testing synchronous components
does not establish that guarantee.

If existing supported contracts cannot provide that protocol, obtain an upstream WebView/lifetime
owner decision first. Do not fix it here with private-manager reflection, a second provider,
blocking UI waits, sleeps/polling, unobserved async disposal, or a new template-owned lifetime engine.
The template stream composes approved infrastructure; it does not silently expand foundation APIs.

### Content, navigation, and deployment

Publish local Razor/static assets and test them from a different working directory and an installed,
non-writable application directory. Keep browser user data in an explicitly owned, writable per-user
location, separate from read-only assets and application settings. Do not ship development-only
absolute static-asset paths.

Keep privileged .NET interaction restricted to trusted local content. Apply an explicit navigation/
external-link policy, reject unexpected schemes/origins, and do not load remote pages into the
privileged application surface by default. Keep developer tools and sensitive diagnostics disabled
in Release. Verify initialization, navigation, renderer, and cleanup failures at their actual
exception boundaries, without implying a native WinForms exception hook captures every browser error.

The WebView2 Runtime is a deployment prerequisite independent of the .NET runtime. Self-contained
.NET publishing does not supply it. Prefer documented Evergreen provisioning for normal deployment;
offline/restricted deployments need an approved offline Evergreen installer or a maintained Fixed
Version distribution. Do not launch an elevated/browser-runtime installer as a template post-action
or silently download one on first launch. Missing-runtime failures require an actionable diagnostic.
Record the intersection of supported Windows, .NET, package, architecture, and WebView2 versions;
do not infer Windows 7 support from a prototype project's `windows7.0` suffix.

## Packaging, discovery, versions, and upgrades

### Package and SDK strategy

Recommend extending the existing `Microsoft.Dotnet.WinForms.ProjectTemplates` content package with
distinct builder families, reusing repository packaging/localization infrastructure. Do not create
a package per language or service option. Preserve classic payloads and selection behavior.
Publication and any change to `IsShipping` require package/release-owner approval; this document
neither flips that flag nor asserts that SDK/Visual Studio distribution is arranged.

The owning implementation must validate the packed layout, hidden metadata/localization inclusion,
exclusions, and `PackageType=Template` search metadata against the actual repository pack targets.
Do not copy an unrelated authoring tutorial project wholesale or install floating authoring tools.
Separate opt-in distribution remains a review alternative if shared-package release cadence or
classic template precedence cannot be preserved.

Keep proposed family groups distinct from classic `Microsoft.Common.WinForms`:
`Microsoft.Common.WinForms.ApplicationBuilder` and `Microsoft.Common.WinForms.BlazorHybrid`.
Language/framework-specific identities and precedence follow existing template conventions.
Do not change a family's group/short name merely for a servicing update. Treat identity, choice,
generated-default, or file-layout changes as compatibility changes and document them.

| Dependency | Basic | Hybrid | Availability requirement |
|---|---|---|---|
| .NET SDK and WindowsDesktop reference/runtime packs | Required | Required | A matching supported release must actually expose the approved builder/lifetime APIs |
| `Microsoft.Extensions.WinForms.Hosting` | Required for the selected services composition | Same instance/version family | Approved, published package with compatible framework and Microsoft.Extensions dependencies |
| Hosting/configuration/options/logging packages | Explicit references where directly consumed; compatible transitive dependencies otherwise | Same root/pipeline | Exact reviewed versions; no unnecessary providers |
| WindowsForms BlazorWebView package and Razor SDK | None | Required | Compatible pinned package/SDK plus demonstrated cleanup contract |
| WebView2 Runtime | None | Required on deployed device | Installer/deployment responsibility, not supplied by the template package |

The [prototype companion project](../../src/Microsoft.Extensions.WinForms.Hosting/src/Microsoft.Extensions.WinForms.Hosting.csproj)
currently uses `UsePrototypeWinFormsReference` to replace installed framework references.
The companion NuGet package alone does not add missing APIs to the installed reference pack/runtime.
Generated consumer projects must not contain repository `ProjectReference`s, copied patched
framework DLLs, private build targets, development feeds, or credentials to hide this gap.
Any repository-only prototype validation harness must be separate, explicit, and labeled as such.

The initial shipping framework choice is the first approved WindowsDesktop target containing the
required public contracts, not all classic TFMs. `net12.0-windows` is the current prototype target,
not a shipping support promise. Gate actual framework choices, defaults, SDK/host constraints, and
package versions on that approval. Reject unsupported combinations rather than expose an arbitrary
target-framework override that appears to make older frameworks compatible.

### CLI and Visual Studio experience

Use standard `dotnet new` install/list/search/create/update/uninstall infrastructure and standard
restore/open-file post-actions. Do not create a custom installer, download script, VSIX, or arbitrary
shell post-action for initial delivery. Restore failures must remain visible; a template-engine
"created successfully" message or a continue-on-error restore post-action is not build acceptance.

Keep C#/VB entries grouped appropriately, preserve safe project/root-namespace/resource substitutions,
and distinguish the builder and Hybrid entries from classic WinForms in IDE discovery. Follow host
metadata schemas supported by the selected Visual Studio version; validate primary-output/open-file
indices for both CLI and IDE variants. Localize new names, descriptions, options, and user-facing
instructions using the existing pipeline.

The .NET authoring documentation describes CLI-installed project/solution templates appearing in
Visual Studio, unlike item templates. That documentation is not evidence that these particular
packages work in every installed VS version. Supported IDE version, desktop/Razor prerequisites,
SDK selection, discovery, creation, and out-of-process Designer round trips remain explicit gates.
Do not access or modify the separately owned private item-template repository for this work.

### Version and upgrade policy

Version the template package with the repository's approved release train. Pin generated direct
package references to exact compatible released or deliberately selected preview versions; do not
use `*` or unbounded "latest" dependencies. Document SDK/runtime/package compatibility separately:
an updated template package cannot upgrade the installed runtime or add framework APIs.

For SDKs supporting the current documented syntax, a version-pinned install has the form
`dotnet new install Microsoft.Dotnet.WinForms.ProjectTemplates@<approved-version>`.
This is an illustrative contract, not a command executed or a package version published by this
child. Preview adoption must be explicit, with matching reference/runtime packs. Enterprise users
can mirror approved packages, pin SDK selection, and apply their own lockfile/feed policy without
embedding machine-specific policy in every generated project.

`dotnet new update --check-only` checks installed template-package updates; applying an update
changes future generation, not existing application source. Existing projects are developer-owned.
Provide release notes and focused migration diffs for startup, package/TFM changes, metadata,
settings schemas, and Hybrid assets. Never rerun generation with `--force` as an upgrade mechanism.
Keep the settings application ID stable; schema migrations are explicit and independent of assembly
or template-package version. Package rollback does not automatically downgrade stored user data.

## Adoption and rejected alternatives

New users choose classic WinForms when they do not need a host, or the builder family when they want
the standard services composition. Hybrid users additionally satisfy its browser/runtime and cleanup
prerequisites. The README must describe those costs rather than market all variants as equivalent
zero-overhead replacements.

Existing applications can first preserve their Form/resources and establish the STA builder/lifetime
entry point without adopting every optional provider. Add the coordinated host, typed dependencies,
configuration, logging, and explicit settings migration in reviewable increments. Do not discard
legacy settings or rewrite Designer code automatically. VB Application Framework applications must
identify their separate parity dependency rather than silently lose application events/features.

Enterprise adoption uses samples to explain service layering, background cancellation, diagnostics,
deployment, and policy. Reuse the existing services-stream examples where appropriate, documenting
which helpers are illustrative. Do not turn samples into a required framework or ship secrets,
external endpoints, automatic cloud enrollment, or background telemetry in a basic template.

| Rejected default | Reason / trade-off |
|---|---|
| Replace `winforms` with hosted startup | Changes existing onboarding and baseline costs; opt-in families make adoption explicit |
| Separate templates for each service/provider combination | Duplicates nearly identical applications and expands the test/support matrix |
| Multi-project enterprise/solution skeleton for every user | Adds architecture before it is needed; samples can teach meaningful multi-project boundaries |
| A standalone all-VB Razor template | Hides a real mixed-language boundary; keep VB hosting and C# Razor composition explicit |
| Form-local/global providers or copied internal startup APIs | Breaks ownership, Designer safety, or external-consumer compatibility |
| Automatic source regeneration or settings reset on upgrade | Overwrites developer-owned changes and can lose user data |
| Treat repository runtime/package substitution as consumer support | A patched local build is not an installed, approved SDK/runtime contract |

## Delivery ownership and acceptance gates

| Child | Deliverable and required evidence |
|---|---|
| dotnet/winforms#14965 | This researched matrix, file ownership, decisions, dependencies, and upgrade/adoption policy. No generated-app completion claim. |
| dotnet/winforms#14966 | Basic C#/VB payloads, packaging metadata, native-language parity, focused creation/content checks, and externally valid dependency composition; report framework/API/package blockers explicitly. |
| dotnet/winforms#14967 | Gated Hybrid composition, exact version selection, supported async cleanup, shared-root/config/logging checks, exception/navigation boundaries, published assets, and Designer-inert startup. |
| dotnet/winforms#14968 | Migration, enterprise, tray/context, and backend samples with focused documentation; avoid duplicating the earlier infrastructure sample without a distinct adoption lesson. |
| [dotnet/winforms#14969](https://github.com/dotnet/winforms/issues/14969) | Isolated template install, list/create, explicit restore, build, bounded run, publish, and published-app run; uninstall/update and expected-file checks without changing the developer's template installation. |
| [dotnet/winforms#14970](https://github.com/dotnet/winforms/issues/14970) | Actual CLI/VS workflows, Designer edits/reopen, localization/accessibility/keyboard, light/Dark/Classic modes, DPI, ARM64, Terminal Services, restricted/offline and packaged/unpackaged deployment; record outcomes and gaps. |
| [dotnet/winforms#14971](https://github.com/dotnet/winforms/issues/14971) | Generated-app startup/shutdown, managed memory/working set, assembly/file/handle counts, restore/build/publish time and deployment size against classic baselines; isolate optional-feature costs. |

The automation matrix covers basic C# and VB plus C# Hybrid, framework-dependent and self-contained
deployment, and representative x64/x86/ARM64 configurations supported by each dependency.
Do not imply unsupported language/architecture combinations by taking an unrestricted Cartesian
product. Check names with spaces/non-identifier characters, namespace/resource substitutions,
content manifests, no-restore creation, missing/cached packages, unsupported SDKs, install/update/
uninstall, startup failure, and both UI- and host-origin shutdown.

External-consumer compilation, execution, publishing, actual Designer work, and remote CI are
separate gates. The inherited API/package reviews, service persistence stress/native-I/O findings,
component-manager resource observations, and broader integration-test gaps remain tracked in the
services design record; templates do not close them. Performance budgets need owner approval, not
an invented threshold. A passing prototype run does not establish shipping acceptance.

For this documentation child, verify source references, relative links, whitespace, and scope.
Template install/create/build/run/publish and CI are not performed or claimed here.

## Basic template implementation (dotnet/winforms#14966)

The subsequent basic-template child adds the opt-in `winforms-builder` family in native C# and VB,
without changing any classic payload. Both entries use standard template metadata, exact package
versions, GUID-generated settings identities, restore/open-file post-actions, English localization
metadata, and the existing content-only package. The package now explicitly declares its `Template`
package type; it remains nonshipping. Per-culture catalogs currently contain English fallback text,
not reviewed translations.

Generated applications target `net12.0-windows`, pin the prototype hosting package to `12.0.0-dev`,
and pin the other direct Microsoft.Extensions dependencies to `10.0.10`. They compose one unbuilt
host, configuration-filtered debug logging, validated options, and registered typed user settings.
The public deferred application-context factory constructs the typed Form after host startup.
The parameterless Form constructor only initializes Designer-owned UI. C# uses generated JSON
metadata; VB uses explicit typed reflection metadata without a C# helper or VB Application Framework
dependency. Settings operations remain explicit, and no trimming/NativeAOT claim is made.

These are deliberately labeled prototypes: the hosting package cannot add the missing framework
APIs to an unmodified public SDK. Generated projects contain no repository imports/references.
The separate [local validation harness](../../pkg/Microsoft.Dotnet.WinForms.ProjectTemplates/tests/README.md)
selects repository-built framework DLLs and matching private dependencies solely for controlled
consumer validation. It does not establish an approved external SDK/reference/runtime contract.

| Local evidence | Result |
|---|---|
| Arcade build/pack of templates and current hosting product/dependencies | Passed |
| Isolated packed-artifact install/create/uninstall, manifests, unique settings IDs, unsupported framework rejection, classic byte preservation | Passed |
| Four generated project builds: simple and spaced/dotted names in each language, warnings as errors | Passed |
| Native C#/VB fixture builds and five runtime scenarios per language | Passed: ten scenarios |
| Actual default-entry-point publishing, C#/VB, framework-dependent/self-contained win-x64 | Passed: four publishes |
| Published application launch from another working directory, configured title, ordinary window close, exit zero | Passed: four runs |

Runtime scenarios cover deployment JSON, environment/CLI precedence, disabled watchers, deferred
settings storage and persistence, invalid options, inert parameterless construction, actual generated
startup/UI close, and host-origin stop. Constructor isolation is not a Visual Studio Designer test.
The guide records commands, the framework-train override, isolation requirements, and watchdogs.

Shipping/API/package approval, an installed matching framework experience, actual Visual Studio
discovery and out-of-process Designer round trips, additional locales/architectures, clean-machine
deployment, and remote CI remain open. Full template CI, exploratory coverage, and performance
remain owned by their later children. No Hybrid implementation or adoption samples are included;
the previously documented Hybrid asynchronous-cleanup prerequisite is unchanged.

## Hybrid implementation readiness (dotnet/winforms#14967)

The implementation entry check reconfirmed the asynchronous-cleanup prerequisite against
[released MAUI 10.0.120](https://github.com/dotnet/maui/blob/10.0.120/src/BlazorWebView/src/WindowsForms/BlazorWebView.cs)
and [the latest change to the control on main](https://github.com/dotnet/maui/blob/130525230b0287ec12a058aa5a38bd2af73c939a/src/BlazorWebView/src/WindowsForms/BlazorWebView.cs).
Both implement `Dispose(bool)` by scheduling and observing asynchronous manager disposal, then
calling base disposal inside that operation. Neither exposes that operation as a public task for
the application to await. `TryDispatchAsync` remains a work-dispatch API, not a cleanup completion
contract.

At the entry check, the [WinForms runner](../../src/System.Windows.Forms/Microsoft/Extensions/WinForms/WinFormsApplication.cs)
provided no asynchronous pre-dispatcher-teardown hook. Host-origin stop requested application
exit and posted quit; final cleanup tore down the thread context, synchronously disposed startup
resources, and then permitted host shutdown. Canceling a Form's closing event could not defer that
committed host-origin stop. The initial internal prototype below changes that behavior only for
opt-in registrations in a bounded scenario; it does not establish a public integration contract.

This is source evidence of an unresolved integration contract, not a newly reproduced runtime
failure or a claim that every package version is affected. The subsequent protocol investigation
also traced NuGet package `11.0.0-rc.2.26505.5` through its
[package manifest](https://api.nuget.org/v3-flatcontainer/microsoft.aspnetcore.components.webview.windowsforms/11.0.0-rc.2.26505.5/microsoft.aspnetcore.components.webview.windowsforms.nuspec)
to [MAUI commit d32b799](https://github.com/dotnet/maui/blob/d32b79928709cf53b671336dabd0caef5a679fc2/src/BlazorWebView/src/WindowsForms/BlazorWebView.cs).
That exact control source likewise discards the dispatcher task from `Dispose(bool)` and exposes
no cleanup completion task. Unlike the reviewed main source, this revision does not attach
`ObserveExceptionsAsync` at that call. The package has not been built or run here and is not selected
as a validated Hybrid dependency.

No discoverable Hybrid template or dependency is added while this gate is unresolved. Before
supported template implementation proceeds, WebView/lifetime owners must approve a public protocol that keeps UI
dispatch and the host provider alive through successful asynchronous cleanup and reports cleanup
failures. Any required foundation API change must be reviewed separately rather than hidden in
template-generated code.

The next focused validation must use a component and renderer-scoped service whose asynchronous
disposal can be deliberately held and released. For user close, host stop, and partial startup
failure, prove cleanup completes with UI dispatch available before root-service disposal; also
exercise cleanup failure and repeated stop requests. Only after that protocol is demonstrated
should the template add shared-root composition, navigation policy, local assets, WebView2
prerequisites, Designer isolation, and build/run/publish checks.

### Public protocol investigation and proposal for approval

The approved investigation is documentation-only. The following API sketches are proposals,
not available contracts or permission to implement framework changes in the template child.

The preview package manifest selects shared WebView package `11.0.0-rc.1.26425.128` and WebView2
`1.0.4078.44`. The shared package's
[manifest](https://api.nuget.org/v3-flatcontainer/microsoft.aspnetcore.components.webview/11.0.0-rc.1.26425.128/microsoft.aspnetcore.components.webview.nuspec)
pins [WebViewManager](https://github.com/dotnet/dotnet/blob/3551975be08744f0418857c5bed8ab1545c5dd47/src/aspnetcore/src/Components/WebView/WebView/src/WebViewManager.cs)
and [PageContext](https://github.com/dotnet/dotnet/blob/3551975be08744f0418857c5bed8ab1545c5dd47/src/aspnetcore/src/Components/WebView/WebView/src/PageContext.cs).
The manager awaits page cleanup; page cleanup disconnects JS interop, awaits renderer disposal,
then awaits its asynchronous service scope. The required completion boundary therefore includes
renderer and renderer-scoped services, not merely the native browser control.
Do not promise that arbitrary JS interop is available during component disposal: this revision
deliberately marks it disconnected.

| Existing public seam | Why it does not establish the required protocol |
|---|---|
| `BlazorWebView.Dispose()` and `Disposed` | Dispose returns before asynchronous work completes. A Disposed-event adapter has no guaranteed failure result, startup-race handling, or promise that parent/child HWND destruction waits. |
| `TryDispatchAsync(Action<IServiceProvider>)` | Completes dispatched work against a live page scope, not manager disposal. An async lambda converted to Action would be async void. |
| Root-component collection removal | The control schedules internal asynchronous removal without exposing a join; removing a component is not disposal of the page service scope or manager. |
| `FormClosing` cancellation | Can delay an ordinary user close, but cannot delay the runner's committed host-stop quit. A single Form handler is not an application-wide cleanup barrier. |
| Custom `ApplicationContext.ExitThreadCore` | Does not prevent Form destruction that has already occurred and cannot suppress the runner's explicit posted quit for host stop. |
| `ApplicationStopping` event or Generic Host stopping token | Synchronous notification, not awaitable cleanup. An async event handler returns at its first incomplete await. |
| Hosted service `StopAsync` | Runs after the runner's UI completion barrier; it is too late for UI-dispatched cleanup. |
| Implement `IAsyncDisposable` on a generated Form/context | The current runner invokes synchronous disposal after the loop; implementing an interface does not change that call or ordering. |

#### Proposed upstream WebView contract

Ask the MAUI/WebView owners to provide a supported awaitable disposal operation on
`BlazorWebView`, preferably `IAsyncDisposable.DisposeAsync()` (final API subject to their review).
Required semantics:

- Start on the control's owning UI thread with dispatch available. Completion joins any in-flight
  initialization, stops new navigation/render work, and disposes the manager, renderer/page scopes,
  and underlying native control in the supported order. If initialization never began, cleanup is
  still well-defined.
- Concurrent/repeated calls join one operation. Mixing synchronous and asynchronous disposal must
  not double-dispose or produce an early success result. Parent Form destruction must follow the
  awaited operation, not race it.
- Return cleanup failures to the caller, including failures otherwise routed to renderer/browser
  error handling where the supported boundary permits. Attempt remaining owned cleanup after a
  failure; do not skip service-scope cleanup simply because renderer disposal failed. The inspected
  `PageContext` currently awaits these sequentially without a finally protecting the second call.
- Borrow `Services`; never dispose the application's root provider. Do not require template code
  to access the private manager or reconstruct renderer ownership.

An alternative public `CloseAsync`/completion contract is acceptable if its owners guarantee the
same resource, failure, initialization, and dispatch ordering. Observing current implementation
details through an inherited event is not equivalent to that guarantee.

#### Proposed WinForms runtime contract

Ask the Application Builder/lifetime owners for an opt-in asynchronous UI shutdown phase before
Form/control destruction and dispatcher teardown. An illustrative registration shape is
`WinFormsApplication.RegisterUiShutdown(Func<ValueTask> shutdown)`; naming, registration lifetime,
and placement on the application versus builder require API review. Registration must be possible
before the corresponding asynchronous UI resource begins activation.

Required behavior, not merely an extra event:

- Coordinate accepted user close, `StopAsync`, host `StopApplication`, run cancellation, and
  supported application-exit paths through one shared stop operation. Preserve cancellation of an
  ordinary user close before commitment. A committed stop remains non-vetoable but waits for
  registered cleanup instead of posting quit immediately.
- Invoke callbacks once on the owning STA while the existing main loop continues dispatching.
  Do not block the UI thread, introduce a template-owned pump, or call `Application.DoEvents`.
  While cleanup awaits, repeated stop requests join it and do not dispose Forms independently.
- Keep the Forms, dispatch controls, host provider, and required host services available until
  all callbacks finish. Then dispose the UI, mark UI completion, and continue existing host
  stop/disposal. This does not undo the host's already-signaled ApplicationStopping token.
- Handle partial startup and failure before the main loop begins explicitly. A callback awaiting
  UI dispatch cannot simply be awaited from the current synchronous finally block; the runtime
  owners must define a supported dispatch-capable path or prohibit resource activation until the
  normal loop can provide it. Do not conceal this case with nested pumps or blocking waits.
- Aggregate/report callback failures through the existing run/stop failure channel, attempt the
  remaining cleanup, and preserve non-success exit semantics. Canceling a StopAsync caller's wait
  must not cancel the shared cleanup or falsely signal safe provider disposal. A timeout is a
  diagnostic/escalation decision, not proof cleanup completed.

Direct premature `Form.Dispose`, abrupt process termination, and OS-forced shutdown cannot be
advertised as graceful asynchronous cleanup. The owners must define supported exit paths and any
restrictions, including modal/owned Forms, before the template promises coordinated lifetime.
Applications without registrations must retain their current close and startup behavior.

With both contracts approved and implemented, generated composition would register the WebView's
awaitable cleanup once and reuse the runner's phase. It would still build only one host/root and
would not implement its own shutdown state machine. Adding only one of these contracts is
insufficient: an awaitable WebView still needs a live dispatcher, and a runtime deferral still
needs a trustworthy WebView completion/failure result.

#### Approval and validation boundary

Recommended next step: approve a separately scoped foundation design/prototype covering both
owners, then implement and test the protocol before resuming the discoverable Hybrid template.
Approval to investigate did not approve the API sketches, a runtime behavior change, a package
selection, or an upstream issue/PR submission. The proposal itself changed no foundation code.
The subsequent implementation request authorized the initial internal prototype below, not
upstream submissions or a shipping/public API approval.

Use completion-source-controlled asynchronous disposal, not timing delays, to test one cleanup
invocation, a still-responsive STA, live root services while cleanup is held, and the ordering
`component/page cleanup -> UI disposal -> host stop/disposal`. Cover successful cleanup, component
and scoped-service failure, initialization failure, stop before activation, cancellation of an
individual stop waiter, repeated/reentrant user/host stop, and unchanged non-Hybrid behavior.
An actual pinned WebView package and WebView2 runtime must then pass the same ordering checks:
a fake cleanup callback validates the runtime seam, not WebView integration.

### Initial internal runtime prototype

The first implementation prototypes only the WinForms side, using internal
`RegisterUiShutdown(Func<ValueTask>)` registration rather than adding an unreviewed public API.
There is no WebView wrapper, copied upstream control, Hybrid template, or new package dependency.
The WebView completion contract and eventual public API review remain external gates.

Registration is deliberately restricted to normal UI message dispatch with the standard
`ApplicationContext` and a nonmodal, non-MDI, non-owned main Form. Register before activating the
resource in that dispatched operation; do not activate asynchronous UI resources in the context
factory, Form constructor, or pre-loop started notification. This bounded prototype does not solve
asynchronous cleanup for resources activated before normal dispatch, custom contexts, or all
modal/multi-UI-thread exit combinations.

Accepted main-Form close, application exit on the owning thread, standard-context/thread exit,
runtime stop, host stop, and run cancellation defer destruction while callbacks execute on the
existing UI synchronization context. Ordinary close cancellation occurs before commitment.
Callbacks run once in registration order; failures are recorded without skipping later callbacks.
Repeated stop requests join shutdown; canceling an individual wait does not cancel cleanup.
The existing host/UI barrier is released only after the cleanup phase and synchronous UI disposal.

The prototype does not treat arbitrary message-loop loss as successful cleanup. Direct disposal,
posted WM_QUIT, or an exception escaping and terminating the pump can bypass graceful deferral;
pending registrations then add an explicit run failure. This diagnostic is not a recovery protocol
and does not prove asynchronous work completed. Application-owned resource-startup operations must
route recoverable failure into the coordinated stop path while dispatch remains available.
If cleanup never completes, graceful shutdown continues waiting; there is no invented timeout
that permits unsafe provider disposal.

Resource messages use the existing localization pipeline; generated translation entries require
the normal localization review. Runtime tests use controlled asynchronous callbacks and the real
Generic Host, not a WebView substitute presented as browser validation. Actual WebView integration,
partial startup before dispatch, public API approval, and clean-machine/Designer/CI checks remain
required before dotnet/winforms#14967 can be called complete.

#### Local prototype validation

The targeted Arcade build passed with zero warnings/errors. The selected unit runs passed
199 cases with no failures or skips: 137 Application Builder cases (including 21 new shutdown
cases), 27 legacy ApplicationContext cases, and 35 legacy Application.Exit cases. New cases cover
eight graceful shutdown sources under both thread-context implementations, an application context
without a main Form, synchronous/asynchronous cleanup failures with continued cleanup, user-close
cancellation, canceled/repeated stop waits, registration guards, and explicit forced-quit failure.
These are controlled runtime tests, not actual WebView/Hybrid acceptance.

Validation used private C-drive artifacts because the repository drive was nearly full. Existing
bin/obj outputs were copied to that test-owned directory before building; shared caches and source
project settings were not changed. Reproduce the build and selected tests from the repository root:

```powershell
$artifacts = Join-Path $env:TEMP 'WinForms14967Artifacts'
.\build.cmd -projects src\test\unit\System.Windows.Forms\System.Windows.Forms.Tests.csproj `
    -verbosity minimal -binaryLogName "$artifacts\UiShutdown.binlog" "/p:ArtifactsDir=$artifacts"
$env:DOTNET_ROOT = "$PWD\.dotnet"
$env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
$tests = Join-Path $artifacts 'bin\System.Windows.Forms.Tests\Debug\net12.0-windows7.0\System.Windows.Forms.Tests.exe'
& $tests --filter-class 'System.Windows.Forms.Tests.WinFormsApplication*' --parallel none --progress off
& $tests --filter-class 'System.Windows.Forms.Tests.ApplicationContextTests' --parallel none --progress off
& $tests --filter-method 'System.Windows.Forms.Tests.ApplicationTests.Application_Exit*' --parallel none --progress off
```

Each command passed locally. Test execution used process-specific watchdogs; logs and TRX results
remain in the test-owned artifact directory. Remote CI was not run. No dependency package was
changed, no new public API was added, and no completion claim is made for the full Hybrid issue.

## Research sources

Local links above identify the inspected repository seams. External source links pin the reviewed
revisions where available; tutorials describe patterns, not approval of this feature.

| Source | Evidence used |
|---|---|
| [WPF metadata](https://github.com/dotnet/wpf/blob/2fe1225fe57195d91cd8b8ad5ade5fdc7eeca7fd/packaging/Microsoft.Dotnet.Wpf.ProjectTemplates/content/WpfApplication-CSharp/.template.config/template.json), [App.xaml](https://github.com/dotnet/wpf/blob/2fe1225fe57195d91cd8b8ad5ade5fdc7eeca7fd/packaging/Microsoft.Dotnet.Wpf.ProjectTemplates/content/WpfApplication-CSharp/net6.0/App.xaml), [code-behind](https://github.com/dotnet/wpf/blob/2fe1225fe57195d91cd8b8ad5ade5fdc7eeca7fd/packaging/Microsoft.Dotnet.Wpf.ProjectTemplates/content/WpfApplication-CSharp/net6.0/App.xaml.cs) | Grouped project metadata and native desktop startup |
| [ASP.NET minimal startup](https://github.com/dotnet/aspnetcore/blob/66e2adb4e0297e445686cb7a70d6c9aa84dcf3af/src/ProjectTemplates/Web.ProjectTemplates/content/EmptyWeb-CSharp/Program.cs), [Worker startup](https://github.com/dotnet/aspnetcore/blob/66e2adb4e0297e445686cb7a70d6c9aa84dcf3af/src/ProjectTemplates/Web.ProjectTemplates/content/Worker-CSharp/Program.cs), [Worker metadata](https://github.com/dotnet/aspnetcore/blob/66e2adb4e0297e445686cb7a70d6c9aa84dcf3af/src/ProjectTemplates/Web.ProjectTemplates/content/Worker-CSharp/.template.config/template.json) | Visible host ownership, options, restore post-action |
| [Blazor metadata](https://github.com/dotnet/aspnetcore/blob/66e2adb4e0297e445686cb7a70d6c9aa84dcf3af/src/ProjectTemplates/Web.ProjectTemplates/content/BlazorWeb-CSharp/.template.config/template.json), [startup](https://github.com/dotnet/aspnetcore/blob/66e2adb4e0297e445686cb7a70d6c9aa84dcf3af/src/ProjectTemplates/Web.ProjectTemplates/content/BlazorWeb-CSharp/BlazorWebCSharp.1/Program.cs) | Conditional project/solution output versus server-specific composition |
| [MAUI Hybrid metadata](https://github.com/dotnet/maui/blob/4c747015a9751840632fddba67cf86c5c7ca269e/src/Templates/src/templates/maui-blazor/.template.config/template.json), [startup](https://github.com/dotnet/maui/blob/4c747015a9751840632fddba67cf86c5c7ca269e/src/Templates/src/templates/maui-blazor/MauiProgram.cs) | Single application builder and Blazor service registration |
| [WinForms Hybrid tutorial](https://github.com/dotnet/AspNetCore.Docs/blob/3693f2731062e678620df08e9c029c1acada66db/aspnetcore/blazor/hybrid/tutorials/windows-forms.md) | Razor SDK, WindowsForms WebView package, local content; Form-local provider is not the selected architecture |
| [WebView control, reviewed main](https://github.com/dotnet/maui/blob/4c747015a9751840632fddba67cf86c5c7ca269e/src/BlazorWebView/src/WindowsForms/BlazorWebView.cs), [released 10.0.120](https://github.com/dotnet/maui/blob/10.0.120/src/BlazorWebView/src/WindowsForms/BlazorWebView.cs), [shared manager](https://github.com/dotnet/aspnetcore/blob/66e2adb4e0297e445686cb7a70d6c9aa84dcf3af/src/Components/WebView/WebView/src/WebViewManager.cs) | Provider assignment, Designer guard, dispatcher-scheduled cleanup, renderer scopes and asynchronous disposal |
| [.NET template authoring](https://github.com/dotnet/docs/blob/5aeb56fb23d8d89872a9d9b229970e4f33b8c2db/docs/core/tools/templates.md), [template packaging](https://github.com/dotnet/docs/blob/5aeb56fb23d8d89872a9d9b229970e4f33b8c2db/docs/core/tutorials/cli-templates-create-template-package.md) | Project/item/solution distinction, NuGet content/metadata, CLI/VS discovery, localization |
| [Install semantics](https://github.com/dotnet/docs/blob/faeef415d2a42d775f02db3a768d5c51300df29f/docs/core/tools/dotnet-new-install.md), [update semantics](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-new-update) | Version-pinned installation, trust, and installed-template-package updates |
| [VB JSON limitations](https://github.com/dotnet/docs/blob/156931bb4ec1e81b028c76ea983553f2e9778bdd/docs/standard/serialization/system-text-json/visual-basic-support.md) | Avoid unsupported ref-struct/converter paths in VB; disclose language boundaries |
| [WebView2 distribution](https://github.com/MicrosoftDocs/edge-developer/blob/670ae635fa4287947d5ba1face1378dd9f7a4f4b/microsoft-edge/webview2/concepts/distribution.md) | Separate browser runtime prerequisite, Evergreen/Fixed Version and offline deployment responsibilities |
