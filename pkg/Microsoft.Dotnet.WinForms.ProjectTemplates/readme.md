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

The C#/VB parity, package prerequisites, Hybrid lifetime gates, and sample boundaries are defined in the
[template and sample matrix](../../docs/design/winforms-application-builder-templates.md).
Hybrid templates and adoption samples are not included in this child.

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
