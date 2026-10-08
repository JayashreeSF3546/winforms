# Windows Forms Projects Templates

We provide the following templates:
* [Windows Forms app (C#)](./content/WinFormsApplication-CSharp)
* [Windows Forms app (VB)](./content/WinFormsApplication-VisualBasic)
* [Windows Forms app with Application Builder (C#)](./content/WinFormsApplicationBuilder-CSharp)
* [Windows Forms app with Application Builder (VB)](./content/WinFormsApplicationBuilder-VisualBasic)
* [Windows Forms class library (C#)](./content/WinFormsLibrary-CSharp)
* [Windows Forms class library (VB)](./content/WinFormsLibrary-VisualBasic)
* [Windows Forms control library (C#)](./content/WinFormsControlLibrary-CSharp)
* [Windows Forms control library (VB)](./content/WinFormsControlLibrary-VisualBasic)

# Item templates are located in the VSProject repo
* [VB](https://devdiv.visualstudio.com/DevDiv/_git/VS?path=/src/vsproject/Templates/Windows/VisualBasic/ItemTemplates/WindowsForms&version=GBmain&_a=contents)
* [C#](https://devdiv.visualstudio.com/DevDiv/_git/VS?path=/src/vsproject/Templates/Windows/CSharp/ItemTemplates&version=GBmain&_a=contents)

## Testing Templates

Before submitting a change to any of the templates please make sure to test them locally.

1. Install the required template with the `dotnet` tool (for example, `WinFormsApplication-CSharp`).<br />
In the command prompt of your choice run the following command:
    ```
    > dotnet new install <path to the repo>\winforms\pkg\Microsoft.Dotnet.WinForms.ProjectTemplates\content\WinFormsApplication-CSharp
    ```

2. To confirm that the template is correctly installed run the following command and look for your template:
    ```
    > dotnet new uninstall
    ```
    ![image](../../docs/images/templates-check-installed.png)

3. Create an app from your template:
    ```
    > dotnet new winforms -n testapp1
    ```

4. Verify the app behaves as expected. <br />
If necessary, tweak the template and create new apps.

5. Once you are happy with the template, uninstall it using the path shown by `dotnet new uninstall`; e.g.
    ```
    > dotnet new uninstall <path to the repo>\winforms\pkg\Microsoft.Dotnet.WinForms.ProjectTemplates\content\WinFormsApplication-CSharp
    ````

## Application Builder templates

The Application Builder project templates use the distinct `winforms-builder`
short name so they do not replace the existing `winforms` templates. Install
both language templates from their source folders, then choose the language
when creating a project:

```powershell
dotnet new install .\pkg\Microsoft.Dotnet.WinForms.ProjectTemplates\content\WinFormsApplicationBuilder-CSharp
dotnet new install .\pkg\Microsoft.Dotnet.WinForms.ProjectTemplates\content\WinFormsApplicationBuilder-VisualBasic
dotnet new winforms-builder --language C# --name BuilderSample
dotnet new winforms-builder --language VB --name BuilderSampleVB
```

The generated projects use the current Application Builder APIs from the
target .NET WinForms framework and the `Microsoft.Extensions.Hosting` package.
They include a Designer-compatible startup Form, typed greeting options,
structured logging through `ILogger<T>`, and per-user JSON settings. A custom
user-settings file location and application identifier can be configured in
the generated code if needed.
