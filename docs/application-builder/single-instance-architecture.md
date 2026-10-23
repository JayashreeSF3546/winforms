# Single-instance activation architecture

**Scope:** issue [#14976](https://github.com/dotnet/winforms/issues/14976), the shared single-instance and next-instance activation contract.

## Shared API

`WinFormsApplicationBuilder.UseSingleInstance()` enables the default identity. `UseSingleInstance(applicationId)` uses an application-selected identity. A later launch forwards its captured `CommandLineArgs` and returns without starting its host, raising its application lifetime events, or entering a message loop. The primary instance raises `WinFormsApplication.ActivationRequested` on its UI thread with an immutable argument snapshot.

The event can be received before startup finishes. The primary queues those arguments and schedules event delivery after `ApplicationStarted` and after the host-backed startup form has been assigned to its context. The event is delivered even when the `ApplicationContext` has no main form. `BringToForeground` defaults to `true`; after the handler returns, the runtime restores a minimized main form and calls `Activate()`. Foreground activation is best-effort and remains subject to Windows foreground-activation policy. Set `BringToForeground` to `false` when the activation handler should not request focus.

## Identity and process boundaries

The default identity is derived from the entry assembly's `GuidAttribute`, or its assembly name when no GUID is present, its major/minor version, and `AppContext.BaseDirectory`. The explicit application ID replaces the assembly/path portion and permits copies at different paths to intentionally share activation. Both modes include the current Windows user SID and process session ID. The full identity is SHA-256 hashed before it is used as the local pipe name; user, path, and application identity values are not exposed in the pipe name.

The major/minor version component preserves the legacy VB framework's version grouping. A major or minor version change starts a distinct instance; a patch/build version change does not. The default path component gives side-by-side copies separate identities. Explicit IDs are scoped per user and session, not machine-wide.

The named pipe uses `PipeOptions.CurrentUserOnly`, which restricts access to the current user and disallows remote clients. The SID and session components also prevent ordinary instances for different users or Windows sessions from colliding, including separate RDP sessions. This is not an elevation boundary: processes with the same user SID and session use the same identity, and any additional Windows integrity-level restrictions are enforced by the operating system. Applications that need a distinct elevation identity should use distinct explicit application IDs. This is local activation IPC, not an authorization mechanism for privileged commands.

## Bounded transport

The pipe uses a versioned, length-prefixed UTF-8 frame with a fixed protocol marker, argument count, and byte length per argument. The protocol rejects malformed headers, invalid UTF-8, trailing data, more than 32,767 arguments, arguments larger than 256 KiB, and total payloads larger than 1 MiB. The server reads one request at a time and returns an explicit acceptance byte. Client connection and response operations have a 2.5-second overall timeout; incomplete client requests are abandoned after 5 seconds so they cannot hold the listener indefinitely.

The listener uses asynchronous local named pipes and does not create global mutexes or other machine-wide synchronization primitives. An activation is acknowledged after it has been accepted into the primary application's queue, not after an application handler completes. If forwarding cannot connect, times out, or is rejected during shutdown, the secondary `Run()` surfaces an `InvalidOperationException` rather than silently continuing as another primary instance.

## Lifecycle and failure boundaries

- The primary endpoint is established after WinForms thread-context setup but before `ApplicationStarting` and host startup. This permits startup-time launches to be queued without running application code on the pipe thread.
- The primary disposes the endpoint as shutdown begins. Activation callbacks execute on the UI thread and may create modeless/modal UI through normal application APIs.
- Startup cancellation or a primary instance that exits before an activation is dispatched can leave an already-accepted request undelivered. The acknowledgement confirms queue acceptance, not successful completion of user activation code; a later launch can become the new primary after the original endpoint closes.
- Exceptions raised by activation handlers follow the normal UI-thread exception policy. Listener failures are recorded as runtime failures and trigger application shutdown.
- The legacy `WindowsFormsApplicationBase` and `StartupNextInstance` behavior is unchanged. This API is a shared migration target, not an implicit wrapper or compatibility alias.

## Validation ownership

Focused tests cover identity partitioning, version grouping, malformed-frame rejection, argument forwarding, and startup-race queueing. Cross-user, elevation, RDP/terminal-services, multi-session, and rapid-relaunch environment validation remains assigned to #14980; this document does not claim those environments were exercised.
