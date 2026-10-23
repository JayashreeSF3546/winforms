// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.ObjectModel;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Provides arguments for a request to activate a running Windows Forms application.
/// </summary>
public sealed class WinFormsApplicationActivationEventArgs : EventArgs
{
    private readonly ReadOnlyCollection<string> _arguments;

    internal WinFormsApplicationActivationEventArgs(string[] arguments)
    {
        _arguments = Array.AsReadOnly([.. arguments]);
    }

    /// <summary>
    ///  Gets the command-line arguments forwarded by the activating instance.
    /// </summary>
    public IReadOnlyList<string> Arguments => _arguments;

    /// <summary>
    ///  Gets or sets whether the main form should be restored and activated after
    ///  the activation event is raised.
    /// </summary>
    public bool BringToForeground { get; set; } = true;
}
