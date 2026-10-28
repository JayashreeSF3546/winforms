// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.ExceptionServices;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Provides ordered, once-only notifications for a Windows Forms application's lifetime.
/// </summary>
/// <remarks>
///  <para>
///   Notifications execute on the application's owning thread. Started is omitted when startup fails.
///   Subscriber failures are propagated after notifying the remaining subscribers and processing
///   reentrant transitions. These events do not replace Generic Host lifetime tokens.
///  </para>
/// </remarks>
public sealed class WinFormsApplicationLifetime
{
    private readonly Thread _ownerThread = Thread.CurrentThread;
    private LifetimeState _state;
    private LifetimeState _requestedState;
    private bool _notifying;

    internal WinFormsApplicationLifetime()
    {
    }

    /// <summary>
    ///  Occurs after startup UI is created and before the root message loop begins.
    /// </summary>
    public event EventHandler? ApplicationStarted;

    /// <summary>
    ///  Occurs once when the application commits to stopping, including failed startup.
    /// </summary>
    public event EventHandler? ApplicationStopping;

    /// <summary>
    ///  Occurs once after application teardown terminates, including teardown with errors.
    /// </summary>
    public event EventHandler? ApplicationStopped;

    internal void NotifyStarted() => Notify(LifetimeState.Started);

    internal void NotifyStopping() => Notify(LifetimeState.Stopping);

    internal void NotifyStopped() => Notify(LifetimeState.Stopped);

    private void Notify(LifetimeState requestedState)
    {
        if (Thread.CurrentThread != _ownerThread)
        {
            throw new InvalidOperationException(SR.WinFormsApplication_WrongThread);
        }

        if (requestedState > _requestedState)
        {
            _requestedState = requestedState;
        }

        if (_notifying)
        {
            return;
        }

        List<Exception>? exceptions = null;
        _notifying = true;
        try
        {
            while (_state < _requestedState)
            {
                _state = _state switch
                {
                    LifetimeState.Initial when _requestedState == LifetimeState.Started => LifetimeState.Started,
                    LifetimeState.Initial or LifetimeState.Started => LifetimeState.Stopping,
                    _ => LifetimeState.Stopped
                };

                EventHandler? handlers = _state switch
                {
                    LifetimeState.Started => ApplicationStarted,
                    LifetimeState.Stopping => ApplicationStopping,
                    _ => ApplicationStopped
                };

                if (handlers is null)
                {
                    continue;
                }

                foreach (EventHandler handler in handlers.GetInvocationList())
                {
                    try
                    {
                        handler(this, EventArgs.Empty);
                    }
                    catch (Exception exception) when (!exception.IsCriticalException())
                    {
                        (exceptions ??= []).Add(exception);
                    }
                }
            }
        }
        finally
        {
            _notifying = false;
        }

        if (exceptions is { Count: 1 })
        {
            ExceptionDispatchInfo.Throw(exceptions[0]);
        }

        if (exceptions is { Count: > 1 })
        {
            throw new AggregateException(exceptions);
        }
    }

    /// <summary>
    ///  Identifies monotonic lifetime notification milestones.
    /// </summary>
    private enum LifetimeState
    {
        Initial,
        Started,
        Stopping,
        Stopped
    }
}
