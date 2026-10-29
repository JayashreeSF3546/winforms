// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Serializes host operations for a caller-owned WinForms runtime.
/// </summary>
/// <remarks>
///  <para>
///   The container exposes this adapter as IHost, including to hosted services. The original host
///   remains private. Cancellation bounds individual waits, never the shared shutdown operation.
///  </para>
/// </remarks>
internal sealed class WinFormsApplicationHost(IHost inner, WinFormsApplication application) : IHost
{
    private readonly Lock _gate = new();
    private Task? _startTask;
    private Task? _stopTask;
    private int _disposed;

    public IServiceProvider Services => inner.Services;

    internal static WinFormsApplicationHost Build(IHostBuilder builder, WinFormsApplication application)
    {
        WinFormsApplicationHost? adapter = null;
        builder.ConfigureServices((_, services) =>
        {
            ServiceDescriptor? descriptor = services.SingleOrDefault(service => service.ServiceType == typeof(IHost));
            if (descriptor is not { Lifetime: ServiceLifetime.Singleton, ImplementationFactory: { } factory })
            {
                throw new InvalidOperationException(SR.WinFormsApplication_UnsupportedHostBuilder);
            }

            services.Remove(descriptor);
            services.AddSingleton<IHost>(provider =>
                adapter = new WinFormsApplicationHost((IHost)factory(provider), application));
            services.AddSingleton<IHostedService>(new WinFormsHostStopBarrier(application));
        });

        try
        {
            IHost host = builder.Build();
            if (!ReferenceEquals(host, adapter)
                || !ReferenceEquals(System.ServiceExtensions.GetRequiredService<IHost>(host.Services), adapter))
            {
                if (!ReferenceEquals(host, adapter))
                {
                    host.Dispose();
                }

                throw new InvalidOperationException(SR.WinFormsApplication_UnsupportedHostBuilder);
            }

            return adapter!;
        }
        catch
        {
            adapter?.DisposeHost();
            throw;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!application.IsRunning)
        {
            throw new InvalidOperationException(SR.WinFormsApplication_HostStartRequiresRun);
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            return _startTask ??= Task.Run(() => inner.StartAsync(cancellationToken), CancellationToken.None);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        application.RequestStop();
        lock (_gate)
        {
            if (_disposed != 0 && _stopTask is null)
            {
                return Task.CompletedTask;
            }

            _stopTask ??= Task.Run(StopCoreAsync, CancellationToken.None);
            return _stopTask.WaitAsync(cancellationToken);
        }
    }

    private async Task StopCoreAsync()
    {
        System.ServiceExtensions.GetRequiredService<IHostApplicationLifetime>(Services).StopApplication();
        await application.UiCompletion.ConfigureAwait(false);

        Task? startTask;
        lock (_gate)
        {
            startTask = _startTask;
        }

        if (startTask is not null)
        {
            // StartAsync reports startup failure to the runner; still stop a partially started host.
            await startTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        await inner.StopAsync(CancellationToken.None).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            application.Dispose();
        }
    }

    internal void DisposeHost()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            inner.Dispose();
        }
    }
}
