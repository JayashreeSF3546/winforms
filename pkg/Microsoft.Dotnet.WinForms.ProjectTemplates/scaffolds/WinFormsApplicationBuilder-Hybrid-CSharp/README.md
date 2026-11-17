# Hybrid Application Builder scaffold: disabled

This is source preparation for dotnet/winforms#14967, **not an installable, runnable, or supported
Hybrid template**. It lives outside the package's `content` directory and deliberately uses
`template.pending.json`, not `template.json`. Neither CLI nor Visual Studio discovery is enabled.

The project stages a Razor SDK app with one host/provider, executable-relative configuration,
validated title options, debug logging, a native Designer-safe Form, a root Razor component, and
local HTML/CSS. The proposed WebView receives the host's provider; it does not build a second one.
The draft navigation policy allows only the pinned control's application-local HTTPS origin and
cancels other navigation without launching an external process or logging URL contents.

`Program.CreateApplication` always fails before creating a host, and runtime Form construction
independently rejects WebView activation. The parameterless Form constructor creates only native
layout. These gates are intentional: **do not remove them to run this scaffold**.

The pinned WebView package is a compile-time candidate, not an approved runtime dependency.
Hosting and logging come from the ASP.NET Core framework reference rather than duplicate packages.
Neither it nor the WinForms internal prototype supplies the public integration contract needed
by generated applications. An installed SDK alone also does not provide the in-development
Application Builder APIs or the development hosting package.

Before enabling discovery or activation:

- Obtain reviewed public WinForms initialization/shutdown registration and awaitable WebView
  cleanup APIs, including initialization failure and cancellation semantics.
- Replace constructor-time WebView activation with the approved paired initialization/cleanup
  protocol. Await actual page/component/scope disposal while UI dispatch and root services live.
- Verify the selected WebView package, WebView2 runtime, navigation origin/policy, initialization
  errors, Razor content deployment, and actual Visual Studio Designer behavior.
- Exercise user close, direct host stop, partial startup failure, cleanup failure, repeated stop,
  and canceled stop waits against real components with deliberately delayed asynchronous disposal.
- Complete generated-project build/run/publish and clean-machine/CI acceptance before moving
  the payload into `content` or renaming the pending metadata.

No WebView cleanup is inferred from `Disposed`, polling, reflection, synchronous waits, or
fire-and-forget tasks. The scaffold does not claim single-file, trimming, Native AOT, VB Hybrid,
automatic user-settings I/O, or actual browser startup.
