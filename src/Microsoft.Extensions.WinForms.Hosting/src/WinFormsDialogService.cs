// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Forms;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Borrows the application's existing UI coordinator instead of creating a second scope tree.
/// </summary>
internal sealed class WinFormsDialogService(WinFormsApplication application) : IWinFormsDialogService
{
    public DialogResult ShowDialog(Func<IServiceProvider, Form> formFactory, WinFormsFormScope? owner = null)
    {
        ArgumentNullException.ThrowIfNull(formFactory);
        using WinFormsFormScope dialog = application.UiScope.CreateForm(formFactory, owner);
        return dialog.ShowDialog();
    }
}
