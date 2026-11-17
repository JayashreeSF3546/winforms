' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Imports System.Drawing
Imports System.Windows.Forms

Friend NotInheritable Partial Class LegacyForm
    Private Sub InitializeComponent()
        _countLabel = New Label With {
            .Name = "Count",
            .Text = "Count: 0",
            .Location = New Point(20, 20),
            .AutoSize = True
        }
        _incrementButton = New Button With {
            .Name = "Increment",
            .Text = "Increment",
            .Location = New Point(20, 60),
            .Size = New Size(140, 35)
        }
        AddHandler _incrementButton.Click, AddressOf Increment_Click
        Controls.AddRange({_countLabel, _incrementButton})
        Text = "Existing VB application"
        ClientSize = New Size(360, 140)
    End Sub

    Private _countLabel As Label
    Private _incrementButton As Button
End Class
