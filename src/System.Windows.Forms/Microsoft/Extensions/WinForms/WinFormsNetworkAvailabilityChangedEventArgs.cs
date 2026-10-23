// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Provides the updated network availability state.
/// </summary>
public sealed class WinFormsNetworkAvailabilityChangedEventArgs : EventArgs
{
    internal WinFormsNetworkAvailabilityChangedEventArgs(bool isAvailable)
    {
        IsAvailable = isAvailable;
    }

    /// <summary>
    ///  Gets whether a network connection is available.
    /// </summary>
    public bool IsAvailable { get; }
}
