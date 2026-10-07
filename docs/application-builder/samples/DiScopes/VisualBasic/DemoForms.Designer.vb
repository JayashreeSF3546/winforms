' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Imports System.ComponentModel
Imports System.Drawing
Imports System.Windows.Forms

Partial Class MainForm
    Private components As IContainer
    Private _openModelessButton As Button
    Private _reviewButton As Button
    Private _closeButton As Button
    Private _statusLabel As Label
    Private _hostDescriptionLabel As Label
    Private _sessionSummary As SessionSummaryControl
    Private _sessionDiagnostics As SessionDiagnosticsComponent

    Private Sub InitializeComponent()
        components = New Container()
        _sessionDiagnostics = New SessionDiagnosticsComponent(components)
        _openModelessButton = New Button()
        _reviewButton = New Button()
        _closeButton = New Button()
        _statusLabel = New Label()
        _hostDescriptionLabel = New Label()
        _sessionSummary = New SessionSummaryControl()
        SuspendLayout()

        _statusLabel.AutoSize = True
        _statusLabel.Location = New Point(16, 16)
        _statusLabel.Text = "Designer construction uses the parameterless path."

        _sessionSummary.Location = New Point(16, 44)
        _sessionSummary.Size = New Size(530, 48)

        _hostDescriptionLabel.AutoSize = True
        _hostDescriptionLabel.Location = New Point(16, 104)
        _hostDescriptionLabel.Text = "Close this window to cancel the hosted background service."

        _openModelessButton.Location = New Point(16, 145)
        _openModelessButton.Size = New Size(175, 36)
        _openModelessButton.Text = "Open modeless Form"
        AddHandler _openModelessButton.Click, AddressOf OnOpenModelessClick

        _reviewButton.Location = New Point(204, 145)
        _reviewButton.Size = New Size(175, 36)
        _reviewButton.Text = "Review navigation"
        AddHandler _reviewButton.Click, AddressOf OnReviewClick

        _closeButton.Anchor = AnchorStyles.Bottom Or AnchorStyles.Right
        _closeButton.Location = New Point(436, 205)
        _closeButton.Size = New Size(110, 32)
        _closeButton.Text = "Close"
        AddHandler _closeButton.Click, AddressOf OnCloseClick

        AutoScaleDimensions = New SizeF(7.0F, 15.0F)
        AutoScaleMode = AutoScaleMode.Font
        ClientSize = New Size(562, 253)
        Controls.Add(_statusLabel)
        Controls.Add(_sessionSummary)
        Controls.Add(_hostDescriptionLabel)
        Controls.Add(_openModelessButton)
        Controls.Add(_reviewButton)
        Controls.Add(_closeButton)
        MinimumSize = New Size(578, 292)
        StartPosition = FormStartPosition.CenterScreen
        Text = "Application Builder DI and Scopes"
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing AndAlso components IsNot Nothing Then
            components.Dispose()
        End If

        MyBase.Dispose(disposing)
    End Sub
End Class

Partial Class DetailsForm
    Private _sessionLabel As Label
    Private _parentSessionLabel As Label
    Private _closeButton As Button

    Private Sub InitializeComponent()
        _sessionLabel = New Label()
        _parentSessionLabel = New Label()
        _closeButton = New Button()
        SuspendLayout()

        _sessionLabel.AutoSize = True
        _sessionLabel.Location = New Point(16, 20)
        _sessionLabel.Text = "Modeless Form scope: available at runtime"

        _parentSessionLabel.AutoSize = True
        _parentSessionLabel.Location = New Point(16, 55)
        _parentSessionLabel.Text = "Opened from scope: available at runtime"

        _closeButton.Anchor = AnchorStyles.Bottom Or AnchorStyles.Right
        _closeButton.Location = New Point(250, 106)
        _closeButton.Size = New Size(100, 32)
        _closeButton.Text = "Close"
        AddHandler _closeButton.Click, Sub(sender, e) Close()

        AutoScaleDimensions = New SizeF(7.0F, 15.0F)
        AutoScaleMode = AutoScaleMode.Font
        ClientSize = New Size(370, 156)
        Controls.Add(_sessionLabel)
        Controls.Add(_parentSessionLabel)
        Controls.Add(_closeButton)
        MinimumSize = New Size(386, 195)
        StartPosition = FormStartPosition.CenterParent
        Text = "Modeless Details"
        ResumeLayout(False)
        PerformLayout()
    End Sub
End Class

Partial Class ReviewDialog
    Private _scopeLabel As Label
    Private _parentSessionLabel As Label
    Private _continueButton As Button
    Private _cancelButton As Button

    Private Sub InitializeComponent()
        _scopeLabel = New Label()
        _parentSessionLabel = New Label()
        _continueButton = New Button()
        _cancelButton = New Button()
        SuspendLayout()

        _scopeLabel.AutoSize = True
        _scopeLabel.Location = New Point(16, 18)
        _scopeLabel.Text = "Modal dialog scope: available at runtime"

        _parentSessionLabel.AutoSize = True
        _parentSessionLabel.Location = New Point(16, 48)
        _parentSessionLabel.Text = "Opened from scope: available at runtime"

        _continueButton.Anchor = AnchorStyles.Bottom Or AnchorStyles.Right
        _continueButton.DialogResult = DialogResult.OK
        _continueButton.Location = New Point(198, 101)
        _continueButton.Size = New Size(100, 32)
        _continueButton.Text = "Continue"

        _cancelButton.Anchor = AnchorStyles.Bottom Or AnchorStyles.Right
        _cancelButton.DialogResult = DialogResult.Cancel
        _cancelButton.Location = New Point(306, 101)
        _cancelButton.Size = New Size(100, 32)
        _cancelButton.Text = "Cancel"

        AcceptButton = _continueButton
        AutoScaleDimensions = New SizeF(7.0F, 15.0F)
        AutoScaleMode = AutoScaleMode.Font
        CancelButton = _cancelButton
        ClientSize = New Size(422, 151)
        Controls.Add(_scopeLabel)
        Controls.Add(_parentSessionLabel)
        Controls.Add(_continueButton)
        Controls.Add(_cancelButton)
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        StartPosition = FormStartPosition.CenterParent
        Text = "Confirm Navigation"
        ResumeLayout(False)
        PerformLayout()
    End Sub
End Class

Partial Class SessionSummaryControl
    Private _summaryLabel As Label

    Private Sub InitializeComponent()
        _summaryLabel = New Label()
        SuspendLayout()

        _summaryLabel.AutoSize = True
        _summaryLabel.Location = New Point(8, 12)
        _summaryLabel.Text = "Designer-created UserControl (no runtime scope assigned)"

        AutoScaleDimensions = New SizeF(7.0F, 15.0F)
        AutoScaleMode = AutoScaleMode.Font
        Controls.Add(_summaryLabel)
        Name = "SessionSummaryControl"
        Size = New Size(530, 48)
        ResumeLayout(False)
        PerformLayout()
    End Sub
End Class
