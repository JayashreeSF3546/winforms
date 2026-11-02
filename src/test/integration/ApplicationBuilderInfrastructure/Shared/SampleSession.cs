// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.WinForms;

namespace ApplicationBuilderInfrastructure;

/// <summary>
///  Supplies validated deployment options, distinct from mutable user preferences.
/// </summary>
public sealed class SampleOptions
{
    public string Caption { get; set; } = "Desktop infrastructure";
    public int WorkDelayMilliseconds { get; set; } = 250;
    public bool Offline { get; set; } = true;
}

/// <summary>
///  Represents the versioned per-user document shared by both sample front ends.
/// </summary>
public sealed class SamplePreferences
{
    public int Value { get; set; } = 7;
}

/// <summary>
///  Provides explicit metadata without reflection-based settings serialization.
/// </summary>
[JsonSerializable(typeof(SamplePreferences))]
internal sealed partial class PreferencesJsonContext : JsonSerializerContext
{
}

/// <summary>
///  Returns only allowlisted operation state to UI; exceptions and health report data are not displayed.
/// </summary>
public sealed record SampleResult(string Text, int? Value = null, bool Failed = false);

/// <summary>
///  Owns UI-free work through all stop origins, draining it before the root service provider is disposed.
/// </summary>
public sealed class SampleSession : IHostedService, IDisposable
{
    private static readonly Action<ILogger, string, string, Exception?> s_logOperation =
        LoggerMessage.Define<string, string>(LogLevel.Information, new EventId(2000, "SampleOperation"),
            "Sample operation {Operation} completed with {Outcome}");
    private readonly Lock _gate = new();
    private readonly IUserSettingsService<SamplePreferences> _settings;
    private readonly WinFormsHealthDiagnostics _health;
    private readonly IConfiguration _configuration;
    private readonly IOptionsMonitor<SampleOptions> _options;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<SampleSession> _logger;
    private readonly CancellationTokenSource _stopping;
    private Task<SampleResult>? _pending;
    private bool _accepting = true;
    private int _disposed;
    private int _ticks;
    private int _uiExceptions;
    private int _workerFailureRequested;

    public SampleSession(
        IUserSettingsService<SamplePreferences> settings,
        WinFormsHealthDiagnostics health,
        IConfiguration configuration,
        IOptionsMonitor<SampleOptions> options,
        IHostApplicationLifetime lifetime,
        IHostEnvironment environment,
        ILogger<SampleSession> logger)
    {
        _settings = settings;
        _health = health;
        _configuration = configuration;
        _options = options;
        _lifetime = lifetime;
        _environment = environment;
        _logger = logger;
        _stopping = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
        Scenario = configuration["Sample:Scenario"] ?? "";
    }

    public string Scenario { get; }
    public Exception? AutomationFailure { get; set; }
    public int Ticks => Volatile.Read(ref _ticks);
    public int UiExceptions => Volatile.Read(ref _uiExceptions);
    internal bool WorkerFailureRequested => Volatile.Read(ref _workerFailureRequested) != 0;
    public bool WorkerStopped { get; internal set; }
    public TaskCompletionSource OperationEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource UiExceptionHandled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string Describe()
        => $"{_environment.EnvironmentName}; {_options.CurrentValue.Caption}; ticks={Ticks}; " +
            $"process={Environment.ProcessId}; {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}";

    public Task StartAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task<SampleResult> ExecuteAsync(string operation, int value = 7)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (!_accepting || _stopping.IsCancellationRequested)
            {
                throw new InvalidOperationException("The sample is stopping and no longer accepts operations.");
            }

            if (_pending is { IsCompleted: false })
            {
                throw new InvalidOperationException("Wait for the current sample operation.");
            }

            _pending = Task.Run(() => ExecuteCoreAsync(operation, value, _stopping.Token));

