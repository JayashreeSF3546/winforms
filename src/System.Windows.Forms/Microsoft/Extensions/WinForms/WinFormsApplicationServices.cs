// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Provides metadata from the entry assembly.
/// </summary>
internal sealed class WinFormsApplicationInfo : IWinFormsApplicationInfo
{
    private readonly Assembly _entryAssembly =
        Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
    private readonly AssemblyName _assemblyName;

    /// <summary>
    ///  Initializes an instance of the <see cref="WinFormsApplicationInfo"/> class.
    /// </summary>
    public WinFormsApplicationInfo()
    {
        _assemblyName = _entryAssembly.GetName();
    }

    /// <inheritdoc/>
    public string Name => _assemblyName.Name ?? string.Empty;

    /// <inheritdoc/>
    public Version Version => _assemblyName.Version ?? new(0, 0);

    /// <inheritdoc/>
    public string BaseDirectory => AppContext.BaseDirectory;

    /// <inheritdoc/>
    public string Company => _entryAssembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? string.Empty;

    /// <inheritdoc/>
    public string Product => _entryAssembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? string.Empty;

    /// <inheritdoc/>
    public string Title => _entryAssembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title ?? string.Empty;

    /// <inheritdoc/>
    public string Description
        => _entryAssembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description ?? string.Empty;

    /// <inheritdoc/>
    public string Copyright
        => _entryAssembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty;

    /// <inheritdoc/>
    public string Trademark
        => _entryAssembly.GetCustomAttribute<AssemblyTrademarkAttribute>()?.Trademark ?? string.Empty;

    /// <inheritdoc/>
    public IReadOnlyList<Assembly> LoadedAssemblies
        => Array.AsReadOnly(AppDomain.CurrentDomain.GetAssemblies());
}

/// <summary>
///  Provides runtime and process information.
/// </summary>
internal sealed class WinFormsRuntimeInfo : IWinFormsRuntimeInfo
{
    /// <inheritdoc/>
    public string FrameworkDescription => RuntimeInformation.FrameworkDescription;

    /// <inheritdoc/>
    public string OperatingSystemDescription => RuntimeInformation.OSDescription;

    /// <inheritdoc/>
    public Architecture ProcessArchitecture => RuntimeInformation.ProcessArchitecture;

    /// <inheritdoc/>
    public string? ProcessPath => Environment.ProcessPath;

    /// <inheritdoc/>
    public int ProcessId => Environment.ProcessId;

    /// <inheritdoc/>
    public long WorkingSet => Environment.WorkingSet;

    /// <inheritdoc/>
    public string StackTrace => Environment.StackTrace;
}

/// <summary>
///  Supplies network availability and marshals changes to the application synchronization context.
/// </summary>
internal sealed class WinFormsNetworkAvailabilityService : IWinFormsNetworkAvailabilityService, IDisposable
{
    private readonly Lock _gate = new();
    private readonly INetworkAvailabilityMonitor _monitor;
    private EventHandler<WinFormsNetworkAvailabilityChangedEventArgs>? _networkAvailabilityChanged;
    private SynchronizationContext? _synchronizationContext;
    private bool? _lastKnownAvailability;
    private bool _initialized;
    private bool _listening;

    /// <summary>
    ///  Initializes an instance using the operating system network monitor.
    /// </summary>
    public WinFormsNetworkAvailabilityService()
        : this(new SystemNetworkAvailabilityMonitor())
    {
    }

    /// <summary>
    ///  Initializes an instance with the specified monitor.
    /// </summary>
    /// <param name="monitor">The network availability monitor.</param>
    internal WinFormsNetworkAvailabilityService(INetworkAvailabilityMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        _monitor = monitor;
    }

    /// <inheritdoc/>
    public bool IsAvailable => _monitor.IsAvailable;

    /// <inheritdoc/>
    public event EventHandler<WinFormsNetworkAvailabilityChangedEventArgs>? NetworkAvailabilityChanged
    {
        add
        {
            ArgumentNullException.ThrowIfNull(value);

            lock (_gate)
            {
                _networkAvailabilityChanged += value;

                try
                {
                    StartListening();
                }
                catch
                {
                    _networkAvailabilityChanged -= value;
                    throw;
                }
            }
        }
        remove
        {
            lock (_gate)
            {
                _networkAvailabilityChanged -= value;

                if (_networkAvailabilityChanged is null)
                {
                    StopListening();
                }
            }
        }
    }

