// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Windows.Forms;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Swaps views inside a region control, giving each view its own <see cref="UIScope"/>.
/// </summary>
/// <remarks>
///  <para>
///   Navigation is deterministic and rolls back: the next view is created and attached before the
///   previous view is detached and disposed. If activation or attachment fails, the previous view
///   stays attached and alive.
///  </para>
/// </remarks>
public sealed class UINavigator : IDisposable
{
    private readonly IUIActivator _activator;
    private readonly Control _region;
    private UIScope? _currentScope;
    private Control? _currentView;
    private bool _disposed;

    /// <summary>
    ///  Initializes a new instance of the <see cref="UINavigator"/> class.
    /// </summary>
    /// <param name="activator">The activator whose scope owns the navigated views.</param>
    /// <param name="region">The container control the views are added to.</param>
    public UINavigator(IUIActivator activator, Control region)
    {
        ArgumentNullException.ThrowIfNull(activator);
        ArgumentNullException.ThrowIfNull(region);

        _activator = activator;
        _region = region;
    }

    /// <summary>
    ///  Gets the currently attached view, or <see langword="null"/> when none is attached.
    /// </summary>
    public Control? CurrentView => _currentView;

    /// <summary>
    ///  Gets the scope of the currently attached view, or <see langword="null"/> when none is
    ///  attached.
    /// </summary>
    public UIScope? CurrentScope => _currentScope;

    /// <summary>
    ///  Activates a view, attaches it to the region, and disposes the previous view and its scope.
    /// </summary>
    /// <typeparam name="TView">The view type.</typeparam>
    /// <param name="cancellationToken">Cancels the activation.</param>
    /// <returns>The activated view.</returns>
    public TView Navigate<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TView>(
        CancellationToken cancellationToken = default)
        where TView : Control
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        UIActivation<TView> activation = _activator.CreateControl<TView>(
            UIScopeKind.Navigation,
            UIServiceScopeMode.Isolated,
            cancellationToken);

        try
        {
            _region.Controls.Add(activation.Instance);
        }
        catch
        {
            activation.Dispose();
            throw;
        }

        DetachCurrent();
        _currentView = activation.Instance;
        _currentScope = activation.Scope;

        return activation.Instance;
    }

    /// <summary>
    ///  Detaches and disposes the current view and its scope.
    /// </summary>
    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        DetachCurrent();
    }

    /// <summary>
    ///  Detaches and disposes the current view and its scope.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DetachCurrent();
    }

    private void DetachCurrent()
    {
        Control? view = _currentView;
        UIScope? scope = _currentScope;
        _currentView = null;
        _currentScope = null;

        if (view is not null)
        {
            _region.Controls.Remove(view);
        }

        scope?.Dispose();
    }
}
