# Desktop infrastructure sample

These C# and VB front ends implement dotnet/winforms#14962 against the **repository prototype**.
Both have native Forms and entry points; the sample-only C# library shares service composition,
explicit JSON metadata, background operations, and automation. It is not a new shipping library.
No global service provider, private API, friend grant, second message loop, network request,
file-logging package, or application scheduler is introduced.

## Build and run

From the repository root:

```powershell
.\build.cmd -projects src\test\integration\UIIntegrationTests\System.Windows.Forms.UI.IntegrationTests.csproj -verbosity minimal -binaryLogName InfrastructureSamples.binlog
$env:DOTNET_ROOT = Join-Path $PWD '.dotnet'
& .\artifacts\bin\ApplicationBuilderInfrastructure.CSharp\Debug\net12.0-windows7.0\ApplicationBuilderInfrastructure.CSharp.exe --environment=Development
& .\artifacts\bin\ApplicationBuilderInfrastructure.VisualBasic\Debug\net12.0-windows7.0\ApplicationBuilderInfrastructure.VisualBasic.exe --environment=Development
```

The required preview .NET runtime must be available. The project graph copies matching repository
WinForms binaries beside each executable and disables transitive installed framework references.
**Do not replace those binaries with an ordinary installed WinForms runtime**: its Application
Builder contracts do not match this prototype. Building/publishing here is not a supported
standalone NuGet consumer, production installer, or shipping-package compatibility claim.

The console shows allowlisted operation outcomes, environment, process architecture, and Terminal
Services status. Debug structured logging requires a debugger/Debug listener; console messages are
independent sample status, not a second logging provider. No file durability or universal logger
flush is promised. The outer caller-owned diagnostics factory outlives `Run`; the attachment is
disposed and `DiagnosticCompletion` joined before that factory is disposed. Hosted logging is owned
by the host. A provider failure is reported independently to stderr.

## Configuration and preferences

- `appsettings.json` supplies the caption, bounded worker interval, and explicit offline mode.
  `appsettings.Development.json` changes the caption in Development. Standard environment variables
  such as `Sample__Caption` and command-line `--Sample:Caption=value` override JSON. The content root
  defaults to the executable directory, not the working directory.
- Options are eagerly validated **before UI activation**. An interval outside 1-30000 milliseconds
  exits unsuccessfully with a sanitized startup error.
- Change the deployed JSON and click **Reload** to reload standard configuration and validate
  monitored options. The caption/status and next health request use monitored values; the worker's
  startup interval requires a restart. Invalid reloads report failure, not success; fix the file and
  reload again. Automatic file watching is disabled by the sample's host-configuration default so
  invalid edited options are handled by this explicit operation rather than a watcher callback.
  Explicitly enabling watchers adopts the standard provider/callback error contract.
- Both languages deliberately share the stable identity `ApplicationBuilderInfrastructureSample`.
  The default primary is
  `%LOCALAPPDATA%\WinForms\ApplicationBuilderInfrastructureSample\settings.json`.
  Deployment moves do not change this identity. The value is not a secret and this is not encrypted
  credential storage. No write permission in the installation directory is needed for preferences.
- For disposable experiments, pass `--Sample:StorageDirectory=C:\absolute\isolated\directory`.
  Never point automated probes at real user data. An invalid override or denied storage access is
  an observable error; it never falls back to another location.

## Interactive scenarios and expected results

1. **Load / Save**: start with defaults (value 7), choose another nonnegative integer, save, close,
   reopen, and load it in either language. The previous complete committed document becomes
   `settings.json.bak`; notifications complete before a successful operation is displayed.
   **Reset** explicitly commits defaults; it does not bypass a malformed or incompatible primary.
2. **Upgrade**: in isolated storage with the application stopped, seed
   `{"schemaVersion":1,"revision":1,"settings":{"Value":42}}` as `settings.json`.
   Load reports that explicit maintenance is required; Upgrade commits schema 2 preserving 42.
   This sample's schema-1 migration preserves the existing payload; it is not an implicit import
   from legacy ApplicationSettingsBase. A future schema is rejected without overwriting it.
3. **Recover**: save twice to create a valid backup, stop, and damage the isolated primary JSON.
   Load and Save must report failure without silently using the backup. Recover explicitly restores
   the compatible backup and retains the damaged primary as `settings.json.corrupt.<id>`.
   A missing/invalid backup is an error; a valid or newer primary is not replaced.
4. **Health**: offline mode reports Degraded without blocking startup or local editing/settings.
   Set `Sample:Offline` false and Reload: the next report is Healthy. This is a deterministic local
   diagnostic, **not an internet-outage detector or proof of network reachability**. Only aggregate
   status is displayed. The standard health logging category is explicitly disabled to avoid
   descriptions, exception details, or report data bypassing the exception adapter's privacy policy.
5. **Controlled exception**: throw the known demonstration exception from a real UI event.
   The explicit application policy logs/marks it handled and continues. An unrelated UI exception
   instead requests host stop and produces an unsuccessful final result; there is no blanket
   continue-on-error policy or suppression of unobserved/terminating CLR failures.
6. **Wait**, then **Close / cancel pending**: the background-only demonstration waits indefinitely
   until cancellation. Closing defers UI close, stops accepting work, cancels and joins the accepted
   task, then posts close. Repeated close requests cannot start duplicate drains.
7. **Wait**, then **Host stop**: host-initiated stop can release the UI before service stop. The
   operation still cancels and joins in the hosted owner's `StopAsync` before provider disposal.
   **Worker failure** throws from the real BackgroundService; the standard StopHost policy stops
   the application and the sample reports failure, never `PASS`.

