' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Imports System.Drawing
Imports System.Windows.Forms

Namespace ApplicationBuilderScopes.VisualBasic
    Public Partial Class SessionControl
        Private identityLabel As Label

        Private Sub InitializeComponent()
            identityLabel = New Label With {.AutoSize = True, .Text = "Designer-created UserControl: no runtime service"}
            Controls.Add(identityLabel)
            Size = New Size(650, 50)
        End Sub
    End Class
End Namespace
