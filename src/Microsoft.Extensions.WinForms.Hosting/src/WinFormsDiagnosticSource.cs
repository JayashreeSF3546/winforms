// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Identifies the source of an explicitly enabled Windows Forms diagnostic.
/// </summary>
public enum WinFormsDiagnosticSource
{
    /// <summary>
    ///  Startup UI has been activated.
    /// </summary>
    ApplicationStarted,

    /// <summary>
    ///  Application shutdown has been committed.
    /// </summary>
    ApplicationStopping,

    /// <summary>
    ///  UI and host shutdown have finished, before owned host disposal.
    /// </summary>
    ApplicationStopped,

    /// <summary>
    ///  The owning UI thread is routing an exception to the selected application policy.
    /// </summary>
    ThreadException,

    /// <summary>
    ///  The CLR is reporting a process-wide unhandled exception.
    /// </summary>
    AppDomainUnhandledException,

    /// <summary>
    ///  The task scheduler is reporting a process-wide unobserved task exception.
    /// </summary>
    UnobservedTaskException,

    /// <summary>
    ///  Application code is reporting an exception propagated from Run.
    /// </summary>
    RunFailure
}
