// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Identifies what a <see cref="UIScope"/> owns.
/// </summary>
/// <remarks>
///  <para>
///   The kind is diagnostic and organizational. It does not change disposal ordering, which is
///   always children first, then the owning object, then the scoped services.
///  </para>
/// </remarks>
public enum UIScopeKind
{
    /// <summary>
    ///  The root scope of a built application. It is created once and disposed with the host.
    /// </summary>
    Application,

    /// <summary>
    ///  A scope that owns a modeless or startup <see cref="System.Windows.Forms.Form"/>.
    /// </summary>
    Form,

    /// <summary>
    ///  A scope that owns a modal dialog.
    /// </summary>
    Dialog,

    /// <summary>
    ///  A scope that owns an explicitly activated <see cref="System.Windows.Forms.Control"/>.
    /// </summary>
    Control,

    /// <summary>
    ///  A scope that owns an explicitly activated <see cref="System.ComponentModel.IComponent"/>.
    /// </summary>
    Component,

    /// <summary>
    ///  A scope that owns one navigated view inside a navigation region.
    /// </summary>
    Navigation
}
