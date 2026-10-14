' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Imports System.Windows.Forms
Imports Microsoft.Extensions.Hosting
Imports Microsoft.Extensions.WinForms

''' <summary>
'''  Demonstrates selecting a startup form by type.
''' </summary>
Friend Module Program
    <STAThread>
    Friend Sub Main(args As String())
        Dim recorder As New LifecycleRecorder()
        AddHandler recorder.EntryRecorded, Sub(sender, entry) Console.WriteLine(entry)
        recorder.Record("main-entered")

        Application.SetHighDpiMode(HighDpiMode.SystemAware)
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)

        Dim hostBuilder As HostApplicationBuilder = Host.CreateApplicationBuilder(args)
        Dim genericHost As IHost = hostBuilder.Build()

        Using application As WinFormsApplication =
            WinFormsApplication.CreateBuilder().
                UseHost(genericHost).
                UseStartupForm(Of SampleForm)().
                Build()
            AddLifecycleEvents(application, recorder)

            recorder.Record("run-entering")
            application.Run()
            recorder.Record("run-returned")
        End Using
    End Sub

    Private Sub AddLifecycleEvents(application As WinFormsApplication, recorder As LifecycleRecorder)
        AddHandler application.Lifetime.ApplicationStarted,
            Sub(sender, e) recorder.Record("application-started")
        AddHandler application.Lifetime.ApplicationStopping,
            Sub(sender, e) recorder.Record("application-stopping")
        AddHandler application.Lifetime.ApplicationStopped,
            Sub(sender, e) recorder.Record("application-stopped")
    End Sub
End Module

''' <summary>
'''  Provides a visible form for the startup-form sample.
''' </summary>
Friend NotInheritable Class SampleForm
    Inherits Form

    Public Sub New()
        Text = "Default startup form"
        ClientSize = New Size(440, 120)

        Dim instructions As New Label With {
            .AutoSize = True,
            .Location = New Point(16, 20),
            .Text = "Close this form to record application shutdown."
        }

        Controls.Add(instructions)
    End Sub
End Class
