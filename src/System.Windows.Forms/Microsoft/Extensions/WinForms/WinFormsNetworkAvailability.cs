// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net.NetworkInformation;
using System.Windows.Forms;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Provides availability snapshots and invalidations; ownership stays with the supplier.
/// </summary>
internal interface IWinFormsNetworkSource
{
    bool IsAvailable { get; }
    event NetworkAddressChangedEventHandler Changed;
}

/// <summary>
///  Uses platform IP-network availability, not Internet or service reachability.
/// </summary>
internal sealed class WinFormsNetworkSource : IWinFormsNetworkSource
{
    public bool IsAvailable => NetworkInterface.GetIsNetworkAvailable();

    public event NetworkAddressChangedEventHandler Changed
    {
        add => NetworkChange.NetworkAddressChanged += value;
        remove => NetworkChange.NetworkAddressChanged -= value;
    }
}

/// <summary>
///  Coalesces source invalidations and re-reads current state only on the owning UI dispatcher.
/// </summary>
/// <remarks>
///  <para>
///   A notification is not a transition log. Serial UI reads prevent delayed source callbacks from
///   delivering stale payloads. Dispose invalidates queued work before unsubscribing, without owning
///   the source or dispatcher. Source and consumer failures terminate the selected application run.
///  </para>
/// </remarks>
internal sealed class WinFormsNetworkAvailability(
    IWinFormsNetworkSource source,
    Control dispatcher,
    Func<bool> stopping,
    Action<bool> changed,
    Action<Exception> failure,
    Action<Exception> postFailure) : IDisposable
{
    private readonly Lock _gate = new();
    private Control? _dispatcher = dispatcher;
    private bool _active;
    private bool _subscribed;
    private bool _pending;
    private bool _available;

    internal void Start()
    {
        lock (_gate)
        {
            _active = true;
        }

        // Mark before add: even a partially failing add must be paired with remove in cleanup.
        _subscribed = true;
        source.Changed += OnChanged;
        if (!stopping())
        {
            _available = source.IsAvailable;
        }
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            if (!_active || _pending)
            {
                return;
            }

            _pending = true;
            try
            {
                _dispatcher!.BeginInvoke(Deliver);
            }
            catch (Exception exception) when (!exception.IsCriticalException())
            {
                _pending = false;
                _active = false;
                _dispatcher = null;
                postFailure(exception);
            }
        }
    }

    private void Deliver()
    {
        lock (_gate)
        {
            _pending = false;
            if (!_active || stopping())
            {
                return;
            }
        }

        try
        {
            bool available = source.IsAvailable;
            if (!stopping() && available != _available)
            {
                _available = available;
                changed(available);
            }
        }
        catch (Exception exception) when (!exception.IsCriticalException())
        {
            failure(exception);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _active = false;
            _dispatcher = null;
        }

        if (_subscribed)
        {
            _subscribed = false;
            source.Changed -= OnChanged;
        }
    }
}
