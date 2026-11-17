// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Forms;

namespace ApplicationBuilderAdoption.Migration.CSharp;

/// <summary>
///  Represents existing application code retained unchanged across all three entry-point stages.
/// </summary>
internal sealed partial class LegacyForm : Form
{
    private int _count;

    internal LegacyForm()
    {
        InitializeComponent();
    }

    private void Increment_Click(object? sender, EventArgs e)
        => _countLabel!.Text = $"Count: {++_count}";
}
