// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Controls whether an activation creates its own dependency-injection scope.
/// </summary>
public enum UIServiceScopeMode
{
    /// <summary>
    ///  Create a new dependency-injection scope for the activated object. Scoped services are
    ///  resolved independently of the activating scope, and are disposed with the new UI scope.
    /// </summary>
    Isolated,

    /// <summary>
    ///  Reuse the activating scope's services. The new UI scope participates in ownership and
    ///  disposal only, so the activated object observes the same scoped service instances as the
    ///  object that activated it.
    /// </summary>
    Shared
}
