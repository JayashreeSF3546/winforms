' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Imports System.Windows.Forms

''' <summary>
''' Represents existing VB Form code retained unchanged across the startup stages.
''' </summary>
Friend NotInheritable Partial Class LegacyForm
    Inherits Form

    Private _count As Integer

    Public Sub New()
        InitializeComponent()
    End Sub

    Private Sub Increment_Click(sender As Object, e As EventArgs)
        _count += 1
        _countLabel.Text = $"Count: {_count}"
    End Sub
End Class
