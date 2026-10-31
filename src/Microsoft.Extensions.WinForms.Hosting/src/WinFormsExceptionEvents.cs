// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Forms;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Provides replaceable event sources for deterministic subscription and concurrency tests.
/// </summary>
internal interface IWinFormsExceptionEvents
{
    event ThreadExceptionEventHandler? ThreadException;
    event UnhandledExceptionEventHandler? UnhandledException;
    event EventHandler<UnobservedTaskExceptionEventArgs>? UnobservedTaskException;
}

/// <summary>
///  Forwards subscriptions directly to the existing UI-thread and process-wide exception events.
/// </summary>
internal sealed class WinFormsExceptionEvents : IWinFormsExceptionEvents
{
    public event ThreadExceptionEventHandler? ThreadException
    {
        add => Application.ThreadException += value;
        remove => Application.ThreadException -= value;
    }

    public event UnhandledExceptionEventHandler? UnhandledException
    {
        add => AppDomain.CurrentDomain.UnhandledException += value;
        remove => AppDomain.CurrentDomain.UnhandledException -= value;
    }

    public event EventHandler<UnobservedTaskExceptionEventArgs>? UnobservedTaskException
    {
        add => TaskScheduler.UnobservedTaskException += value;
        remove => TaskScheduler.UnobservedTaskException -= value;
    }
}
