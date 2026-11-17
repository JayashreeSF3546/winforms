' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Imports Microsoft.VisualBasic.ApplicationServices

Namespace ApplicationBuilderMigration.VisualBasic
    ' The type's XML documentation is on the other partial declaration in MyApplication.vb.
    ' Keep native legacy event wiring while delegating startup work to UI-free logic.
    Friend Partial Class MyApplication
        Private Sub ApplicationStartup(sender As Object, e As StartupEventArgs) Handles Me.Startup
            e.Cancel = Not _session.Start(e.CommandLine)
        End Sub

        Private Sub ApplicationShutdown(sender As Object, e As EventArgs) Handles Me.Shutdown
            _session.Events.Add("shutdown")
        End Sub
    End Class
End Namespace
