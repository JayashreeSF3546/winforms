// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  One node of the Windows Forms ownership graph. A scope owns an optional dependency-injection
///  scope, the object that was activated into it, and every child scope created from it.
/// </summary>
/// <remarks>
///  <para>
///   Disposal is deterministic and idempotent. It runs in this order: child scopes in reverse
///   creation order, then the owning object when the scope owns it, then the scoped services, and
///   finally the <see cref="Disposed"/> event. Services are therefore still resolvable while the
///   owning object is being disposed.
///  </para>
///  <para>
///   Scopes are affine to the thread that created them, because the objects they own are Windows
///   Forms objects. Create and dispose them on the UI thread.
///  </para>
/// </remarks>
public sealed class UIScope : IDisposable
{
    private readonly Lock _syncRoot = new();
    private readonly List<UIScope> _children = [];
    private readonly IServiceScope? _serviceScope;
    private object? _owner;
    private bool _ownsOwner;
    private bool _disposed;

    internal UIScope(
        UIScopeKind kind,
        string name,
        UIScope? parent,
        IServiceProvider services,
        IServiceScope? serviceScope)
    {
        Kind = kind;
        Name = name;
        Parent = parent;
        Services = services;
        _serviceScope = serviceScope;
        UIScopeDiagnostics.OnScopeCreated();
    }

    /// <summary>
    ///  Occurs after the scope, its children, its owner, and its services have been disposed.
    /// </summary>
    public event EventHandler? Disposed;

    /// <summary>
    ///  Gets what this scope owns.
    /// </summary>
    public UIScopeKind Kind { get; }

    /// <summary>
    ///  Gets the diagnostic name of the scope.
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///  Gets the scope that created this scope, or <see langword="null"/> for the application scope.
    /// </summary>
    public UIScope? Parent { get; }

    /// <summary>
    ///  Gets the service provider used to resolve services for this scope.
    /// </summary>
    public IServiceProvider Services { get; }

    /// <summary>
    ///  Gets the object activated into this scope, when one exists. The reference is released when
    ///  the scope is disposed.
    /// </summary>
    public object? Owner => _owner;

    /// <summary>
    ///  Gets a value indicating whether the scope has been disposed.
    /// </summary>
    public bool IsDisposed => Volatile.Read(ref _disposed);

    /// <summary>
    ///  Gets a snapshot of the child scopes that are currently alive.
    /// </summary>
    public IReadOnlyList<UIScope> Children
    {
        get
        {
            lock (_syncRoot)
            {
                return [.. _children];
            }
        }
    }

    /// <summary>
    ///  Creates a child scope that this scope owns.
    /// </summary>
    /// <param name="kind">What the new scope owns.</param>
    /// <param name="scopeMode">
    ///  Whether the child creates its own dependency-injection scope or shares this scope's
    ///  services.
    /// </param>
    /// <param name="name">An optional diagnostic name. The scope kind is used when it is omitted.</param>
    /// <returns>The new child scope.</returns>
    /// <exception cref="ObjectDisposedException">The scope has been disposed.</exception>
    /// <exception cref="InvalidOperationException">
    ///  The call was made at design time, or the Windows Forms activation services are not
    ///  registered.
    /// </exception>
    public UIScope CreateChildScope(
        UIScopeKind kind,
        UIServiceScopeMode scopeMode = UIServiceScopeMode.Isolated,
        string? name = null)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed), this);
        DesignTimeGuard.ThrowIfDesignTime();

        string scopeName = name ?? kind.ToString();

        if (scopeMode == UIServiceScopeMode.Shared)
        {
            UIScope sharedChild = new(kind, scopeName, this, Services, serviceScope: null);
            AddChild(sharedChild);

            return sharedChild;
        }

        IServiceScopeFactory scopeFactory = Services.GetService<IServiceScopeFactory>()
            ?? throw ActivationServices.MissingServicesException();

        IServiceScope serviceScope = scopeFactory.CreateScope();

        try
        {
            UIScope child = new(kind, scopeName, this, serviceScope.ServiceProvider, serviceScope);
            UIScopeHolder holder = serviceScope.ServiceProvider.GetService<UIScopeHolder>()
                ?? throw ActivationServices.MissingServicesException();

            holder.Attach(child);
            AddChild(child);

            return child;
        }
        catch
        {
            serviceScope.Dispose();
            throw;
        }
    }

    /// <summary>
    ///  Assigns this scope's service provider to <paramref name="target"/> and to every reachable
    ///  Designer-created child control or component that implements
    ///  <see cref="IServiceProviderAssignable"/>.
    /// </summary>
    /// <param name="target">The object to assign services to.</param>
    /// <returns>The number of objects that received the service provider.</returns>
    /// <exception cref="ObjectDisposedException">The scope has been disposed.</exception>
    /// <exception cref="InvalidOperationException">The call was made at design time.</exception>
    /// <remarks>
    ///  <para>
    ///   Components whose <see cref="ISite.DesignMode"/> is <see langword="true"/> are skipped, and
    ///   the walk does not descend into them.
    ///  </para>
    /// </remarks>
    public int AssignServices(object target)
    {
        ArgumentNullException.ThrowIfNull(target);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed), this);
        DesignTimeGuard.ThrowIfDesignTime();

        return ServiceAssignment.Assign(Services, target);
    }

    /// <summary>
    ///  Disposes the scope, its children, the object it owns, and its scoped services.
    /// </summary>
    public void Dispose()
    {
        UIScope[] children;
        object? owner;
        bool ownsOwner;

        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            // The flag is set first so that a re-entrant call - for example, the container disposing
            // this scope while this scope is disposing the container - short-circuits.
            Volatile.Write(ref _disposed, true);
            children = [.. _children];
            _children.Clear();
            owner = _owner;
            ownsOwner = _ownsOwner;
            _owner = null;
        }

        List<Exception>? failures = null;

        for (int i = children.Length - 1; i >= 0; i--)
        {
            Collect(ref failures, children[i].Dispose);
        }

        if (ownsOwner && owner is IDisposable disposableOwner)
        {
            Collect(ref failures, disposableOwner.Dispose);
        }

        if (_serviceScope is not null)
        {
            Collect(ref failures, _serviceScope.Dispose);
        }

        Parent?.RemoveChild(this);
        UIScopeDiagnostics.OnScopeDisposed();
        Disposed?.Invoke(this, EventArgs.Empty);

        if (failures is not null)
        {
            throw new AggregateException(
                $"One or more failures occurred while disposing the '{Name}' UI scope.",
                failures);
        }
    }

    internal void AttachOwner(object owner, bool ownsOwner)
    {
        lock (_syncRoot)
        {
            _owner = owner;
            _ownsOwner = ownsOwner;
        }
    }

    private static void Collect(ref List<Exception>? failures, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }
    }

    private void AddChild(UIScope child)
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                child.Dispose();
                throw new ObjectDisposedException(GetType().FullName);
            }

            _children.Add(child);
        }
    }

    private void RemoveChild(UIScope child)
    {
        lock (_syncRoot)
        {
            _children.Remove(child);
        }
    }
}
