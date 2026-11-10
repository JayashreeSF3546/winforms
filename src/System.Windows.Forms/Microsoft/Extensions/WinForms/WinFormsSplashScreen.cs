// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Forms;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Provides a nonactivating splash for the internal owner-STA prototype.
/// </summary>
internal class WinFormsSplashScreen : Form
{
    protected sealed override bool ShowWithoutActivation => true;
}

/// <summary>
///  Owns one splash and a monotonic, nonblocking visibility gate on the existing root pump.
/// </summary>
/// <remarks>
///  <para>
///   The factory and all Form work run on the owner STA. Timer callbacks only post invalidations.
///   Disposal invalidates queued callbacks before releasing the timer and Form; no worker is joined.
///  </para>
/// </remarks>
internal sealed class WinFormsSplashLifetime(
    WinFormsSplashScreen screen,
    TimeProvider clock,
    TimeSpan minimum,
    Control dispatcher,
    Action<Exception> failure,
    Action<Exception> postFailure,
    Action earlyClose) : IDisposable
{
    private readonly Lock _gate = new();
    private Control? _dispatcher = dispatcher;
    private ITimer? _timer;
    private Action? _ready;
    private long _displayed;
    private bool _disposed;

    internal Form Screen => screen;

    internal void Show()
    {
        WinFormsApplicationOptions.ValidateStartupForm(screen);
        screen.Disposed += OnEarlyClose;
        screen.VisibleChanged += OnVisibilityChanged;
        screen.Show();
        if (!screen.Visible)
        {
            earlyClose();
        }

        // Successful visible acknowledgement, not a guarantee of first paint.
        _displayed = clock.GetTimestamp();
    }

    internal void WhenMinimumElapsed(Action ready)
    {
        _ready = ready;
        CheckMinimum();
    }

    private void CheckMinimum()
    {
        try
        {
            CheckMinimumCore();
        }
        catch (Exception exception) when (!exception.IsCriticalException())
        {
            failure(exception);
        }
    }

    private void CheckMinimumCore()
    {
        if (_disposed)
        {
            return;
        }

        TimeSpan remaining = minimum - clock.GetElapsedTime(_displayed);
        if (remaining <= TimeSpan.Zero)
        {
            _ready!();
            return;
        }

        _timer ??= clock.CreateTimer(
            _ => PostCheck(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        // Avoid repeated zero-millisecond scheduling when the platform truncates a fractional delay.
        TimeSpan due = remaining < TimeSpan.FromMilliseconds(1) ? TimeSpan.FromMilliseconds(1) : remaining;
        ObjectDisposedException.ThrowIf(!_timer.Change(due, Timeout.InfiniteTimeSpan), _timer);
    }

    private void PostCheck()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                _dispatcher!.BeginInvoke(CheckMinimum);
            }
            catch (Exception exception) when (!exception.IsCriticalException())
            {
                postFailure(exception);
            }
        }
    }

    private void OnEarlyClose(object? sender, EventArgs e)
        => earlyClose();

    private void OnVisibilityChanged(object? sender, EventArgs e)
    {
        if (!screen.Visible)
        {
            earlyClose();
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

            _disposed = true;
            _ready = null;
            _dispatcher = null;
        }

        try
        {
            _timer?.Dispose();
        }
        finally
        {
            screen.Disposed -= OnEarlyClose;
            screen.VisibleChanged -= OnVisibilityChanged;
            if (!screen.IsDisposed)
            {
                screen.Dispose();
            }
        }
    }
}
