// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Prevents host stopped notification before owned UI cleanup has terminated.
/// </summary>
/// <remarks>
///  <para>
///   Shutdown cancellation deliberately does not release this barrier. Generic Host continues
///   shutdown after a hosted-service failure, so throwing on cancellation would break the ordering.
///   UI completion, not total run completion, avoids waiting recursively for host shutdown.
///  </para>
/// </remarks>
internal sealed class WinFormsHostStopBarrier(WinFormsApplication application) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken)
    {
        application.RequestStop();
        return application.UiCompletion;
    }
}
