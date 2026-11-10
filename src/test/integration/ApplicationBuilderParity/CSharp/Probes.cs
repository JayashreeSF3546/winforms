// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net.NetworkInformation;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.WinForms;
using Microsoft.VisualBasic.ApplicationServices;

namespace ApplicationBuilderParity;

/// <summary>
///  Collects process evidence shared by the two test workers, without composing either application.
/// </summary>
public sealed class Probe
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly List<string> _events = [];
    private readonly Lock _gate = new();
    private Exception? _callbackFailure;

    public string[] Arguments { get; set; } = [];

    public InvalidOperationException Failure { get; } = new("controlled parity failure");

    public void Add(string name)
    {
        lock (_gate)
        {
            _events.Add(name);
        }
    }

    public void Ui(string name)
    {
        Require(Environment.CurrentManagedThreadId == _owner, "Wrong callback owner.");
        Require(Thread.CurrentThread.GetApartmentState() == ApartmentState.STA, "Not STA.");
        Add(name);
    }

    public static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public void Tokens(IList<string> arguments)
    {
        Arguments = arguments.ToArray();
        try
        {
            arguments[0] = "not allowed";
            throw new InvalidOperationException("Mutable event arguments.");
        }
        catch (NotSupportedException)
        {
            Ui("immutable");
        }
    }

    public void Guard(Action callback)
    {
        try
        {
            callback();
        }
        catch (Exception exception)
        {
            // Never let an assertion escape a native window callback or open an error dialog.
            _callbackFailure = exception;
            Application.ExitThread();
        }
    }

    public void Post(Action callback)
        => SynchronizationContext.Current!.Post(_ => Guard(callback), null);

    public void Finish()
    {
        if (_callbackFailure is not null)
        {
            throw new InvalidOperationException("Owner callback assertion failed.", _callbackFailure);
        }

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Events = _events.ToArray(),
            Arguments,
            FormsModule = typeof(Form).Module.ModuleVersionId,
            VisualBasicModule = typeof(WindowsFormsApplicationBase).Module.ModuleVersionId,
            FormsPath = typeof(Form).Assembly.Location,
            VisualBasicPath = typeof(WindowsFormsApplicationBase).Assembly.Location
        }));
    }
}

/// <summary>
///  Records host ownership without touching machine services or shared settings.
/// </summary>
public sealed class DrainService(Probe probe) : IHostedService, IDisposable
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        probe.Add("host-start");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        probe.Add("host-stop");
        return Task.CompletedTask;
    }

    public void Dispose() => probe.Add("host-dispose");
}

/// <summary>
///  Releases a controlled background failure only after UI readiness.
/// </summary>
public sealed class FaultService(Probe probe) : BackgroundService
{
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Release.Task.WaitAsync(stoppingToken);
        probe.Add("background-fault");
        throw probe.Failure;
    }
}

/// <summary>
///  Injects network invalidations; no operating-system adapter is changed.
/// </summary>
public sealed class NetworkSource : IWinFormsNetworkSource
{
    private NetworkAddressChangedEventHandler? _changed;

    public bool IsAvailable { get; private set; }
    public int Subscriptions { get; private set; }

    public event NetworkAddressChangedEventHandler Changed
    {
        add
        {
            _changed += value;
            Subscriptions++;
        }
        remove
        {
            _changed -= value;
            Subscriptions--;
        }
    }

    public void Raise()
    {
        IsAvailable = true;
        _changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
///  Advances a single splash timer explicitly; elapsed wall time is not evidence.
/// </summary>
public sealed class GateClock : TimeProvider
{
    private long _timestamp;
    private GateTimer? _timer;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _timestamp;
    public bool TimerDisposed => _timer?.Disposed == true;

    public void Advance()
    {
        _timestamp += TimeSpan.TicksPerSecond;
        _timer!.Fire();
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        => _timer = new GateTimer(callback, state);

    /// <summary>
    ///  Supplies only the explicit invalidation needed by the deterministic gate test.
    /// </summary>
    private sealed class GateTimer(TimerCallback callback, object? state) : ITimer
    {
        public bool Disposed { get; private set; }
        public bool Change(TimeSpan dueTime, TimeSpan period) => !Disposed;
        internal void Fire() => callback(state);
        public void Dispose() => Disposed = true;
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
