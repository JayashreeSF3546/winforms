# WinForms Application Builder template strategy

**Status:** Matrix and packaging recommendations for [issue #14965](https://github.com/dotnet/winforms/issues/14965)

**Parent:** [Application Builder templates and adoption](https://github.com/dotnet/winforms/issues/14964) and [proposal #14082](https://github.com/dotnet/winforms/issues/14082)

## Goals and constraints

Templates are executable onboarding guidance, not a replacement for the
framework APIs or a promise that every architecture is supported by a
first-party project template. The initial set should be small, discoverable
from both `dotnet new` and Visual Studio, build on the Application Builder
contracts from streams A, B, and C, and keep generated code understandable
and Designer-friendly.

The existing .NET WinForms project templates remain the compatibility
baseline. The Application Builder experience is an explicit alternative for
new projects; it must not change the behavior or identity of the existing
`winforms` templates or force migration on existing applications.

## Research across .NET template experiences

| Workload | Current strategy | Decision relevant to WinForms |
|---|---|---|
| WinForms | The SDK provides C# and Visual Basic application templates and library templates. This repository contains their `.template.config` sources and a packaging project; item-template ownership is separate in the Visual Studio project repository. | Preserve the existing `winforms` identities and add a distinct Application Builder project-template identity and short name. Follow the existing paired-language and Designer-file conventions. |
| WPF | The SDK provides application, class-library, custom-control, and user-control project templates for C# and Visual Basic. | Keep project types separate when they have materially different output; do not make the Application Builder basic app replace standard desktop templates. |
| ASP.NET Core | The SDK ships a broad set of first-party project templates and exposes supported choices as template options. | Ship only stable, common WinForms startup composition in the basic template. Put opinionated architectures and optional integrations in samples or distinct templates. |
| .NET Generic Host | The `worker` template demonstrates `HostApplicationBuilder`, dependency injection, and `IHostedService`; the Generic Host itself is a reusable library, not a GUI project type. | Reuse the established host and service contracts, but do not use the Worker Service template as a WinForms starter or turn a GUI app into a worker. |
| .NET MAUI | MAUI templates are installed with the MAUI workload and Visual Studio components because they depend on workload-specific SDKs, platform tooling, and deployment targets. | Do not introduce a WinForms workload. WinForms targets Windows and should use the normal SDK/template distribution path. |
| Blazor | Blazor Web templates ship with the SDK. The WinForms Blazor Hybrid tutorial starts with a WinForms C# project and adds the Razor SDK, WebView package, component assets, and `BlazorWebView` composition. | Hybrid has materially different build, package, content, and runtime requirements, so keep it out of the basic template and provide a dedicated C# template in issue #14967. |

This comparison favors the .NET template engine rather than a WinForms-specific
scaffolding mechanism. The engine's project and solution templates can be
installed through a template package and are surfaced by the Visual Studio
**Create a new project** dialog. Visual Studio item templates are a separate
surface and are not part of this project-template matrix.

## Template and sample matrix

| Scenario | Delivery | Initial languages | Scope and rationale |
|---|---|---|---|
| Existing WinForms App (.NET) | Keep the in-box `winforms` project templates unchanged. | C#, Visual Basic | Compatibility baseline and simplest `Application.Run` experience. |
| WinForms App (Application Builder) | **First-class project template pair**; proposed CLI short name `winforms-builder`, distinct from `winforms`. | C#, Visual Basic | The minimum new-app path: builder, host lifetime, DI, standard configuration/options, user settings, and logging, with one small example service and a Designer-compatible startup Form. Implement in #14966. |
| WinForms + Services | Samples and documentation, not another basic project template. | C#, Visual Basic where the sample demonstrates language-specific startup | Service registration and configuration already belong in the basic Application Builder template. A second “services” template would duplicate the same startup structure without teaching a distinct architecture. |
| WinForms + Blazor Hybrid | Separate first-class project template. | C# initially | Razor SDK, Razor components, WebView package/content, and browser-runtime deployment make this a distinct composition with additional dependencies. Implement in #14967. |
| Incremental migration | Sample and migration guide. | C#, Visual Basic where appropriate | Existing project structure and legacy settings vary too much for a reliable one-click conversion or generic bridge solution. Demonstrate opt-in, incremental changes without rewriting the user's project. |
| Enterprise/layered application | Focused sample, not a general-purpose solution template. | C#; add a VB counterpart only where it teaches a distinct language scenario | Layering, persistence, testing, and MVVM choices vary by organization. Avoid imposing EF Core, a view-model framework, or a prescribed solution structure on all WinForms users. |
| System-tray / custom `ApplicationContext` | Focused sample. | C# and Visual Basic if both can show the same lifetime behavior | This is a specialized lifetime shape and is better taught through a small explicit example than a second default application template. |
| Backend or cloud integration | Optional sample/documentation, not a general-purpose template. | C# initially; language parity is not a requirement for provider-specific integrations | Cloud provider, authentication, connectivity, offline behavior, and infrastructure deployment are application-specific and can introduce credentials or costs. |
| Windows Service / background worker | Use the existing .NET Worker Service template; explain how a WinForms application can register background services where relevant. | Follow the built-in template's supported languages | A Windows Service is not a desktop UI application. Do not add service-installation behavior to a WinForms template. |
| MVVM, Azure, or multi-tier solution variants | Samples or third-party templates unless future usage demonstrates a stable, broadly useful product need. | As appropriate to the sample | These alternatives add dependencies and encode architectural choices beyond the small, supported WinForms startup model. |

No new solution template is recommended for the first delivery. A solution
template should be added only when it composes multiple independently useful
projects in a way that cannot be taught clearly by a focused sample. Issue
#14968 may refine the sample-versus-solution decision for migration and
enterprise scenarios, but it must not add templates outside its assigned
scope without review.

## Language support and parity

The basic Application Builder template is a paired C# and Visual Basic
offering. Both generated projects should have equivalent startup behavior,
service registrations, configuration, settings, logging, Designer workflow,
and supported template options. Differences should be syntax and normal
language-project conventions, not missing infrastructure capabilities.

The new VB template is an opt-in alternative; it does not disable, replace, or
change the existing Visual Basic Application Framework or its generated
`My Project` files. Its startup code should clearly use the shared builder
model while preserving existing VB applications unchanged.

The initial Blazor Hybrid template is C#-only because its generated Razor
components and current WinForms Hybrid onboarding path are C#-oriented. This
is a documented template limitation, not a limitation on the Application
Builder's use from Visual Basic. Do not claim a VB Hybrid template until its
generated project, component workflow, Designer experience, and publish path
are supported and tested.

## Generated-file ownership and optional features

- The template owns the entry point, project file, basic configuration files,
  and the small sample service. The entry point owns builder creation,
  registrations, startup-form selection, and application run/disposal.
- Visual Studio owns generated `*.Designer.cs` and `*.Designer.vb` files.
  Keep the Form partial-class layout and `InitializeComponent` convention;
  runtime service activation must not add service construction or registrations
  to Designer-generated files.
- Keep a Designer-safe parameterless construction path where required by the
  Designer. Resolve runtime dependencies through the supported builder
  activation path, not through a process-wide service locator.
- Exclude template metadata and build outputs from generated applications.
  Generated files should be ordinary projects that remain understandable and
  editable after template installation is removed.
- Keep the basic template lean: no cloud SDK, database provider, MVVM
  framework, telemetry backend, or background worker unless a chosen template
  scenario requires it. Put materially different deployment/runtime
  requirements, especially Blazor Hybrid, in a separate template.

## Package, SDK, Visual Studio, and versioning strategy

### Authoring and delivery

Use the existing .NET template engine format (`.template.config/template.json`)
and the WinForms template source/packaging project under
[`pkg/Microsoft.Dotnet.WinForms.ProjectTemplates`](../../pkg/Microsoft.Dotnet.WinForms.ProjectTemplates/readme.md).
Place the new project templates beside the existing WinForms project
templates, with unique identities and a distinct short name so they do not
shadow the built-in `winforms` template. Keep one SDK-owned template
distribution for the C#/VB pair and Hybrid template rather than creating one
package per language or scenario; the intended product experience is an
in-box SDK/Visual Studio template, not a separately maintained public NuGet
package.

The current repository project includes template content but declares
`IsShipping=false`; building that project alone does not establish that a
template is included in a released SDK or Visual Studio installation. Use it
as the authoring/packaging input and coordinate the actual SDK ingestion and
in-box discovery with the .NET SDK and Visual Studio template owners before
release. Issue #14969 owns automated package/installation validation.

Do not create parallel `.vstemplate` project templates. The .NET template
engine is the common project-template authoring format for `dotnet new` and
Visual Studio. Existing Visual Studio **Add New Item** templates remain under
their separate ownership and are outside this issue.

### SDK and package dependencies

- The basic template targets only a Windows TFM for which the released
  Application Builder APIs are available. The current branch's samples target
  `net11.0-windows`; use the release branch's supported current WinForms TFM
  as the initial choice. Add older or newer target choices only after
  confirming API availability and validating generated output on those SDKs.
- Use the supported Windows Desktop SDK and the released WinForms framework
  APIs. Do not add a project reference to this repository or to an internal
  implementation assembly.
- Reference `Microsoft.Extensions.Hosting` when generated source consumes
  Generic Host, DI, configuration, options, or logging APIs. Use explicit,
  tested package versions aligned with the supported SDK/release train;
  do not use floating package versions.
- The Hybrid template alone adds
  `Microsoft.AspNetCore.Components.WebView.WindowsForms`, Razor SDK settings,
  component files, and static web assets. Its package/runtime prerequisites
  and deployment behavior must be documented with that template.
- Keep EF Core, MVVM, cloud, and other third-party dependencies out of the
  basic template. Samples may demonstrate them explicitly as optional choices.

### Versioning and upgrades

The official template package follows the .NET SDK/WinForms release train.
Each template has a stable, unique identity and short name; the template
generator version and supported target-framework choices must match the
template engine and APIs supported by that release. Preview templates must be
clearly marked and must not silently target unsupported runtime APIs.

Updating an installed template package affects future project creation; it
does **not** rewrite projects that were previously generated. Existing
applications opt in by following a migration guide and updating their own
project and source explicitly. Preserve the existing `winforms` template
experience and use distinct identities to avoid an upgrade becoming an
unrequested application migration.

## Adoption strategy

- **New applications:** keep the standard `winforms` choice intact and offer
  the Application Builder pair as an explicit, discoverable alternative.
  Generated code should be runnable after creation and show only the minimum
  registrations needed to demonstrate the supported infrastructure.
- **Existing applications:** provide incremental migration guidance and a
  sample. Users choose when to replace manual startup, add a host, adopt DI,
  or migrate settings; template installation never edits an existing project.
- **Enterprise adoption:** provide samples that can be adapted or used as the
  basis of private organizational templates. Do not prescribe a universal
  enterprise architecture or include organization-specific deployment and
  cloud assumptions in the first-party basic template.

## Decisions, alternatives, and follow-up

**Selected:** one paired C#/VB basic project template, one separate C# Hybrid
project template, and focused samples/documentation for services, migration,
enterprise, system-tray, and backend integration.

**Rejected for the initial matrix:** replacing the `winforms` short name;
one mega-template with many architecture switches; a new WinForms workload;
separate template packages per language; project templates for every sample;
and a generic migration or enterprise solution template. These options either
risk compatibility, multiply maintenance/test combinations, add installation
or deployment prerequisites, or make opinionated choices for users.

**Dependencies and ownership:**

- The Application Builder, host/lifetime, DI/scopes, and services contracts
  from streams A, B, and C are the foundation. Templates consume their
  released public APIs and must not add framework infrastructure in this
  stream.
- #14966 implements the approved basic C#/VB pair.
- #14967 owns the distinct WinForms Blazor Hybrid project template.
- #14968 owns migration, enterprise, system-tray, and backend samples.
- #14969 owns template generation, restore, build, publish, run, package
  update, and clean-install automation.
- #14970 owns exploratory Visual Studio, Designer, accessibility, session,
  architecture, deployment, and offline validation.
- #14971 owns generated-app performance and footprint measurements.

## References

Repository evidence:

- [WinForms template package README](../../pkg/Microsoft.Dotnet.WinForms.ProjectTemplates/readme.md)
- [WinForms template packaging project](../../pkg/Microsoft.Dotnet.WinForms.ProjectTemplates/Microsoft.Dotnet.Winforms.ProjectTemplates.csproj)
- Existing C# and Visual Basic `winforms` template metadata under
  `pkg/Microsoft.Dotnet.WinForms.ProjectTemplates/content`.

Official documentation:

- [.NET templates for authors](https://learn.microsoft.com/dotnet/core/tools/templates)
- [.NET SDK default templates](https://learn.microsoft.com/dotnet/core/tools/dotnet-new-sdk-templates)
- [`dotnet new install` and package version updates](https://learn.microsoft.com/dotnet/core/tools/dotnet-new-install)
- [.NET Generic Host](https://learn.microsoft.com/dotnet/core/extensions/generic-host)
- [Install the .NET MAUI workload](https://learn.microsoft.com/dotnet/maui/get-started/installation)
- [Build a Windows Forms Blazor app](https://learn.microsoft.com/aspnet/core/blazor/hybrid/tutorials/windows-forms)
