// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System.Drawing;
using System.Windows.Forms;

namespace ApplicationBuilderSample.DiScopes;

internal sealed partial class MainForm
{
    private System.ComponentModel.IContainer? components;
    private Button? _openModelessButton;
    private Button? _reviewButton;
    private Button? _closeButton;
    private Label? _statusLabel;
    private Label? _hostDescriptionLabel;
    private SessionSummaryControl? _sessionSummary;
    private SessionDiagnosticsComponent? _sessionDiagnostics;

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        _sessionDiagnostics = new SessionDiagnosticsComponent(components);
        _openModelessButton = new Button();
        _reviewButton = new Button();
        _closeButton = new Button();
        _statusLabel = new Label();
        _hostDescriptionLabel = new Label();
        _sessionSummary = new SessionSummaryControl();
        SuspendLayout();

        _statusLabel.AutoSize = true;
        _statusLabel.Location = new Point(16, 16);
        _statusLabel.Text = "Designer construction uses the parameterless path.";

        _sessionSummary.Location = new Point(16, 44);
        _sessionSummary.Size = new Size(530, 48);

        _hostDescriptionLabel.AutoSize = true;
        _hostDescriptionLabel.Location = new Point(16, 104);
        _hostDescriptionLabel.Text = "Close this window to cancel the hosted background service.";

        _openModelessButton.Location = new Point(16, 145);
        _openModelessButton.Size = new Size(175, 36);
        _openModelessButton.Text = "Open modeless Form";
        _openModelessButton.Click += OnOpenModelessClick;

        _reviewButton.Location = new Point(204, 145);
        _reviewButton.Size = new Size(175, 36);
        _reviewButton.Text = "Review navigation";
        _reviewButton.Click += OnReviewClick;

        _closeButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _closeButton.Location = new Point(436, 205);
        _closeButton.Size = new Size(110, 32);
        _closeButton.Text = "Close";
        _closeButton.Click += OnCloseClick;

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(562, 253);
        Controls.Add(_statusLabel);
        Controls.Add(_sessionSummary);
        Controls.Add(_hostDescriptionLabel);
        Controls.Add(_openModelessButton);
        Controls.Add(_reviewButton);
        Controls.Add(_closeButton);
        MinimumSize = new Size(578, 292);
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Application Builder DI and Scopes";
        ResumeLayout(false);
        PerformLayout();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && components is not null)
        {
            components.Dispose();
        }

        base.Dispose(disposing);
    }
}

internal sealed partial class DetailsForm
{
    private Label? _sessionLabel;
    private Label? _parentSessionLabel;
    private Button? _closeButton;

    private void InitializeComponent()
    {
        _sessionLabel = new Label();
        _parentSessionLabel = new Label();
        _closeButton = new Button();
        SuspendLayout();

        _sessionLabel.AutoSize = true;
        _sessionLabel.Location = new Point(16, 20);
        _sessionLabel.Text = "Modeless Form scope: available at runtime";

        _parentSessionLabel.AutoSize = true;
        _parentSessionLabel.Location = new Point(16, 55);
        _parentSessionLabel.Text = "Opened from scope: available at runtime";

        _closeButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _closeButton.Location = new Point(250, 106);
        _closeButton.Size = new Size(100, 32);
        _closeButton.Text = "Close";
        _closeButton.Click += (_, _) => Close();

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(370, 156);
        Controls.Add(_sessionLabel);
        Controls.Add(_parentSessionLabel);
        Controls.Add(_closeButton);
        MinimumSize = new Size(386, 195);
        StartPosition = FormStartPosition.CenterParent;
        Text = "Modeless Details";
        ResumeLayout(false);
        PerformLayout();
    }
}

internal sealed partial class ReviewDialog
{
    private Label? _scopeLabel;
    private Label? _parentSessionLabel;
    private Button? _continueButton;
    private Button? _cancelButton;

    private void InitializeComponent()
    {
        _scopeLabel = new Label();
        _parentSessionLabel = new Label();
        _continueButton = new Button();
        _cancelButton = new Button();
        SuspendLayout();

        _scopeLabel.AutoSize = true;
        _scopeLabel.Location = new Point(16, 18);
        _scopeLabel.Text = "Modal dialog scope: available at runtime";

        _parentSessionLabel.AutoSize = true;
        _parentSessionLabel.Location = new Point(16, 48);
        _parentSessionLabel.Text = "Opened from scope: available at runtime";

        _continueButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _continueButton.DialogResult = DialogResult.OK;
        _continueButton.Location = new Point(198, 101);
        _continueButton.Size = new Size(100, 32);
        _continueButton.Text = "Continue";

        _cancelButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _cancelButton.DialogResult = DialogResult.Cancel;
        _cancelButton.Location = new Point(306, 101);
        _cancelButton.Size = new Size(100, 32);
        _cancelButton.Text = "Cancel";

        AcceptButton = _continueButton;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = _cancelButton;
        ClientSize = new Size(422, 151);
        Controls.Add(_scopeLabel);
        Controls.Add(_parentSessionLabel);
        Controls.Add(_continueButton);
        Controls.Add(_cancelButton);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Confirm Navigation";
        ResumeLayout(false);
        PerformLayout();
    }
}

internal sealed partial class SessionSummaryControl
{
    private Label? _summaryLabel;

    private void InitializeComponent()
    {
        _summaryLabel = new Label();
        SuspendLayout();

        _summaryLabel.AutoSize = true;
        _summaryLabel.Location = new Point(8, 12);
        _summaryLabel.Text = "Designer-created UserControl (no runtime scope assigned)";

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(_summaryLabel);
        Name = "SessionSummaryControl";
        Size = new Size(530, 48);
        ResumeLayout(false);
        PerformLayout();
    }
}
