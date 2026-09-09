// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Pairs an activated object with the <see cref="UIScope"/> that owns it.
/// </summary>
/// <typeparam name="T">The activated type.</typeparam>
/// <remarks>
///  <para>
///   Disposing the handle disposes the scope, which disposes child scopes, then the instance when
///   the scope owns it, and then the scoped services. Instances that were resolved from the
///   container are owned by the container and are not disposed by the scope.
///  </para>
/// </remarks>
public sealed class UIActivation<T> : IDisposable
    where T : class
{
    internal UIActivation(T instance, UIScope scope)
    {
        Instance = instance;
        Scope = scope;
    }

    /// <summary>
    ///  Gets the activated instance.
    /// </summary>
    public T Instance { get; }

    /// <summary>
    ///  Gets the scope that owns the activated instance.
    /// </summary>
    public UIScope Scope { get; }

    /// <summary>
    ///  Gets a value indicating whether the owning scope has been disposed.
    /// </summary>
    public bool IsDisposed => Scope.IsDisposed;

    /// <summary>
    ///  Disposes the owning scope and everything it owns.
    /// </summary>
    public void Dispose() => Scope.Dispose();
}
