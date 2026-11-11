' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Imports System.Windows.Forms
Imports Microsoft.VisualBasic.ApplicationServices

Namespace ApplicationBuilderParity.VisualBasic
    ''' <summary>
    '''  Uses the generated-application partial-class shape against the unchanged public VB framework.
    ''' </summary>
    ''' <remarks>
    '''  This is a checked-in source fixture, not evidence that the Visual Studio generator ran.
    ''' </remarks>
    Friend Partial Class LegacyApplication
        Inherits WindowsFormsApplicationBase

        Private ReadOnly _probe As Probe
        Private ReadOnly _scenario As String

        Friend Sub New(probe As Probe, scenario As String)
            MyBase.New(AuthenticationMode.ApplicationDefined)
            _probe = probe
            _scenario = scenario
            EnableVisualStyles = False
            SaveMySettingsOnExit = False
        End Sub

        Protected Overrides Sub OnCreateMainForm()
            _probe.Ui("activate")
            MainForm = New Form With {.ShowInTaskbar = False}
            AddHandler MainForm.Shown,
                Sub(sender, e)
                    _probe.Guard(
                        Sub()
                            _probe.Ui("shown")
                            MainForm.Close()
                        End Sub)
                End Sub
        End Sub
    End Class
End Namespace
