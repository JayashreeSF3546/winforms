// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System.Drawing;
using System.Windows.Forms;

namespace ApplicationBuilderAdoption.Migration.CSharp;

internal sealed partial class LegacyForm
{
    private void InitializeComponent()
    {
        _countLabel = new Label
        {
            Name = "Count",
            Text = "Count: 0",
            Location = new Point(20, 20),
            AutoSize = true
        };
        _incrementButton = new Button
        {
            Name = "Increment",
            Text = "Increment",
            Location = new Point(20, 60),
            Size = new Size(140, 35)
        };
        _incrementButton.Click += Increment_Click;
        Controls.AddRange([_countLabel, _incrementButton]);
        Text = "Existing C# application";
        ClientSize = new Size(360, 140);
    }

    private Label? _countLabel;
    private Button? _incrementButton;
}
