# Application Builder template exploratory testing

**Issue:** [#14970](https://github.com/dotnet/winforms/issues/14970)  
**Status:** Partial; environment-specific manual checks remain outstanding.

## Test environment

- Windows x64 workstation in a Console session; not a Terminal Services host.
- Repository SDK `12.0.100-alpha.1.26480.102`, with the repository targeting `net11.0`.
- Visual Studio 18 Professional and the WebView2 Runtime are installed.
- No native ARM64 device or disconnected network environment was available.

## Results

| Scenario | Result | Evidence and limits |
|---|---|---|
| `dotnet new` CLI | Pass | The #14969 validator generated all nine supported C#, VB, and Blazor Hybrid variants and checked required and unexpected output files. |
| Restore, build, and publish | Pass | The #14969 validator restored, built, and published all nine variants. Applications were published framework-dependent and self-contained; libraries were published framework-dependent. |
| Application startup | Partial | The #14969 validator ran classic C# and VB apps through a bounded startup smoke test. Builder and Blazor Hybrid apps were not launched because their APIs are absent from the installed framework runtime. |
| Visual Studio project build | Pass | A generated C# WinForms project built through Visual Studio 18 Professional's command-line build. Visual Studio template discovery was not checked. |
| Visual Studio Designer | Not verified | An automated Designer-open attempt was rejected by the active Visual Studio COM instance (`RPC_E_CALL_REJECTED`). No Designer result is claimed. |
| Accessibility | Not run | Keyboard navigation, UI Automation, high contrast, and screen-reader behavior were not manually assessed. |
| ARM64 | Cross-publish passed | Classic C# and VB apps published for `win-arm64` in framework-dependent and self-contained modes. They could not be executed on this x64 host. |
| Terminal Services | Not run | This machine is a workstation in a Console session, not a Terminal Services or Remote Desktop test environment. |
| Self-contained startup | Partial | A self-contained x64 classic app stayed running with `DOTNET_ROOT` pointed at a nonexistent directory. Network access was not disabled, so this is not a complete disconnected-network test. |
| Package restore | Warm-cache offline restore passed | Blazor Hybrid restored from the populated global package cache while pointed at an empty package source. Cold-cache and proxy/authentication scenarios were not tested. |
| Template reinstall and uninstall | Pass | The #14969 validator installed the local template source, reinstalled it with `--force`, and uninstalled it successfully. A version-to-version packaged upgrade was not tested. |
| Azure Pipelines | Not run | The PR pipeline change was not executed by Azure DevOps in this session. |

The repeatable template matrix is implemented in
[`ValidateProjectTemplates.ps1`](../../eng/scripts/ValidateProjectTemplates.ps1).
The run performed for this exploration completed successfully on the local x64
host.

## Findings and risks

- The VB Application Builder template initially failed compilation with
  `WFO3011` because several Designer fields lacked `WithEvents`. The declarations
  were corrected with the #14969 validation change, and the generated VB project
  now builds.
- The installed framework reference pack does not contain the in-progress
  Application Builder APIs. The automated build and publish checks therefore
  use the locally built WinForms reference assemblies; they do not prove that a
  released SDK or runtime contains those APIs.
- Successful cross-publishing does not establish native ARM64 runtime behavior.
  Likewise, the bounded startup smoke test does not substitute for Designer,
  accessibility, Remote Desktop, or disconnected-network testing.
- The WebView2 Runtime was present, but the Blazor Hybrid app was not launched;
  browser availability alone would not resolve the missing framework runtime
  APIs.

## Follow-up recommendations

1. Open the generated C# and VB forms in a clean Visual Studio instance; verify
   load, save, reopen, and basic control editing in the Designer.
2. Run keyboard-only, high-contrast, UI Automation, and screen-reader checks on
   the basic and Builder forms.
3. Run the published applications on native ARM64 and in a Terminal Services or
   Remote Desktop session.
4. Repeat self-contained startup with network access disabled, and test package
   restore with cold caches, restricted feeds, and proxy/authentication
   configuration.
5. Test an actual versioned template-package upgrade after old and new packages
   are available, and execute the added Azure Pipelines leg.
6. Once the Application Builder runtime is available to generated applications,
   run startup and shutdown smoke tests for Builder and Blazor Hybrid templates.
