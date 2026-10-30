// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace ApplicationBuilderScopes.CSharp;

public partial class ScopeForm
{
    private IContainer? components;
    private SessionControl? sessionView;
    private SessionComponent? sessionWorker;
    private Button? modelessButton;
    private Button? modalButton;
    private Button? nextButton;
    private Button? cancelButton;
    private Button? closeButton;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new Container();
        sessionView = new SessionControl();
        sessionWorker = new SessionComponent(components);
        modelessButton = new Button { Text = "Open modeless", AutoSize = true };
        modalButton = new Button { Text = "Open modal", AutoSize = true };
        nextButton = new Button { Text = "Next page", AutoSize = true };
        cancelButton = new Button { Text = "Cancel application", AutoSize = true };
        closeButton = new Button { Text = "Close", AutoSize = true };
        FlowLayoutPanel layout = new() { Dock = DockStyle.Fill, Padding = new Padding(12), AutoScroll = true };
        layout.Controls.Add(sessionView);
        layout.Controls.Add(modelessButton);
        layout.Controls.Add(modalButton);
        layout.Controls.Add(nextButton);
        layout.Controls.Add(cancelButton);
        layout.Controls.Add(closeButton);
        Controls.Add(layout);
        ClientSize = new Size(700, 200);
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Service-free layout";
    }
}
