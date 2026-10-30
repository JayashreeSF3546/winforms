# Application Builder DI/scopes prototypes

These C# and VB samples belong to dotnet/winforms#14953. They exercise the **actual internal**
scope implementation through explicit sample-only friend-assembly grants. They are repository-only
prototypes, **not an approved public API or copy-ready external application template**.
No reflection, replacement scope coordinator, ambient provider, or generated service hookup is used.

## Build and run

From the repository root, build both samples and their smoke tests with Arcade:

```powershell
.\build.cmd -projects src\test\integration\UIIntegrationTests\System.Windows.Forms.UI.IntegrationTests.csproj -verbosity minimal
```

Run either language using the repository SDK:

```powershell
$env:DOTNET_ROOT=Join-Path $PWD '.dotnet'
.\.dotnet\dotnet.exe run --no-build --project src\test\integration\ApplicationBuilderScopes\CSharp\ApplicationBuilderScopes.CSharp.csproj -- --light
.\.dotnet\dotnet.exe run --no-build --project src\test\integration\ApplicationBuilderScopes\VisualBasic\ApplicationBuilderScopes.VisualBasic.vbproj -- --light
```

Options: `--classic`, `--light`, `--dark`, `--component-manager`, `--smoke`,
and `--cancel` (the last option requires `--smoke`). Classic omits visual-style initialization.
Light enables visual styles with Classic color mode; Dark enables visual styles and Dark color mode.
Color flags do not change the process's OS theme.

The console displays architecture, Terminal Services status, scope identities, typed assignment,
and component/UserControl/Form/service disposal. The executable intentionally keeps console output
available instead of requiring a debugger to see diagnostics.

## Interactive scenarios

- **Open modeless** creates an independently scoped form with an explicit logical owner.
  Close it and verify its session is released while the owner remains usable.
- **Open modal** creates a fresh dialog scope. Close the dialog; its UI and scope are disposed
  when the modal invocation unwinds, while the owner's services remain available.
- Open descendants from those windows, then close their owner. Logical descendant scopes
  must finish before the owner's service scope. Microsoft DI scopes themselves are flat.
- **Next page** replaces `ApplicationContext.MainForm`, shows the next page, and closes the old
  page without losing the new page's independent scope. Navigation is enabled only on root pages.
- **Cancel application** cancels `Run`, which cleans all tracked UI/scopes before reporting
  cancellation. It does not start background work or introduce another cancellation service.
- Close the last root window to complete normally. The final `PASS` line verifies that every
  resolved session was disposed exactly once; it is not a long-running leak-test result.

The form receives a typed constructor dependency. Its existing Designer-created `SessionControl`
and container-owned `SessionComponent` receive that same instance through explicit typed callbacks.
Each receiver rejects repeated initialization; the nonvisual component also rejects initialization
after disposal. No UI object stores a provider or creates its own scope.

`InitializeComponent` contains only ordinary construction and layout. Parameterless constructors
remain service-free. The service state is internal and absent from property browsing/serialization.
Diagnostics run during normal UI disposal while services are still alive. The scoped contract
remains **base-last disposal**, and scoped dependencies in this sample are synchronously disposable.
The host and application scope are owned by the builder, not by the forms.

## Automated coverage

```powershell
$env:DOTNET_ROOT=Join-Path $PWD '.dotnet'
& .\artifacts\bin\System.Windows.Forms.UI.IntegrationTests\Debug\net11.0-windows7.0\System.Windows.Forms.UI.IntegrationTests.exe `
  --filter-class 'System.Windows.Forms.UITests.WinFormsApplicationScopeSampleTests' `
  --no-progress --report-trx
```

The 24 process cases combine two languages, three color selections, two message-loop backends,
and normal/canceled completion. Each isolated process exercises modeless close, modal close,
navigation, typed child assignment, UI-before-service disposal, and exactly-once scope cleanup.
UI actions are event-driven; the process deadline is only a hang guard.
These cases are included in the existing integration-test project and public x64/x86 CI jobs.

Two additional local Designer cases construct the actual sample Forms without runtime services,
create and site their UserControl/component types through `IDesignerHost`, and reconstruct those
added objects through CodeDOM serialization. Referenced types and in-memory ResX metadata are
provided explicitly; no legacy binary serializer is enabled. These cases do not save/reload a
root source document or certify Visual Studio OOP behavior.

Local Debug/x64 result: the build command above passed with zero warnings/errors; the test command
above passed **26 cases**, with zero failures/skips. Targeted whitespace formatting verification
and `git diff --check` also passed. No remote CI or other architecture/configuration pass is claimed.

## Manual and external acceptance still required

Record versions, architecture, language, backend, source diffs, and results for each environment:

1. In the Designer team's agreed environment/harness, open the Form and UserControl without
   runtime services. Edit layout and drag/drop a control and nonvisual component. Save, close,
   reopen, rename, delete, undo, redo, and reload. Confirm parent/container relationships and
   ordinary generated code survive without service/provider/scope calls. **Actual VS OOP
   compatibility has not been certified**; these source-project prototypes do not by themselves
   establish a supported Designer project/SDK configuration.
2. Visually inspect Classic, light, and Dark Mode in both languages: labels, buttons, focus,
   resizing, DPI changes, nested windows, and cancellation.
3. Repeat interactive navigation/modal/modeless/owner-close/cancellation on native Windows ARM,
   in Terminal Services including disconnect/reconnect, and on representative slow hardware.
   Record responsiveness rather than inventing a fixed timing threshold.
4. Publish the changes and obtain CI results. Local process smoke tests are not remote CI,
   visual acceptance, or Designer-team approval.

Public API approval and Designer/OOP agreement remain dotnet/winforms#14951 gates.
Dedicated thousands-of-iterations/resource-trend testing belongs to dotnet/winforms#14954.
