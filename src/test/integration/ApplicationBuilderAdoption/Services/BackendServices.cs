// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApplicationBuilderAdoption.Services;

/// <summary>
///  Keeps networking opt-in and permits observable, cancellable offline latency.
/// </summary>
public sealed class BackendOptions
{
    public bool Offline { get; set; } = true;
    public string? Endpoint { get; set; }
    public int IntervalMilliseconds { get; set; } = 1000;
    public int OfflineDelayMilliseconds { get; set; }

    public bool HasSafeEndpoint()
        => Offline || (Uri.TryCreate(Endpoint, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0);
}

/// <summary>
///  Publishes UI-free status and observable worker completion across threads.
/// </summary>
public sealed class BackendState
{
    private string _status = "Waiting for backend";
    private int _succeeded;
    private int _failed;
    private int _stopped;
    private int _disposed;

    public string Status => Volatile.Read(ref _status);
    public bool Succeeded => Volatile.Read(ref _succeeded) != 0;
    public bool Failed => Volatile.Read(ref _failed) != 0;
    public bool Stopped => Volatile.Read(ref _stopped) != 0;
    public bool Disposed => Volatile.Read(ref _disposed) != 0;
    public Exception? Failure { get; internal set; }
    public TaskCompletionSource ReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ReadCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal void Report(string status, bool succeeded)
    {
        Volatile.Write(ref _status, status);
        if (succeeded)
        {
            Volatile.Write(ref _succeeded, 1);
        }
        else
        {
            Volatile.Write(ref _failed, 1);
        }

        ReadCompleted.TrySetResult();
    }

    internal void Stop() => Volatile.Write(ref _stopped, 1);
    internal void Dispose() => Volatile.Write(ref _disposed, 1);
}

/// <summary>
///  Separates the background workflow from an offline or network transport.
/// </summary>
public interface IBackendSource
{
    Task<string> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>
///  Supplies deterministic data without constructing an HTTP client or making network requests.
/// </summary>
public sealed class OfflineBackendSource(IOptions<BackendOptions> options) : IBackendSource
{
    private int _sequence;

    public async Task<string> ReadAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(options.Value.OfflineDelayMilliseconds, cancellationToken).ConfigureAwait(false);

        return $"Offline snapshot {Interlocked.Increment(ref _sequence)}";
    }
}

/// <summary>
///  Checks an explicitly configured endpoint without downloading a response body or logging its URL.
/// </summary>
public sealed class HttpBackendSource(HttpClient client, IOptions<BackendOptions> options) : IBackendSource
{
    public async Task<string> ReadAsync(CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Head, options.Value.Endpoint);
        using HttpResponseMessage response = await client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return $"Backend reachable: HTTP {(int)response.StatusCode}";
    }
}

/// <summary>
///  Owns cancellable, UI-free polling; expected connection failures leave the desktop application running.
/// </summary>
public sealed partial class BackendWorker(
    IBackendSource source, BackendState state, IOptions<BackendOptions> options, ILogger<BackendWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(options.Value.IntervalMilliseconds));
        try
        {
            do
            {
                state.ReadEntered.TrySetResult();
                try
                {
                    string result = await source.ReadAsync(stoppingToken).ConfigureAwait(false);
                    state.Report(result, succeeded: true);
                }
                catch (HttpRequestException exception)
                {
                    state.Report("Backend unavailable", succeeded: false);
                    Unavailable(logger, exception.StatusCode is { } code ? (int)code : null);
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    state.Report("Backend request timed out", succeeded: false);
                    TimedOut(logger);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            Console.WriteLine("CANCELED backend work");
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            state.Failure = exception;
            Console.Error.WriteLine($"BACKEND FAILED: {exception.GetType().Name}");
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        if (ExecuteTask is { IsCompleted: false })
        {
            throw new InvalidOperationException("Backend shutdown did not join the worker.");
        }

        state.Stop();
        Console.WriteLine("STOPPED backend worker");
    }

    public override void Dispose()
    {
        base.Dispose();
        state.Dispose();
        Console.WriteLine("DISPOSED backend worker");
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Backend unavailable; HTTP status {Status}.")]
    private static partial void Unavailable(ILogger logger, int? status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Backend request timed out.")]
    private static partial void TimedOut(ILogger logger);
}
