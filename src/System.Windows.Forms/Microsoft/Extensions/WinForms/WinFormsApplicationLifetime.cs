// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Runtime.ExceptionServices;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Provides lifecycle notifications for a Windows Forms application.
/// </summary>
/// <remarks>
///  <para>
///   Notifications are raised at most once and in starting, started, stopping,
///   stopped order. Startup cancellation or failure omits the started notification.
///  </para>
///  <para>
///   Runtime coordination raises these events on the application lifecycle
///   thread. Event-handler exceptions are not suppressed.
///  </para>
/// </remarks>
public sealed class WinFormsApplicationLifetime
{
    /// <summary>
    ///  Identifies the lifecycle phase used to suppress duplicate notifications.
    /// </summary>
    private enum LifecycleState
    {
        NotStarted,
        Starting,
        Started,
        Stopping,
        Stopped
    }

    private LifecycleState _state;
    private readonly Lock _stateLock = new();

    internal WinFormsApplicationLifetime()
    {
    }

    /// <summary>
    ///  Occurs on the application thread before startup UI activation or host startup.
    /// </summary>
    /// <remarks>
    ///  <para>
    ///   Set <see cref="CancelEventArgs.Cancel"/> to prevent startup. The
    ///   application still raises <see cref="ApplicationStopping"/> and
    ///   <see cref="ApplicationStopped"/> during cleanup.
    ///  </para>
    /// </remarks>
    public event EventHandler<CancelEventArgs>? ApplicationStarting;

    /// <summary>
    ///  Occurs after the application has started successfully.
    /// </summary>
    public event EventHandler? ApplicationStarted;

    /// <summary>
    ///  Occurs when application shutdown begins.
    /// </summary>
    public event EventHandler? ApplicationStopping;

    /// <summary>
    ///  Occurs after the application has stopped.
    /// </summary>
    public event EventHandler? ApplicationStopped;

    internal bool NotifyApplicationStarting()
    {
        lock (_stateLock)
        {
            if (_state != LifecycleState.NotStarted)
            {
                return false;
            }

            _state = LifecycleState.Starting;
            CancelEventArgs eventArgs = new();
            ApplicationStarting?.Invoke(this, eventArgs);

            return eventArgs.Cancel;
        }
    }

    internal void NotifyApplicationStarted()
    {
        lock (_stateLock)
        {
            if (_state is not (LifecycleState.NotStarted or LifecycleState.Starting))
            {
                return;
            }

            _state = LifecycleState.Started;
            ApplicationStarted?.Invoke(this, EventArgs.Empty);
        }
    }

    internal void NotifyApplicationStopping()
    {
        lock (_stateLock)
        {
            if (_state is LifecycleState.Stopping or LifecycleState.Stopped)
            {
                return;
            }

            _state = LifecycleState.Stopping;
            ApplicationStopping?.Invoke(this, EventArgs.Empty);
        }
    }

    internal void NotifyApplicationStopped()
    {
        lock (_stateLock)
        {
            if (_state == LifecycleState.Stopped)
            {
                return;
            }

            bool notifyStopping = _state != LifecycleState.Stopping;
            _state = LifecycleState.Stopped;
            Exception? failure = null;

            try
            {
                if (notifyStopping)
                {
                    ApplicationStopping?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            try
            {
                ApplicationStopped?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception exception)
            {
                failure = failure is null ? exception : new AggregateException(failure, exception);
            }

            if (failure is not null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
    }
}
