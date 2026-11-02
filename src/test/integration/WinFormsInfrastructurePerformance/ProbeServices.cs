// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WinFormsInfrastructurePerformance;

/// <summary>
///  Tracks application-owned resources without retaining UI objects or service providers.
/// </summary>
internal sealed class CycleState
{
    public bool UiDisposed { get; set; }
    public bool WorkerStopped { get; set; }
    public bool FlushAfterUi { get; set; }
    public int FlushedRecords { get; set; }
    public int DisposedServices { get; set; }
    public int HealthScopeDisposals;
    public OperationMeasurement Flush { get; set; } = new(0, 0);
}

/// <summary>
///  Implements a deterministic in-memory test sink, not a production or durable file logger.
/// </summary>
internal sealed class BufferedProbeLogger(CycleState state) : ILoggerProvider, ILogger
{
    private readonly Lock _gate = new();
    private readonly List<string> _records = [];
    private bool _disposed;

    public ILogger CreateLogger(string categoryName) => this;
    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState value, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _records.Add(formatter(value, exception));
        }
    }

    internal void Flush()
    {
        lock (_gate)
        {
            state.FlushedRecords += _records.Count;
            _records.Clear();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            Flush();
            _disposed = true;
            state.DisposedServices++;
        }
    }
}

/// <summary>
///  Flushes the same buffered sink at the same hosted stop boundary in both compositions.
/// </summary>
internal sealed class ProbeFlushService(BufferedProbeLogger logger, CycleState state) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (!state.UiDisposed)
        {
            throw new InvalidOperationException("Provider flush preceded UI disposal.");
        }

        state.FlushAfterUi = true;
        state.Flush = Program.Measure(logger.Flush);

        return Task.CompletedTask;
    }
}

/// <summary>
///  Owns a cancellable background task and joins it before marking service stop complete.
/// </summary>
internal sealed class ProbeWorker(CycleState state) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            state.WorkerStopped = true;
        }
    }
}

/// <summary>
///  Exercises asynchronous health-scope disposal on every explicit check.
/// </summary>
internal sealed class ProbeHealthCheck(CycleState state) : IHealthCheck, IAsyncDisposable
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
        => Task.FromResult(HealthCheckResult.Healthy());

    public ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref state.HealthScopeDisposals);

        return ValueTask.CompletedTask;
    }
}
