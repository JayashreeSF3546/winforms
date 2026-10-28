' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Imports System.Windows.Forms

''' <summary>
''' Replaces its main form without ending the root application loop.
''' </summary>
Friend NotInheritable Class SwitchingContext
    Inherits ApplicationContext

    Friend Sub New()
        MainForm = New MainForm("The custom context can replace this window without ending the run.", "Continue", AddressOf ShowNext)
    End Sub

    Friend Sub ShowNext()
        Dim previous As Form = MainForm
        MainForm = New MainForm(
            "The old window is closed. Closing this window now ends the run.", "Close window", requestShutdown:=Nothing)
        MainForm.Show()
        previous.Close()
        Debug.WriteLine("Context switched")
    End Sub
End Class
