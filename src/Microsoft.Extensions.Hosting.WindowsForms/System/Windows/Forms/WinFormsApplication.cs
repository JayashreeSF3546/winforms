// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.ExceptionServices;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Windows.Forms;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Represents a configured Generic Host with a WinForms message-loop lifetime.
/// </summary>
public sealed class WinFormsApplication : IHost
{
    private static WinFormsApplication? s_current;
    private readonly IHost _host;
    private readonly WinFormsHostedService _hostedService;
    private int _disposed;

    internal WinFormsApplication(IHost host)
    {
        _host = host;
        Lifetime = host.Services.GetRequiredService<WinFormsApplicationLifetime>();
        _hostedService = host.Services.GetRequiredService<WinFormsHostedService>();
        Volatile.Write(ref s_current, this);
    }

    /// <summary>
    ///  Gets the most recently built application that has not been disposed.
    /// </summary>
    public static WinFormsApplication? Current => Volatile.Read(ref s_current);

    /// <summary>
    ///  Creates a builder with Generic Host defaults.
    /// </summary>
    public static WinFormsApplicationBuilder CreateBuilder(string[]? args = null)
        => new(args);

    /// <summary>
    ///  Gets the services configured for the application.
    /// </summary>
    public IServiceProvider Services => _host.Services;

    /// <summary>
    ///  Gets the WinForms lifetime coordinator.
    /// </summary>
    public WinFormsApplicationLifetime Lifetime { get; }

    /// <summary>
    ///  Starts the host and blocks until the WinForms application exits.
    /// </summary>
    public void Run() => RunAsync().GetAwaiter().GetResult();

    /// <summary>
    ///  Starts the host with the specified startup form and blocks until it exits.
    /// </summary>
    public void Run<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TForm>()
        where TForm : Form
    {
        _hostedService.SetStartupForm<TForm>();
        Run();
    }

    /// <summary>
    ///  Starts the host and blocks until the WinForms application exits.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _host.RunAsync(cancellationToken).ConfigureAwait(false);

        if (_hostedService.Exception is { } exception)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return _host.StartAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return _host.StopAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _host.Dispose();
            Interlocked.CompareExchange(ref s_current, null, this);
        }
    }
}
