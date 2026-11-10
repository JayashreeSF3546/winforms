' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Imports Microsoft.VisualBasic.ApplicationServices

Namespace ApplicationBuilderParity.VisualBasic
    ' Exercises native Handles Me event wiring, independently of the C# baseline.
    Friend Partial Class LegacyApplication
        Private Sub OnStartupEvent(sender As Object, e As StartupEventArgs) Handles Me.Startup
            _probe.Ui("starting")
            _probe.Tokens(e.CommandLine)
            e.Cancel = _scenario = "legacy-cancel"
            If _scenario = "legacy-failure" Then
                Throw _probe.Failure
            End If
        End Sub

        Private Sub OnShutdownEvent(sender As Object, e As EventArgs) Handles Me.Shutdown
            _probe.Ui("shutdown")
        End Sub

        Private Sub OnExceptionEvent(sender As Object, e As UnhandledExceptionEventArgs) Handles Me.UnhandledException
            Probe.Require(ReferenceEquals(e.Exception, _probe.Failure), "Lost legacy failure.")
            _probe.Ui("legacy-handled")
            e.ExitApplication = False
        End Sub
    End Class
End Namespace
