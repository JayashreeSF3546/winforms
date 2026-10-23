// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Coordinates display and cancellation of an application's splash screen.
/// </summary>
internal sealed class WinFormsSplashScreen : IDisposable
{
    private readonly System.Windows.Forms.Form _form;
    private readonly TimeSpan _minimumDisplayDuration;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly CancellationTokenSource _stopSource = new();
    private long _displayStartedAt;
    private int _stopRequested;
    private int _disposed;

    /// <summary>
    ///  Initializes an instance with a real asynchronous delay.
    /// </summary>
    /// <param name="form">The splash form.</param>
    /// <param name="minimumDisplayDuration">The minimum duration of the splash display.</param>
    internal WinFormsSplashScreen(System.Windows.Forms.Form form, TimeSpan minimumDisplayDuration)
        : this(form, minimumDisplayDuration, static (duration, cancellationToken) =>
            Task.Delay(duration, cancellationToken))
    {
    }

    /// <summary>
    ///  Initializes an instance with an injectable delay for deterministic tests.
    /// </summary>
    /// <param name="form">The splash form.</param>
    /// <param name="minimumDisplayDuration">The minimum duration of the splash display.</param>
    /// <param name="delay">The asynchronous delay implementation.</param>
    internal WinFormsSplashScreen(
        System.Windows.Forms.Form form,
        TimeSpan minimumDisplayDuration,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(delay);
        ArgumentOutOfRangeException.ThrowIfLessThan(minimumDisplayDuration, TimeSpan.Zero);

        _form = form;
        _minimumDisplayDuration = minimumDisplayDuration;
        _delay = delay;
    }

    /// <summary>
    ///  Shows the splash form and starts measuring its display duration.
    /// </summary>
    internal void Show()
    {
        _displayStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        _form.Show();
    }

    /// <summary>
    ///  Asynchronously waits for the remaining minimum display duration.
    /// </summary>
    internal async Task WaitForMinimumDisplayDurationAsync()
    {
        TimeSpan remaining = _minimumDisplayDuration - System.Diagnostics.Stopwatch.GetElapsedTime(_displayStartedAt);

        if (remaining > TimeSpan.Zero)
        {
            await _delay(remaining, _stopSource.Token).ConfigureAwait(true);
        }
    }

    /// <summary>
    ///  Cancels a pending delay and hides the splash form.
    /// </summary>
    internal void Stop()
    {
        if (Interlocked.Exchange(ref _stopRequested, 1) != 0)
        {
            return;
        }

        try
        {
            _stopSource.Cancel();
        }
        finally
        {
            if (!_form.IsDisposed)
            {
                _form.Dispose();
            }
        }
    }

    /// <summary>
    ///  Cancels any pending delay and disposes the splash form.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            Stop();
        }
        finally
        {
            _stopSource.Dispose();
        }
    }
}
