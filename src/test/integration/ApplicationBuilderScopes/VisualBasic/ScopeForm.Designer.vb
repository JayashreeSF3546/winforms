' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Imports System.ComponentModel
Imports System.Drawing
Imports System.Windows.Forms

Namespace ApplicationBuilderScopes.VisualBasic
    Public Partial Class ScopeForm
        Private components As IContainer
        Private sessionViewControl As SessionControl
        Private sessionWorkerComponent As SessionComponent
        Private modelessButton As Button
        Private modalButton As Button
        Private nextButton As Button
        Private cancelApplicationButton As Button
        Private closeButton As Button

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing AndAlso components IsNot Nothing Then
                components.Dispose()
            End If

            MyBase.Dispose(disposing)
        End Sub

        Private Sub InitializeComponent()
            components = New Container()
            sessionViewControl = New SessionControl()
            sessionWorkerComponent = New SessionComponent(components)
            modelessButton = New Button With {.Text = "Open modeless", .AutoSize = True}
            modalButton = New Button With {.Text = "Open modal", .AutoSize = True}
            nextButton = New Button With {.Text = "Next page", .AutoSize = True}
            cancelApplicationButton = New Button With {.Text = "Cancel application", .AutoSize = True}
            closeButton = New Button With {.Text = "Close", .AutoSize = True}
            Dim layout As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .Padding = New Padding(12), .AutoScroll = True}
            layout.Controls.Add(sessionViewControl)
            layout.Controls.Add(modelessButton)
            layout.Controls.Add(modalButton)
            layout.Controls.Add(nextButton)
            layout.Controls.Add(cancelApplicationButton)
            layout.Controls.Add(closeButton)
            Controls.Add(layout)
            ClientSize = New Size(700, 200)
            StartPosition = FormStartPosition.CenterScreen
            Text = "Service-free layout"
        End Sub
    End Class
End Namespace
