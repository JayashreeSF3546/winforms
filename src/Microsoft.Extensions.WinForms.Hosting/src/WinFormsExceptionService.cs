// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Activates exception routing with the host and reports diagnostic failures before provider disposal.
/// </summary>
internal sealed class WinFormsExceptionService : IHostedService, IDisposable
{
    private readonly WinFormsApplication _application;
    private readonly WinFormsExceptionLogging _logging;

    public WinFormsExceptionService(WinFormsApplication application, WinFormsExceptionLogging logging)
    {
        _application = application;
        _logging = logging;
        application.Lifetime.ApplicationStopped += OnStopped;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // ApplicationStopped follows host StopAsync, so retain logging until that notification.
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose()
    {
        _application.Lifetime.ApplicationStopped -= OnStopped;
        _logging.Dispose();
    }

    private void OnStopped(object? sender, EventArgs e)
    {
        Dispose();
        _logging.DiagnosticCompletion.GetAwaiter().GetResult();
    }
}
