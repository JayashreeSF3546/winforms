// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using Microsoft.Extensions.Hosting;

namespace WinFormsApplicationBuilder.CSharp;

/// <summary>
///  Requests host shutdown from background work without blocking UI dispatch.
/// </summary>
internal sealed class SampleWorker(IHostApplicationLifetime lifetime, Task uiReady, bool smoke) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Debug.WriteLine("Background service started");
        await uiReady.WaitAsync(stoppingToken).ConfigureAwait(false);
        if (!smoke)
        {
            for (int update = 1; update <= 5; update++)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
                Debug.WriteLine($"Background update: {update}");
            }
        }

        Debug.WriteLine("Background requesting shutdown");
        lifetime.StopApplication();
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        Debug.WriteLine("Background service stopped");
    }
}
