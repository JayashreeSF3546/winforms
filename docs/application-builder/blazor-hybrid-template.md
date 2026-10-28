# WinForms Blazor Hybrid Application Builder template

The C# `winforms-blazor` project template adds Blazor Hybrid to a WinForms
application without creating another host or root service provider. It follows
the template matrix in [template strategy](template-strategy.md) and the
official [Windows Forms Blazor tutorial](https://learn.microsoft.com/aspnet/core/blazor/hybrid/tutorials/windows-forms).

## Composition and lifetime

- `WinFormsApplication.CreateBuilder(args)` creates the application's Generic
  Host. The builder owns configuration, logging, and dependency-injection
  registrations.
- `AddWindowsFormsBlazorWebView()` registers the WebView services with the
  builder's service collection. The startup Form is activated in the
  Application Builder's form scope and assigns that scope's provider to
  `BlazorWebView.Services`; Razor components resolve services from the same
  host registrations.
- Razor components use the host's `IConfiguration`, `ILogger<T>`, options, and
  application services. Router links demonstrate in-WebView navigation.
- The app-level `LoggingErrorBoundary` logs unhandled component exceptions
  through the host logger and shows a generic message instead of exposing
  exception details in the UI.
- The Form owns the `BlazorWebView` control. Disposing the Form disposes the
  control and its browser infrastructure. The Application Builder manages the
  Form scope and message-loop lifetime; disposing `WinFormsApplication` after
  `Run` stops and disposes the host.

The template deliberately doesn't create a separate `ServiceCollection`, call
`BuildServiceProvider`, or start a second Generic Host. Those approaches split
service registrations and logging/configuration state and complicate shutdown.
It also doesn't manually dispose the provider assigned to the WebView: the
Application Builder owns that Form scope.

## Project and deployment dependencies

The project uses `Microsoft.NET.Sdk.Razor`,
`Microsoft.AspNetCore.Components.WebView.WindowsForms` 10.0.10, and
`Microsoft.Extensions.Hosting` 10.0.10. Versions are explicit and fixed; the
WebView package version aligns with the host package used by the Application
Builder.

The Razor SDK processes the `wwwroot` host page and static assets. The
`BlazorWebView` loads `wwwroot/index.html`, which references
`_framework/blazor.webview.js` and the generated scoped-style bundle. The
WebView2 Runtime must be present on the target Windows machine. The template
doesn't bundle or install that runtime; deployment validation and automation
belong to #14969, while Visual Studio Designer and environment exploration
belong to #14970.

## Scope and decisions

This is a C#-only template, as approved in the matrix. It demonstrates one
host, shared DI/configuration/logging, and simple routed components. It doesn't
add a new Blazor host, a second settings system, an external UI framework, or
WebView2 runtime installation logic.
