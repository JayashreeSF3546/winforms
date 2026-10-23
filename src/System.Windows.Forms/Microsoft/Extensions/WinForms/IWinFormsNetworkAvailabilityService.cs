// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Reports network availability and changes to that availability.
/// </summary>
public interface IWinFormsNetworkAvailabilityService
{
    /// <summary>
    ///  Gets whether the local machine has an available network connection.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    ///  Occurs when network availability changes.
    /// </summary>
    event EventHandler<WinFormsNetworkAvailabilityChangedEventArgs>? NetworkAvailabilityChanged;
}
