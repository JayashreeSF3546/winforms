' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Imports System.Windows.Forms
Imports Microsoft.VisualBasic.ApplicationServices

Namespace ApplicationBuilderMigration.VisualBasic
    ''' <summary>
    '''  Provides an explicit manual baseline snapshot, not Visual Studio generated output.
    ''' </summary>
    ''' <remarks>
    '''  Real existing projects retain their generated constructor, defaults and selected entry point.
    ''' </remarks>
    Friend Partial Class MyApplication
        Inherits WindowsFormsApplicationBase

        Private ReadOnly _session As MigrationSession
        Private ReadOnly _probe As Boolean

        Friend Sub New(session As MigrationSession, probe As Boolean)
            MyBase.New(AuthenticationMode.ApplicationDefined)
            _session = session
            _probe = probe
            EnableVisualStyles = True
            IsSingleInstance = False
            SaveMySettingsOnExit = False
            ShutdownStyle = ShutdownMode.AfterMainFormCloses
        End Sub

        Protected Overrides Sub OnCreateMainForm()
            _session.Events.Add("activate")
            MainForm = New MainForm(_session, _probe, "legacy")
        End Sub
    End Class
End Namespace
