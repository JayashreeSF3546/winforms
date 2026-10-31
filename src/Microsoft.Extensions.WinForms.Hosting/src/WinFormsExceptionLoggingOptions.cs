// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Selects additional exception notifications and disclosure for an explicitly enabled logging scope.
/// </summary>
public sealed class WinFormsExceptionLoggingOptions
{
    /// <summary>
    ///  Gets or sets whether to subscribe to process-wide CLR unhandled-exception notifications.
    /// </summary>
    public bool LogAppDomainExceptions { get; set; }

    /// <summary>
    ///  Gets or sets whether to subscribe to process-wide unobserved-task notifications without marking them observed.
    /// </summary>
    public bool LogUnobservedTaskExceptions { get; set; }

    /// <summary>
    ///  Gets or sets whether the original exception is passed to the logger. The default is false.
    /// </summary>
    /// <remarks>
    ///  <para>
    ///   Messages, stack traces, and exception data may contain sensitive application or personal information.
    ///  </para>
    /// </remarks>
    public bool IncludeExceptionDetails { get; set; }
}
