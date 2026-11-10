# Single-instance and next-instance activation prototype

Issue: [dotnet/winforms#14976 — Implement secure single-instance and next-instance activation][issue].
Parent: [dotnet/winforms#14972][parent].

Status: **bounded internal opt-in implementation; not an approved public API, shipping-security
certification, or full issue acceptance**. No classic VB Application Framework behavior is changed.
The [inventory][inventory], [contracts][contracts], and [shared lifecycle][lifecycle] remain the
compatibility baselines (committed contracts `f0a17b129`, lifecycle `67309ecd1`).
Upstream child, parent and cited issue bodies were read through GitHub's API on 2026-11-06.

## What is this issue proposing?

A single-instance application should open one application UI, not one UI per launch.
A later launch can pass file names or other argument tokens to the running application's UI handler
and exit. This requires more than “a pipe already exists”: the endpoint must be authenticated, the
identity policy explicit, and the result must say whether a handler actually finished.

```text
Entry point: explicitly choose logical ID, version cohort and handoff deadline
  -> validate/snapshot arguments -> try exclusive local pipe creation
       acquired -> primary lease; listener initially responds NotReady
                   -> configure existing builder -> Build once -> transfer lease
                   -> Run on caller STA -> host startup -> Starting
                   -> deferred root context -> ApplicationStarted -> existing root pump
       occupied/denied -> connect locally, authenticate endpoint and verify process
                         -> send one framed request -> await matching completion response
                         -> return secondary result; do NOT Build/Run another UI

Primary during startup -> NotReady (no startup queue, no re-entry into Starting)
Primary during pump   -> one owner-dispatcher callback -> optional normal Form activation
                         -> Handled / HandlerFailed / HandledFocusFailed
Committed stopping    -> invalidate pending dispatch -> Stopping; stop and join listener
Startup cancel/failure or unrun Dispose -> stop/join listener -> existing resource/host cleanup
Broken/late response  -> Unknown once a write was attempted; never automatically resend
```

`ApplicationStarted` is still the existing pre-pump notification, not a paint/readiness guarantee.
Requests before `_loopEntered` are rejected as NotReady. Posted activation may execute in the
existing pump (including a nested dialog); it is not a new runner, message loop or provider.
Synchronous handlers must terminate and must not recursively wait for host/UI shutdown.
No framework can make arbitrary nonterminating UI application code safely cancellable.

## Implemented identity and security boundary

`WinFormsSingleInstanceIdentity.Create(logicalId, versionCohort)` captures, rather than accepts
caller-injected security inputs:

| Input | Prototype policy |
|---|---|
| Logical ID | Explicit nonempty application-controlled string, case-sensitive. No assembly/MVID guess. |
| Version cohort | Explicit nonempty string; caller can select `"1.2"` or an exact version. No automatic major/minor policy. |
| Path | Current process executable, `Path.GetFullPath`, ordinal-ignore-case comparison. Different paths remain independent. No caller-supplied peer allowlist, symlink/file-ID canonicalization or cross-folder sharing. |
| User | Current Windows token user SID; not `Thread.CurrentPrincipal`, environment user name or a client claim. |
| Session | Current process Windows session ID; peer PID is checked against that session. |
| Elevation | Normal primary token whose owner SID equals user SID and which is not impersonating. Elevated/custom-owner tokens fail closed. There is no cross-elevation channel or invented elevation discriminator. |

The key is SHA-256 over separately length-prefixed identity fields, not delimiter concatenation.
`GetKey` is a pure internal test seam; changing synthetic SID/session inputs in a key test is
**not** a multi-user, RDP or elevation authentication test.

The transport uses existing CsWin32 interop and framework pipe/ACL types, with no new packages:

- `CreateNamedPipe`: overlapped, byte mode, one instance, `FILE_FLAG_FIRST_PIPE_INSTANCE`,
  `PIPE_REJECT_REMOTE_CLIENTS`, noninheritable handle.
- Protected DACL grants only the captured user SID read/write/synchronize rights. Clients are not
  granted `CreateNewInstance`. The same elected handle is disconnected/reused, not closed/reopened
  between clients; there is no ownership gap or mutex with thread-affine release.
- Client connects to `"."` only. Framework `CurrentUserOnly` checks the pipe owner. Both sides use
  OS-reported pipe peer PID and verify its session and executable path, failing closed when query
  access is unavailable. A wrong-image squatter is not acknowledged as a valid primary.
- Server examines the identification-only pipe client token, checking user and owner SID.
  Only identity comparison runs in `RunAsClient`; no handler, file I/O or host work runs impersonated.
- A namespace ownership error alone is not proof of secondary delivery. Connection/access/protocol
  failure never triggers fallback UI. Unexpected election/listener errors propagate at acquisition
  or owned cleanup instead of becoming false success.

Windows can report either access denied or pipe busy for an unacquired occupied endpoint, depending
on its ACL and current connection state. Only those two native outcomes enter authenticated
handoff; neither itself establishes a secondary or a delivery result. Other acquisition errors
propagate. The controlled wrong-reply endpoint test exposed and covers this distinction.

One narrow private `LibraryImport` preserves `CreateNamedPipeW` last-error. Inspection of this
checkout's emitted CsWin32 binding showed no `SetLastError`; blindly reading the managed cached
error incorrectly turned occupied-pipe acquisition into a Win32 “success” exception. The generator
also rejects requesting `GetLastError`. The private blittable import uses existing generated flags
and security attributes, `SetLastError = true` and System32 search only; it does not change assembly-
wide marshalling or introduce a downlevel fallback. Native binding/metadata reconciliation is a
review follow-up, not a reason to treat arbitrary acquisition errors as legitimate secondaries.

These are deliberate defenses, **not proof against an adversarial same-user process able to run
the same image, replace application files, alter its ACLs, debug/inject the process, or compromise
the application itself**. The protocol GUID correlates a reply; it is not an authentication secret.
Endpoint ACL ownership, native flags, token handling and peer-image checks still need security and
platform review. Privileged administrators are not a claimed isolation boundary. Actual cross-user,
remote, elevation, low-integrity, reconnect and deployment behavior has not been certified here.

The current target is `net12.0` from this checkout's manifests. No downlevel API fallback is added.
Package/API placement, supported Windows minimum, AOT/trimming and deployment profiles need review.

## Transport, outcomes and resource ownership

The #14980 [environment validation](winforms-application-builder-environment-validation.md)
exposed unread replies being discarded by immediate Windows pipe disconnect during rapid
activations. After receiving and validating a correlated reply, the client now sends a one-byte
consumption receipt. The listener waits under its existing connection deadline/stop token before
disconnecting; no unbounded synchronous pipe drain or resend is introduced. Receipt-write failure
does not undo a result already validated by the client. A missing receipt is bounded and does not
prevent stop. This internal framing change requires protocol/deployment compatibility review;
it does not remove `Unknown` after real lost replies or add an exactly-once guarantee.

Request payload: version integer, request GUID, foreground Boolean, argument count, and each
strict UTF-8 token with a byte length. Outer frame length is validated before allocation.
Limit: **64 KiB payload, 256 arguments**, bounded enumeration, strict decoding, no trailing bytes.
Empty strings, order, Unicode, spaces, literal quotes and quoted paths remain independent tokens.
Invalid UTF-8/UTF-16 and null arguments are rejected; there is no join/split, JSON serializer,
configuration reload, logging of tokens, or alteration of original lifecycle arguments.

The caller supplies a positive deadline no greater than one minute. Each server connection has
its own deadline, independent of UI progress. Only one transport client is served at a time;
concurrent clients may time out while another occupies that slot. This is a bounded prototype,
not a fairness or high-throughput guarantee. A disconnected/silent/malformed client is closed;
shutdown cancellation interrupts pending transport I/O and pending dispatch, and joins the task.

| Result | What is actually established |
|---|---|
| Primary | Exclusive pipe acquired, not UI readiness or successful startup. Caller owns lease until successful Build. |
| NotReady | Valid request reached listener before usable dispatch. No callback is queued or executed. |
| Stopping | Request rejected/canceled before dispatch because shutdown committed. |
| Handled | Callback returned and optional focus operation did not throw. It does **not** mean Windows granted foreground focus. |
| HandlerFailed | Callback threw an ordinary exception. It may already have performed partial side effects. No retry, exception-detail transport or escape from native dispatch. |
| HandledFocusFailed | Callback returned, but optional restore/activation threw. Do not rerun callback to retry focus. |
| Rejected | Access/peer validation rejected the endpoint or connection. No fallback UI. |
| Unavailable | Connection failed before any write, or acknowledged queued-dispatch deadline expired before callback entry. No success claim. |
| Unknown | Write was attempted but a trustworthy completion response was not obtained, or a callback had begun when its wait ended. Effects may have happened. |

There is no queue-accepted acknowledgement pretending to mean handled. Replies correlate request
GUID and outcome. There are no automatic retries, dedup cache or exactly-once claims across crashes.
Caller cancellation before sending can throw cancellation; after a send attempt it returns Unknown.
Only a fresh launch which actually acquires the exclusive pipe may become primary after leader
exit; a failed handoff does not promote itself. After a dead leader's handle closes, Windows releases
the election resource; process-kill recovery still needs explicit executable validation.

`UseSingleInstance(primary, handler)` is internal and checks owner-thread/single-use builder rules.
Build binds the lease only after any host build succeeds. A failed build leaves the primary lease
with its caller; a lease cannot be transferred twice. Disposing a transferred lease directly is
rejected: dispose/stop the owning application. Unrun application disposal and every accepted Run
cleanup join the listener. A canceled StopAsync wait does not cancel the shared stop operation.
Pending dispatch uses an atomic claim: cancellation wins before entry, or callback runs once.
Cancellation cannot replay a callback or interrupt partial effects after entry.

Focus happens after the handler, only for the selected context's live visible MainForm, and only
if requested and not stopping. Minimized Form is restored; `Form.Activate()` obeys Windows rules.
There is no `OpenForms` search, forced foreground grant, wrong-window selection or claim of focus
success. Custom/tray contexts without MainForm still receive activation; no window is invented.

## Small internal-use sketch

This is composition code **inside a friend/internal prototype consumer**, not a public API sample.
Run it from the application's owning STA entry point. Do not block that STA on host/UI callbacks.

```csharp
var identity = WinFormsSingleInstanceIdentity.Create("Contoso.Editor", "1.2");
var role = WinFormsSingleInstance.SelectAsync(
    identity, args, requestForeground: true, timeout: TimeSpan.FromSeconds(5))
    .GetAwaiter().GetResult();
if (role.Primary is null)
{
    // Return/report role.Result; Unknown is not safe to retry.
    return;
}

using var primary = role.Primary; // remains caller-owned if Build throws
using var app = WinFormsApplication.CreateBuilder()
    .UseStartupForm<EditorForm>()
    .UseSingleInstance(primary, (sender, request) =>
    {
        // Runs on the application's existing owner STA.
        // Consume request.CommandLine without reparsing; may set BringToForeground = false.
    })
    .Build();
app.Run(); // application owns transport through coordinated cleanup
```

Builders which do not select this feature retain their original behavior. No changes to
`WindowsFormsApplicationBase`, public approval files, classic VB defaults or generated events.
No splash/services, migration sample projects, broad parity suite or benchmarks are added.

## Known reports and disposition

These are source requirements, not reports reproduced or fixed in the old VB framework:

| Upstream report | Finding / bounded response |
|---|---|
| [dotnet/winforms#11232][terminal] | Different terminal-server users collide/connect unsuccessfully. Captured user/session identity and ACL are explicit here; actual multi-user/RDP testing remains required. |
| [dotnet/winforms#11365][folders] | Copies in different folders run separately. This prototype deliberately retains per-path separation; cross-folder logical identity/allowlisted peer authentication remains blocked by policy review. |
| [dotnet/winforms#8441][mdi] | MDI child creation fails in next-instance callback. Shared dispatch is on the owning UI dispatcher with contained ordinary callback failure; the reported app/native failure has not been reproduced. |
| [dotnet/winforms#4805][context] | MainForm-null custom contexts lose legacy activation. This prototype dispatches independently of MainForm; only optional focus requires one. |
| [dotnet/winforms#3296][versions] | MVID changes between builds conflict with major/minor identity. Caller explicitly chooses cohort; no MVID is used and no automatic legacy identity conversion is asserted. |

## Source pointers and review gates

- `WinFormsSingleInstanceIdentity`: immutable capture, key calculation and peer process comparison.
- `WinFormsSingleInstancePipe`: ACL, exclusive/local native pipe creation and authenticated peer checks.
- `WinFormsSingleInstanceProtocol`: immutable request snapshot, strict bounded binary codec and outcomes.
- `WinFormsSingleInstance`: pre-build election/handoff, one listener, framed reply, deadlines and shared stop.
- `WinFormsApplicationBuilder.UseSingleInstance` / `Build`: opt-in transfer after successful host construction.
- `WinFormsApplication.DispatchNextInstanceAsync`, `SignalStopRequest`, `NotifyStopping`, Run/Dispose:
  existing owner dispatcher, atomic pending cancellation, handler/focus outcomes and cleanup.
- `WinFormsSingleInstanceTests`: bounded local kernel-pipe, dispatcher and owned-process checks.

Full dotnet/winforms#14976 acceptance remains blocked on reviewed public contracts and identity/
cross-path/version/elevation/deployment policy, security review of native ACL/authentication/election,
real multi-user/session/remote/elevated/ARM64 validation, and remote CI. This scoped implementation
does not substitute synthetic key tests for those gates. Review before suggesting
[dotnet/winforms#14977](https://github.com/dotnet/winforms/issues/14977); do not start it automatically.

## Focused local validation (2026-11-06)

Windows x64, Debug; repository SDK `12.0.100-alpha.1.26531.102`; the test executable's current
runtime configuration requires `Microsoft.NETCore.App 12.0.0-alpha.1.26554.104`, already installed
under `.dotnet`. No runtime/package installation or manifest change was needed.

Authoritative affected managed build: **0 warnings, 0 errors**. Hosting package graph pack:
**0 warnings, 0 errors**, produced `Microsoft.Extensions.WinForms.Hosting.12.0.0-dev.nupkg`.
This is not a full native/product package build or remote CI result. Arcade prints a missing-cmake
banner in this environment; the selected managed graphs complete successfully. No machine policy,
firewall, accounts, privilege changes, shared caches or shared IDE sessions were manipulated.

| Test selection | Passed | Failed / skipped |
|---|---:|---:|
| `WinFormsSingleInstanceTests` | 21 | 0 / 0 |
| `WinFormsApplicationFeaturesTests` | 33 | 0 / 0 |
| `WinFormsApplicationBuilderTests` | 24 | 0 / 0 |
| `WinFormsApplicationLifetimeTests` | 10 | 0 / 0 |
| `WinFormsApplicationRuntimeTests` | 49 | 0 / 0 |
| `WinFormsApplicationHostingTests` | 33 | 0 / 0 |
| **Total** | **170** | **0 / 0** |

The 21-case selection includes the unconfigured no-op worker entry. In addition, parent tests
launch that worker in separate test-owned processes to verify NotReady and Handled with no
secondary host/UI, and terminate one acknowledged test-owned elected leader to verify kernel
election release. Do not double-count child worker runs in the 170 total. Identity key variation
is synthetic; local process image matching, pipe framing and handoff are executable local tests.

Focused cases cover explicit cohort separation and concurrent first election; immutable
Unicode/empty/space/quote/path tokens; invalid/trailing/oversized/invalid-UTF-8 payloads; malformed,
disconnected and silent clients; real deadline-driven close without elapsed-time assertions;
uncorrelated acknowledgement; canceled selection and canceled shared-stop wait; startup cancel,
pending-dispatch cancellation/stop, running-handler cancellation with Unknown; contained handler
failure and later delivery; MainForm-null context; stop suppressing Form restore/focus; lease
transfer rejection, unrun disposal, stopped dispatcher and joined listener completion.
Process watchdogs and cleanup refer only to specific test-owned processes and random IPC names.
These are not global handle/thread leak measurements or proof of multi-user/elevated/RDP behavior.

Tests were added before behavior changes. The initial red build was blocked by missing COM test
references in the fresh output tree. The first runnable suite failed 5/11 cases, exposing uncached
native last-error and malformed-frame listener termination; both were corrected. A later
controlled endpoint test exposed the pipe-busy election variant, also corrected and rerun.
Analyzer findings were corrected, not disabled.

The fresh C-drive tree needed six generated AxHosts DLLs copied from BuildAssist's D-drive
output, plus the existing UnsupportedTypes test dependency built into C. BuildAssist's nested
Framework invocation does not inherit `ArtifactsDir`; no build-infrastructure fix was made here.
Corrected dependency command:

```powershell
.\build.cmd -projects src\test\unit\*\UnsupportedTypes\UnsupportedTypes.csproj `
    -msbuildEngine vs -verbosity minimal /p:ArtifactsDir="$env:TEMP\WinForms14976Artifacts\" `
    -binaryLogName "$env:TEMP\WinForms14976Artifacts\UnsupportedTypes.binlog"
```

Final commands from repository root (regression classes execute serially in separate processes):

```powershell
$env:DOTNET_ROOT = "$PWD\.dotnet"
$env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
.\build.cmd -projects src\test\unit\System.Windows.Forms\System.Windows.Forms.Tests.csproj `
    -verbosity minimal /p:Restore=false /p:ArtifactsDir="$env:TEMP\WinForms14976Artifacts\" `
    -binaryLogName "$env:TEMP\WinForms14976Artifacts\SingleInstance14976-Verified.binlog"

$exe = "$env:TEMP\WinForms14976Artifacts\bin\System.Windows.Forms.Tests\Debug\net12.0-windows7.0\System.Windows.Forms.Tests.exe"
& $exe --filter-class System.Windows.Forms.Tests.WinFormsSingleInstanceTests --parallel none `
    --report-trx --results-directory "$env:TEMP\WinForms14976Artifacts\TestResults\SingleInstance14976-Verified"
foreach ($class in 'WinFormsApplicationFeaturesTests','WinFormsApplicationBuilderTests',
    'WinFormsApplicationLifetimeTests','WinFormsApplicationRuntimeTests','WinFormsApplicationHostingTests') {
    & $exe --filter-class "System.Windows.Forms.Tests.$class" --parallel none --report-trx `
        --results-directory "$env:TEMP\WinForms14976Artifacts\TestResults\SingleInstance14976-$class"
}

.\build.cmd -pack -projects src\Microsoft.Extensions.WinForms.Hosting\src\Microsoft.Extensions.WinForms.Hosting.csproj `
    -verbosity minimal /p:Restore=false /p:ArtifactsDir="$env:TEMP\WinForms14976Artifacts\" `
    -binaryLogName "$env:TEMP\WinForms14976Artifacts\SingleInstance14976-Pack.binlog"
git --no-pager diff --check
```

TRX and binlogs remain in the above task-owned C-drive tree. BOM/CRLF, local documentation links
and generated localization additions were checked. HEAD remains `67309ecd1`; no commits or PRs.
No tests/samples/benchmarks belonging to later child issues, full CI, native build, multi-user/
session/elevation matrix, or stakeholder security/API approval are claimed.

[issue]: https://github.com/dotnet/winforms/issues/14976
[parent]: https://github.com/dotnet/winforms/issues/14972
[inventory]: winforms-application-builder-vb-parity.md
[contracts]: winforms-application-builder-vb-csharp-contracts.md
[lifecycle]: winforms-application-builder-shared-lifecycle.md
[terminal]: https://github.com/dotnet/winforms/issues/11232
[folders]: https://github.com/dotnet/winforms/issues/11365
[mdi]: https://github.com/dotnet/winforms/issues/8441
[context]: https://github.com/dotnet/winforms/issues/4805
[versions]: https://github.com/dotnet/winforms/issues/3296
