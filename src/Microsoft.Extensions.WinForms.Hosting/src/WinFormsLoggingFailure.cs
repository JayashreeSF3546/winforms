// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Describes a logging-provider failure for the application's separate diagnostic fallback.
/// </summary>
public sealed class WinFormsLoggingFailure
{
    internal WinFormsLoggingFailure(WinFormsDiagnosticSource source, Exception exception)
    {
        Source = source;
        Exception = exception;
    }

    /// <summary>
    ///  Gets the diagnostic whose logging failed.
    /// </summary>
    public WinFormsDiagnosticSource Source { get; }

    /// <summary>
    ///  Gets the logging failure, not the original application exception.
    /// </summary>
    public Exception Exception { get; }
}
