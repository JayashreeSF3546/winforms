// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;

namespace ApplicationBuilderScopes.CSharp;

/// <summary>
///  Is created by the Designer container and receives a borrowed dependency only at runtime.
/// </summary>
public sealed class SessionComponent : Component
{
    private bool _disposed;

    public SessionComponent()
    {
        Disposed += (_, _) => _disposed = true;
    }

    public SessionComponent(IContainer container) : this() => container.Add(this);

    internal ScopeSession? Session { get; private set; }

    internal void InitializeServices(ScopeSession session)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(session);
        if (Session is not null)
        {
            throw new InvalidOperationException("Services have already been assigned.");
        }

        Session = session;
        Disposed += (_, _) =>
        {
            session.VerifyAlive();
            Console.WriteLine($"Component disposed: {session.Id}");
        };
    }
}
