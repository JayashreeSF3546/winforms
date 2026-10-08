Imports System.ComponentModel
Imports System.Drawing
Imports System.Windows.Forms

Namespace WinFormsApplicationBuilderApp
    <Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
    Partial Class MainForm
        Inherits Form

        Private components As IContainer
        Private greetingLabel As Label
        Private nameTextBox As TextBox
        Private WithEvents greetButton As Button
        Private WithEvents saveButton As Button
        Private statusLabel As Label

        Protected Overrides Sub Dispose(disposing As Boolean)
            Try
                If disposing AndAlso components IsNot Nothing Then
                    components.Dispose()
                End If
            Finally
                MyBase.Dispose(disposing)
            End Try
        End Sub

        <System.Diagnostics.DebuggerStepThrough()>
        Private Sub InitializeComponent()
            components = New Container()
            greetingLabel = New Label()
            nameTextBox = New TextBox()
            greetButton = New Button()
            saveButton = New Button()
            statusLabel = New Label()
            SuspendLayout()
            '
            ' greetingLabel
            '
            greetingLabel.AutoSize = True
            greetingLabel.Location = New Point(20, 20)
            greetingLabel.Name = "greetingLabel"
            greetingLabel.Size = New Size(115, 15)
            greetingLabel.Text = "Hello, developer!"
            '
            ' nameTextBox
            '
            nameTextBox.Location = New Point(20, 55)
            nameTextBox.Name = "nameTextBox"
            nameTextBox.PlaceholderText = "Your name"
            nameTextBox.Size = New Size(220, 23)
            '
            ' greetButton
            '
            greetButton.Location = New Point(255, 54)
            greetButton.Name = "greetButton"
            greetButton.Size = New Size(90, 25)
            greetButton.Text = "Greet"
            greetButton.UseVisualStyleBackColor = True
            '
            ' saveButton
            '
            saveButton.Location = New Point(355, 54)
            saveButton.Name = "saveButton"
            saveButton.Size = New Size(110, 25)
            saveButton.Text = "Save name"
            saveButton.UseVisualStyleBackColor = True
            '
            ' statusLabel
            '
            statusLabel.AutoSize = True
            statusLabel.Location = New Point(20, 100)
            statusLabel.Name = "statusLabel"
            statusLabel.Size = New Size(0, 15)
            '
            ' MainForm
            '
            AutoScaleDimensions = New SizeF(7.0F, 15.0F)
            AutoScaleMode = AutoScaleMode.Font
            ClientSize = New Size(490, 145)
            Controls.Add(statusLabel)
            Controls.Add(saveButton)
            Controls.Add(greetButton)
            Controls.Add(nameTextBox)
            Controls.Add(greetingLabel)
            Name = "MainForm"
            Text = "WinForms Application Builder"
            ResumeLayout(False)
            PerformLayout()
        End Sub
    End Class
End Namespace
