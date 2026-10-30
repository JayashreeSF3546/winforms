// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System.Drawing;
using System.Windows.Forms;

namespace ApplicationBuilderScopes.CSharp;

public partial class SessionControl
{
    private Label? identityLabel;

    private void InitializeComponent()
    {
        identityLabel = new Label { AutoSize = true, Text = "Designer-created UserControl: no runtime service" };
        Controls.Add(identityLabel);
        Size = new Size(650, 50);
    }
}
