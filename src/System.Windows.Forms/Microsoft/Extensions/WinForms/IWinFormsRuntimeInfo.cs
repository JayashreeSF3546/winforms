// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Provides runtime and process information for a Windows Forms application.
/// </summary>
public interface IWinFormsRuntimeInfo
{
    /// <summary>
    ///  Gets the description of the runtime.
    /// </summary>
    string FrameworkDescription { get; }

    /// <summary>
    ///  Gets the description of the operating system.
    /// </summary>
    string OperatingSystemDescription { get; }

    /// <summary>
    ///  Gets the architecture of the current process.
    /// </summary>
    Architecture ProcessArchitecture { get; }

    /// <summary>
    ///  Gets the path of the current process, or null when it is unavailable.
    /// </summary>
    string? ProcessPath { get; }

    /// <summary>
    ///  Gets the identifier of the current process.
    /// </summary>
    int ProcessId { get; }

    /// <summary>
    ///  Gets the current process working set in bytes.
    /// </summary>
    long WorkingSet { get; }

    /// <summary>
    ///  Gets the current stack trace.
    /// </summary>
    string StackTrace { get; }
}