            return _pending;
        }
    }

    private async Task<SampleResult> ExecuteCoreAsync(string operation, int value, CancellationToken token)
    {
        try
        {
            SampleResult result;
            switch (operation)
            {
                case "Load":
                    SamplePreferences preferences = await _settings.LoadAsync(token).ConfigureAwait(false);
                    result = new("Loaded preferences.", preferences.Value);
                    break;
                case "Save":
                    result = await CompleteCommitAsync(
                        _settings.SaveAsync(new SamplePreferences { Value = value }, token)).ConfigureAwait(false);
                    break;
                case "Reset":
                    result = await CompleteCommitAsync(_settings.ResetAsync(token)).ConfigureAwait(false);
                    break;
                case "Upgrade":
                    result = await CompleteCommitAsync(_settings.UpgradeAsync(token)).ConfigureAwait(false);
                    break;
                case "Recover":
                    result = await CompleteCommitAsync(_settings.RecoverAsync(token)).ConfigureAwait(false);
                    break;
                case "Health":
                    HealthReport report = await _health.CheckHealthAsync(
                        registration => registration.Tags.Contains("diagnostic"), token).ConfigureAwait(false);
                    result = new($"Health={report.Status} (offline is diagnostic, not a startup gate).");
                    break;
                case "Reload":
                    ((IConfigurationRoot)_configuration).Reload();
                    result = new(Describe());
                    break;
                case "Wait":
                    OperationEntered.TrySetResult();
                    await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
                    throw new InvalidOperationException("The cancellation demonstration unexpectedly completed.");
                default:
                    throw new ArgumentException("Unknown sample operation.", nameof(operation));
            }

            s_logOperation(_logger, operation, "Success", null);
            Console.WriteLine($"OPERATION {operation}: {result.Text}");

            return result;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            Console.WriteLine($"CANCELED {operation}");

            return new SampleResult($"{operation} canceled.");
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            s_logOperation(_logger, operation, exception.GetType().Name, null);
            Console.Error.WriteLine($"FAILED {operation}: {exception.GetType().Name}");

            return new SampleResult($"{operation} failed: {exception.GetType().Name}. See recovery guidance.", Failed: true);
        }
    }

    private static async Task<SampleResult> CompleteCommitAsync(Task<UserSettingsCommit> operation)
    {
        UserSettingsCommit commit = await operation.ConfigureAwait(false);
        await commit.NotificationCompletion.ConfigureAwait(false);

        return new SampleResult($"Committed schema={commit.SchemaVersion}, revision={commit.Revision}.",
            commit.Settings.GetProperty(nameof(SamplePreferences.Value)).GetInt32());
    }

    public void StopAccepting()
    {
        lock (_gate)
        {
            _accepting = false;
        }

        _stopping.Cancel();
    }

    public async Task DrainAsync()
    {
        Task? pending;
        lock (_gate)
        {
            pending = _pending;
        }

        if (pending is not null)
        {
            await pending.ConfigureAwait(false);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        StopAccepting();
        await DrainAsync().ConfigureAwait(false);
        Console.WriteLine("DRAINED owner operations before provider disposal");
    }

    public void RequestHostStop() => _lifetime.StopApplication();

    public void RequestWorkerFailure() => Interlocked.Exchange(ref _workerFailureRequested, 1);

    public void RecordUiException()
    {
        Interlocked.Increment(ref _uiExceptions);
        Console.WriteLine("HANDLED controlled UI exception");
        UiExceptionHandled.TrySetResult();
    }

    internal void Tick() => Interlocked.Increment(ref _ticks);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _stopping.Dispose();
        Console.WriteLine("DISPOSED sample session");
    }
}

/// <summary>
///  Performs cancellable background work without retaining Controls or posting after committed stop.
/// </summary>
public sealed class SampleWorker(
    SampleSession session, IOptions<SampleOptions> options, IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using CancellationTokenSource stopping = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken, lifetime.ApplicationStopping);
        try
        {
            using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(options.Value.WorkDelayMilliseconds));
            while (await timer.WaitForNextTickAsync(stopping.Token).ConfigureAwait(false))
            {
                if (session.WorkerFailureRequested)
                {
                    throw new InvalidOperationException("Intentional infrastructure sample worker failure.");
                }

                session.Tick();
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            session.AutomationFailure = exception;
            Console.Error.WriteLine($"FAILED background worker: {exception.GetType().Name}");
            throw;
        }
        finally
        {
            session.WorkerStopped = true;
            Console.WriteLine("STOPPED background worker");
        }
    }
}

/// <summary>
///  Demonstrates an explicit offline diagnostic without making a network request or blocking startup.
/// </summary>
public sealed class OfflineHealthCheck(IOptionsMonitor<SampleOptions> options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
        => Task.FromResult(options.CurrentValue.Offline
            ? HealthCheckResult.Degraded("Offline diagnostic mode.")
            : HealthCheckResult.Healthy("Local demonstration only; no remote reachability is asserted."));
}