The shared session accepts one operation at a time; the UI disables operation buttons while busy.
Tasks retain no Forms, Controls, or UI synchronization context. UI continuations check disposal
before updating controls and are not needed to complete hosted cleanup. The worker observes both
its BackgroundService token and committed host stop. The sample intentionally chooses an infinite
host shutdown timeout and joins owner work independently of the stop caller's wait token:
**noncooperative operations can delay shutdown indefinitely**. Forced process termination is not
graceful cancellation and cannot guarantee a final commit, notification, or diagnostic.

## Automated scenarios

Do not interact with the desktop while these isolated applications open and close.

```powershell
$env:DOTNET_ROOT = Join-Path $PWD '.dotnet'
& .\artifacts\bin\System.Windows.Forms.UI.IntegrationTests\Debug\net12.0-windows7.0\System.Windows.Forms.UI.IntegrationTests.exe `
  --filter-class System.Windows.Forms.UITests.WinFormsInfrastructureSampleTests `
  --progress off --report-trx --results-directory "$PWD\artifacts\TestResults\InfrastructureSamples"
```

Sixteen process cases cover both languages: service composition/configuration precedence, environment
JSON loaded from the executable directory rather than the working directory, settings
save/load and backup, explicit schema upgrade and corruption recovery/reset, offline diagnostics,
an asynchronously dispatched UI exception, normal close with pending work, host stop with pending
work, real worker failure, and eager invalid options. Each uses an isolated absolute storage folder
and an unrelated working directory. Successful shutdown checks worker completion and owner drain
before exactly-once session disposal; settings assertions inspect the persisted JSON, not only exit
codes. Expected failures assert unsuccessful exits or explicit operation errors. A 60-second
per-process watchdog is a hang guard, not a responsiveness budget.

The source is in the existing UI integration-test project and its x64/x86 CI route; **local results
do not establish remote CI success**. See the
[architecture document](../../../../docs/design/winforms-application-builder-services.md#infrastructure-reference-samples-dotnetwinforms14962)
for recorded local evidence.

## Exploratory deployment matrix

For each real run, record source revision, language, OS/session type, architecture, build/runtime
versions, deployment method, actual profile/storage path, command, exit result, console transcript,
and any UI observations. Do not copy real preference content or personal profile details into
public reports. The following expected results are acceptance instructions, not claims that the
environments have all been tested.

- **Unpackaged, framework-dependent output**: run both apphosts from an unrelated working directory
  with the matching preview runtime and adjacent prototype binaries. Configuration comes from the
  executable content root; saves only change per-user or explicitly isolated storage. Repeat in
  Debug and Release and with actual x64/x86 apphosts/runtime, not just an architecture property.
- **Published framework-dependent folder**: publish each executable project to its own output:

  ```powershell
  .\.dotnet\dotnet.exe publish src\test\integration\ApplicationBuilderInfrastructure\CSharp\ApplicationBuilderInfrastructure.CSharp.csproj -c Debug --no-restore --no-build --self-contained false -o artifacts\tmp\InfrastructurePublish\CSharp
  .\.dotnet\dotnet.exe publish src\test\integration\ApplicationBuilderInfrastructure\VisualBasic\ApplicationBuilderInfrastructure.VisualBasic.vbproj -c Debug --no-restore --no-build --self-contained false -o artifacts\tmp\InfrastructurePublish\VisualBasic
  ```

  First perform the Arcade build above. Inspect the published configuration and framework binaries,
  then run all three `--Sample:Scenario=smoke`, `close-pending`, and `host-stop` cases with isolated
  absolute storage, asserting their transcripts and exit codes. Retain the prototype runtime caveat;
  this does not certify stock runtime deployment.
- **Self-contained / single-file / trimmed / NativeAOT**: not certified by this sample. Restore and
  publish with a matching architecture/runtime pack only where those prototype dependencies and
  WinForms features are supported. Do not infer support from source-generated settings metadata.
- **MSIX or representative signed installer**: use the deployment team's agreed identity/signing
  and matching runtime provisioning, install/update/uninstall as a standard user, and repeat
  persistence, upgrade/recovery, cancellation and diagnostics. Verify packaged profile/path
  virtualization and update preservation explicitly. Folder publish is **not packaged acceptance**.
- **Read-only installation**: use a genuinely ACL-restricted installation directory as a standard
  unelevated user; launch without editing its files and save/reopen preferences. Expect no attempts
  to write settings/logs beside the executable. Separately deny the chosen user store: Save must
  visibly fail and preserve committed data. A file's ReadOnly bit or running elevated is not proof.
- **Multiple users**: launch the same install as two actual Windows users without a shared storage
  override. Save distinct values, restart, and verify independent profile state. Two temporary
  directories under one identity are not multi-user evidence. Two processes of the same identity
  share the store and must preserve valid complete documents under the existing lock contract.
- **Terminal Services**: run in a real remote session, record the reported session flag, disconnect/
  reconnect while work is pending, then close and verify cancellation/drain and persisted state.
  Shared user identity across simultaneous sessions is not automatically isolated by the stable
  application ID; roaming/redirected-profile semantics need their own policy.
- **Offline**: first use the deterministic offline diagnostic. Separately disable actual remote
  connectivity in an approved test environment and repeat startup/local editing/settings/shutdown.
  No sample operation needs a network; build-time package restore is not offline runtime evidence.
- **Visual/manual acceptance**: inspect both Forms for usable layout, focus, scaling and error
  visibility. Native ARM64, representative slower devices, debugger output, terminating CLR paths,
  real file-provider flush/durability, Installer/Designer-team approval and resource budgets remain
  separate external gates. This sample does not close dotnet/winforms#14963.
