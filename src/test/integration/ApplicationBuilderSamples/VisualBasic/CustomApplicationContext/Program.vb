' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Imports System.Drawing
Imports System.Windows.Forms
Imports Microsoft.Extensions.Hosting
Imports Microsoft.Extensions.WinForms

''' <summary>
'''  Demonstrates supplying a custom ApplicationContext.
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
        Dim context As New SampleApplicationContext(recorder)

        Using application As WinFormsApplication =
            WinFormsApplication.CreateBuilder().
                UseHost(genericHost).
                UseApplicationContext(context).
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
'''  Owns the sample form and records its context lifecycle.
''' </summary>
Friend NotInheritable Class SampleApplicationContext
    Inherits ApplicationContext

    Public Sub New(recorder As LifecycleRecorder)
        MyBase.New(CreateMainForm(recorder))
        recorder.Record("custom-context-created")
        AddHandler ThreadExit, Sub(sender, e) recorder.Record("custom-context-thread-exit")
    End Sub

    Private Shared Function CreateMainForm(recorder As LifecycleRecorder) As Form
        recorder.Record("context-main-form-created")

        Dim form As New Form With {
            .Text = "Custom ApplicationContext",
            .ClientSize = New Size(460, 120)
        }

        form.Controls.Add(New Label With {
            .AutoSize = True,
            .Location = New Point(16, 20),
            .Text = "Close this form to exit the custom context."
        })

        Return form
    End Function
End Class
