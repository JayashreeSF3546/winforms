// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Shared helpers for locating the Windows Forms activation services in a container.
/// </summary>
internal static class ActivationServices
{
    internal static InvalidOperationException MissingServicesException()
        => new(
            "The Windows Forms activation services are not registered. Call "
            + "IServiceCollection.AddWindowsFormsActivation(), or build the application with "
            + "WinFormsApplicationBuilder, before requesting a UI scope or activator.");

    internal static IUIActivator? GetApplicationActivatorOrDefault(IServiceProvider services)
    {
        UIApplicationScopeHolder? holder = services.GetService<UIApplicationScopeHolder>();

        return holder?.GetOrCreateApplicationScope().Services.GetRequiredService<IUIActivator>();
    }
}

/// <summary>
///  Binds one <see cref="UIScope"/> to the dependency-injection scope that backs it.
/// </summary>
/// <remarks>
///  <para>
///   The holder is registered as a scoped service, so every dependency-injection scope has exactly
///   one. It is populated by <see cref="UIScope.CreateChildScope"/> immediately after the
///   dependency-injection scope is created and before any service is resolved from it.
///  </para>
/// </remarks>
internal sealed class UIScopeHolder
{
    internal UIScope? Scope { get; private set; }

    internal void Attach(UIScope scope) => Scope = scope;
}

/// <summary>
///  Creates and owns the application <see cref="UIScope"/>.
/// </summary>
/// <remarks>
///  <para>
///   The holder is registered as a singleton, so the application scope is created once per
///   container and disposed when the container is disposed. The application scope is backed by a
///   real dependency-injection scope, which keeps scoped services resolvable even when the
///   container validates scopes.
///  </para>
/// </remarks>
internal sealed class UIApplicationScopeHolder(IServiceScopeFactory scopeFactory) : IDisposable
{
    private readonly Lock _syncRoot = new();
    private UIScope? _applicationScope;
    private bool _disposed;

    internal UIScope GetOrCreateApplicationScope()
    {
        lock (_syncRoot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_applicationScope is not null)
            {
                return _applicationScope;
            }

            IServiceScope serviceScope = scopeFactory.CreateScope();

            try
            {
                UIScope scope = new(
                    UIScopeKind.Application,
                    nameof(UIScopeKind.Application),
                    parent: null,
                    serviceScope.ServiceProvider,
                    serviceScope);

                UIScopeHolder holder = serviceScope.ServiceProvider.GetService<UIScopeHolder>()
                    ?? throw ActivationServices.MissingServicesException();

                holder.Attach(scope);
                _applicationScope = scope;

                return scope;
            }
            catch
            {
                serviceScope.Dispose();
                throw;
            }
        }
    }

    public void Dispose()
    {
        UIScope? scope;

        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            scope = _applicationScope;
            _applicationScope = null;
        }

        scope?.Dispose();
    }
}
