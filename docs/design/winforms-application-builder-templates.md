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