    /// <summary>
    ///  Captures the application synchronization context and begins listening when needed.
    /// </summary>
    /// <param name="synchronizationContext">The context used to marshal availability notifications.</param>
    internal void Initialize(SynchronizationContext synchronizationContext)
    {
        ArgumentNullException.ThrowIfNull(synchronizationContext);

        lock (_gate)
        {
            _synchronizationContext = synchronizationContext;
            _initialized = true;
            StartListening();
        }
    }

    /// <summary>
    ///  Stops listening for network changes.
    /// </summary>
    internal void Stop()
    {
        lock (_gate)
        {
            _initialized = false;
            _synchronizationContext = null;
            StopListening();
        }
    }

    /// <summary>
    ///  Stops the network listener.
    /// </summary>
    public void Dispose() => Stop();

    private void StartListening()
    {
        if (!_initialized || _listening || _networkAvailabilityChanged is null)
        {
            return;
        }

        _lastKnownAvailability = _monitor.IsAvailable;
        _listening = true;

        try
        {
            _monitor.NetworkAddressChanged += OnNetworkAddressChanged;
        }
        catch
        {
            _listening = false;
            _lastKnownAvailability = null;
            throw;
        }
    }

    private void StopListening()
    {
        if (!_listening)
        {
            return;
        }

        _monitor.NetworkAddressChanged -= OnNetworkAddressChanged;
        _listening = false;
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs eventArgs)
    {
        SynchronizationContext? synchronizationContext;

        lock (_gate)
        {
            if (!_listening)
            {
                return;
            }

            synchronizationContext = _synchronizationContext;
        }

        if (synchronizationContext is null)
        {
            return;
        }

        try
        {
            synchronizationContext.Post(
                static state => ((WinFormsNetworkAvailabilityService)state!).RaiseIfAvailabilityChanged(),
                this);
        }
        catch (InvalidOperationException) when (!IsListening)
        {
        }
    }

    private void RaiseIfAvailabilityChanged()
    {
        EventHandler<WinFormsNetworkAvailabilityChangedEventArgs>? handlers;
        bool currentAvailability;

        lock (_gate)
        {
            if (!_listening || _networkAvailabilityChanged is null)
            {
                return;
            }

            currentAvailability = _monitor.IsAvailable;

            if (_lastKnownAvailability == currentAvailability)
            {
                return;
            }

            _lastKnownAvailability = currentAvailability;
            handlers = _networkAvailabilityChanged;
        }

        handlers.Invoke(this, new(currentAvailability));
    }

    private bool IsListening
    {
        get
        {
            lock (_gate)
            {
                return _listening;
            }
        }
    }

    /// <summary>
    ///  Listens to network address changes reported by the operating system.
    /// </summary>
    private sealed class SystemNetworkAvailabilityMonitor : INetworkAvailabilityMonitor
    {
        private event EventHandler? _networkAddressChanged;

        /// <inheritdoc/>
        public bool IsAvailable => NetworkInterface.GetIsNetworkAvailable();

        /// <inheritdoc/>
        public event EventHandler? NetworkAddressChanged
        {
            add
            {
                bool startListening = _networkAddressChanged is null;
                _networkAddressChanged += value;

                if (startListening)
                {
                    try
                    {
                        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
                    }
                    catch
                    {
                        _networkAddressChanged -= value;
                        throw;
                    }
                }
            }
            remove
            {
                _networkAddressChanged -= value;

                if (_networkAddressChanged is null)
                {
                    NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
                }
            }
        }

        private void OnNetworkAddressChanged(object? sender, EventArgs eventArgs)
            => _networkAddressChanged?.Invoke(sender, eventArgs);
    }
}

/// <summary>
///  Abstracts the operating system's network availability notifications for testing.
/// </summary>
internal interface INetworkAvailabilityMonitor
{
    /// <summary>
    ///  Gets the current network availability state.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    ///  Occurs when a network address changes.
    /// </summary>
    event EventHandler? NetworkAddressChanged;
}
