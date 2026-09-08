// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Exposes Generic Host lifetime notifications and the WinForms synchronization context.
/// </summary>
public sealed class WinFormsApplicationLifetime
{
    private readonly IHostApplicationLifetime _hostLifetime;

    internal WinFormsApplicationLifetime(IHostApplicationLifetime hostLifetime)
    {
        _hostLifetime = hostLifetime;
        _hostLifetime.ApplicationStarted.Register(() => ApplicationStarted?.Invoke(this, EventArgs.Empty));
        _hostLifetime.ApplicationStopping.Register(() => ApplicationStopping?.Invoke(this, EventArgs.Empty));
        _hostLifetime.ApplicationStopped.Register(() => ApplicationStopped?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>
    ///  Occurs after the host and the WinForms message loop have started.
    /// </summary>
    public event EventHandler? ApplicationStarted;

    /// <summary>
    ///  Occurs when the host begins graceful shutdown.
    /// </summary>
    public event EventHandler? ApplicationStopping;

    /// <summary>
    ///  Occurs after hosted services and the WinForms message loop have stopped.
    /// </summary>
    public event EventHandler? ApplicationStopped;

    /// <summary>
    ///  Gets the cancellation token raised when the host has started.
    /// </summary>
    public CancellationToken ApplicationStartedToken => _hostLifetime.ApplicationStarted;

    /// <summary>
    ///  Gets the cancellation token raised when graceful shutdown begins.
    /// </summary>
    public CancellationToken ApplicationStoppingToken => _hostLifetime.ApplicationStopping;

    /// <summary>
    ///  Gets the cancellation token raised after graceful shutdown completes.
    /// </summary>
    public CancellationToken ApplicationStoppedToken => _hostLifetime.ApplicationStopped;

    /// <summary>
    ///  Gets the synchronization context installed on the WinForms UI thread.
    /// </summary>
    public SynchronizationContext? SynchronizationContext { get; internal set; }

    /// <summary>
    ///  Requests graceful application shutdown.
    /// </summary>
    public void StopApplication() => _hostLifetime.StopApplication();
}
