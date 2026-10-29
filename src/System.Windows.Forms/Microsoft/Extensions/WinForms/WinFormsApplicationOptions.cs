// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Forms;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Stores the immutable startup selection transferred from a builder to its application.
/// </summary>
internal sealed class WinFormsApplicationOptions
{
    internal WinFormsApplicationOptions(
        Thread ownerThread,
        Form? startupForm,
        Func<ApplicationContext>? contextFactory,
        Func<IServiceProvider, Form>? serviceFormFactory)
    {
        Debug.Assert((startupForm is not null ? 1 : 0)
            + (contextFactory is not null ? 1 : 0)
            + (serviceFormFactory is not null ? 1 : 0) == 1);
        OwnerThread = ownerThread;
        StartupForm = startupForm;
        ContextFactory = contextFactory;
        ServiceFormFactory = serviceFormFactory;
    }

    internal Thread OwnerThread { get; }

    internal Form? StartupForm { get; }

    internal Func<ApplicationContext>? ContextFactory { get; }

    internal Func<IServiceProvider, Form>? ServiceFormFactory { get; }

    internal static void ValidateStartupForm(Form form)
    {
        ObjectDisposedException.ThrowIf(form.IsDisposed, form);
        if (form.CreateThreadId != PInvokeCore.GetCurrentThreadId())
        {
            throw new InvalidOperationException(SR.WinFormsApplication_WrongThread);
        }
    }
}
