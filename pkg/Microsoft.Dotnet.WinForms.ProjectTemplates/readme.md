# Windows Forms Projects Templates

We provide the following templates:
* [Windows Forms app (C#)](./content/WinFormsApplication-CSharp)
* [Windows Forms app (VB)](./content/WinFormsApplication-VisualBasic)
* [Windows Forms class library (C#)](./content/WinFormsLibrary-CSharp)
* [Windows Forms class library (VB)](./content/WinFormsLibrary-VisualBasic)
* [Windows Forms control library (C#)](./content/WinFormsControlLibrary-CSharp)
* [Windows Forms control library (VB)](./content/WinFormsControlLibrary-VisualBasic)

## Application Builder prototype templates

* [Application Builder app (C#)](./content/WinFormsApplicationBuilder-CSharp)
* [Application Builder app (VB)](./content/WinFormsApplicationBuilder-VisualBasic)

The basic `winforms-builder` family is a prototype requiring the matching in-development
WindowsDesktop APIs and hosting package. It is not supported by installing this package on an
unmodified public SDK. Read the generated README before restoring or running it.

### Create a basic sample

From the repository root, build the hosting and template packages:

```powershell
.\build.cmd -projects src\Microsoft.Extensions.WinForms.Hosting\src\Microsoft.Extensions.WinForms.Hosting.csproj -pack
.\build.cmd -projects pkg\Microsoft.Dotnet.WinForms.ProjectTemplates\Microsoft.Dotnet.WinForms.ProjectTemplates.csproj -pack
```

Install the template for the current Windows user, then generate a C# sample:

```powershell
.\.dotnet\dotnet.exe new install .\artifacts\packages\Debug\NonShipping\Microsoft.Dotnet.WinForms.ProjectTemplates.12.0.0-dev.nupkg
.\.dotnet\dotnet.exe new winforms-builder --language C# --name BuilderDemo --output .\artifacts\samples\BuilderDemo --no-restore
```

For VB, use `--language VB` and a different name/output directory. To replace a previously
installed build of the same development package version, first uninstall it, then repeat the
install command above:

```powershell
.\.dotnet\dotnet.exe new uninstall Microsoft.Dotnet.WinForms.ProjectTemplates
```

Avoid `new install --force` with this preview SDK: it can leave duplicate package registrations
and make creation fail with `Sequence contains more than one matching element`.
Uninstalling the template does not delete applications already generated from it.

Open the generated `.csproj` or `.vbproj` in a compatible Visual Studio preview.
Visual Studio template discovery and Designer acceptance still require separate verification.

The generated entry point demonstrates `builder.Services.AddWinFormsUserSettingsService`,
`AddWinFormsExceptionService`, WinForms Debug logging, and fluent startup configuration.
It follows commit `63577553` directly: `UseHost(new HostBuilder())`, a parameterless
`UseStartupForm<Form1>()`, and one `StartupPreferences` model with `UseCompactLayout`.
There is no extra options model, configuration file, constructor injection, or context factory.

`--no-restore` creates source only. Running the application still requires matching prototype
framework packs and the hosting package. For repository-local validation, use the external
[prototype consumer harness](./tests/README.md#explicit-prototype-consumer-harness);
do not add its test-only overrides to the template or treat a public SDK as sufficient.

The C#/VB parity, package prerequisites, Hybrid lifetime gates, and sample boundaries are defined in the
[template and sample matrix](../../docs/design/winforms-application-builder-templates.md).
Hybrid templates and adoption samples are not included in this child.

The [gated C# Hybrid scaffold](./scaffolds/WinFormsApplicationBuilder-Hybrid-CSharp) stages source
for dotnet/winforms#14967 outside the packed content. Its metadata is intentionally not discoverable,
and runtime activation is disabled until the public lifecycle contracts are approved and integrated.
It is not a runnable Hybrid template or a WebView support claim.

Build the package with the repository's `build.cmd -projects` packaging workflow, then use PowerShell 7
to run the
focused creation/manifest checks against its actual `.nupkg`:

```powershell
.\pkg\Microsoft.Dotnet.WinForms.ProjectTemplates\tests\Test-BasicTemplates.ps1 -TemplatePackage <built-package-path>
```

The checks use an isolated template-engine hive, preserve classic payloads byte-for-byte, generate
native C#/VB projects (including names with spaces/dots), reject unsupported frameworks, and
uninstall from that hive. They do not claim installed-framework compatibility or replace the
later build/run/publish CI matrix.

The [local validation guide](./tests/README.md) documents the separate prototype consumer harness,
native-language runtime checks, publish checks, results, and remaining acceptance gates.

# Item templates are located in the VSProject repo
* [VB](https://devdiv.visualstudio.com/DevDiv/_git/VS?path=/src/vsproject/Templates/Windows/VisualBasic/ItemTemplates/WindowsForms&version=GBmain&_a=contents)
* [C#](https://devdiv.visualstudio.com/DevDiv/_git/VS?path=/src/vsproject/Templates/Windows/CSharp/ItemTemplates&version=GBmain&_a=contents)

## Testing Templates

Before submitting a change to any of the templates please make sure to test them locally.

1. Install the required template with `dotnet` tool (e.g. `WinFormsApplication-CSharp`).<br />
In the command prompt of your choice run the following command:
    ```
    > dotnet new -i <path to the repo>\winforms\pkg\Microsoft.Dotnet.WinForms.ProjectTemplates\content\WinFormsApplication-CSharp
    ```

2. To confirm that the template is correctly installed run the following command and look for your template:
    ```
    > dotnet new -u
    ```
    ![image](../../docs/images/templates-check-installed.png)

3. Create an app from your template:
    ```
    > dotnet new winforms -n testapp1
    ```

4. Verify the app behaves as expected. <br />
If necessary, tweak the template and create new apps.

5. Once you are happy with the template, you can uninstall it to revert to the original by running the command given in `dotnet new -u` output; e.g.
    ```
    > dotnet new -u <path to the repo>\winforms\pkg\Microsoft.Dotnet.WinForms.ProjectTemplates\content\WinFormsApplication-CSharp
    ````
