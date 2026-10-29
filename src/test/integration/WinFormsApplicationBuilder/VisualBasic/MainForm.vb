' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' Provides an ordinary close action or an explicit application shutdown action.
''' </summary>
Friend NotInheritable Class MainForm
    Inherits Form

    Public Sub New()
        Me.New("Close this default startup form to finish the application.", "Close window", requestShutdown:=Nothing)
    End Sub

    Friend Sub New(description As String, actionText As String, requestShutdown As Action, Optional vetoClose As Boolean = False)
        Text = "Application Builder - VB"
        ClientSize = New Size(560, 180)
        StartPosition = FormStartPosition.CenterScreen
        Dim layout As New FlowLayoutPanel With {
            .Dock = DockStyle.Fill,
            .FlowDirection = FlowDirection.TopDown,
            .Padding = New Padding(12)
        }
        Dim instructions As New Label With {
            .Text = description,
            .AutoSize = True,
            .MaximumSize = New Size(520, 0)
        }
        Dim action As New Button With {.Text = actionText, .AutoSize = True}
        AddHandler action.Click,
            Sub(sender, e)
                If requestShutdown Is Nothing Then
                    Close()
                Else
                    requestShutdown()
                End If
            End Sub
        layout.Controls.Add(instructions)
        layout.Controls.Add(action)
        If vetoClose Then
            Dim veto As New CheckBox With {.Text = "Cancel ordinary window closing", .Checked = True, .AutoSize = True}
            AddHandler FormClosing, Sub(sender, e) e.Cancel = veto.Checked
            layout.Controls.Add(veto)
        End If

        Controls.Add(layout)
        AddHandler Disposed, Sub(sender, e) Debug.WriteLine("UI disposed")
    End Sub
End Class
