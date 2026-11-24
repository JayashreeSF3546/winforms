// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Forms;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Shows factory-created modal forms with application-tracked dependency injection scopes.
/// </summary>
public interface IWinFormsDialogService
{
    /// <summary>
    ///  Creates a fresh dialog scope, shows its form, and disposes both when the modal invocation unwinds.
    /// </summary>
    /// <param name="formFactory">Creates a fresh, unshown form from its dialog-scoped services.</param>
    /// <param name="owner">An active owner scope from the same application, or null for an unowned dialog.</param>
    /// <returns>The dialog's modal result.</returns>
    /// <remarks>
    ///  <para>
    ///   Select scoped startup and call on the application's owning STA after activation begins and
    ///   before shutdown. Construct the form explicitly; do not resolve container-owned UI.
    ///   Form cleanup must precede base.Dispose. Async-only disposable scoped services are unsupported.
    ///   Factory, modal execution, and cleanup failures propagate.
    ///  </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="formFactory"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The application, owner, or returned form is disposed.</exception>
    /// <exception cref="InvalidOperationException">
    ///  Scoped startup is unavailable, activation is attempted on the wrong thread or during shutdown,
    ///  the owner belongs to another application, or the returned form cannot transfer ownership.
    /// </exception>
    DialogResult ShowDialog(Func<IServiceProvider, Form> formFactory, WinFormsFormScope? owner = null);
}
