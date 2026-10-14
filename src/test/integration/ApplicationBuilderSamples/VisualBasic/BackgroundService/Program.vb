' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Imports System.Drawing
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.Hosting
Imports Microsoft.Extensions.WinForms

''' <summary>
'''  Demonstrates a hosted background worker alongside a WinForms startup form.
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
        hostBuilder.Services.AddSingleton(recorder)
        hostBuilder.Services.AddHostedService(Of HeartbeatService)()
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
'''  Records periodic work and observes cancellation during application shutdown.
''' </summary>
Friend NotInheritable Class HeartbeatService
    Inherits BackgroundService

    Private ReadOnly _recorder As LifecycleRecorder

    Public Sub New(recorder As LifecycleRecorder)
        _recorder = recorder
    End Sub

    Protected Overrides Async Function ExecuteAsync(stoppingToken As CancellationToken) As Task
        _recorder.Record("background-service-started")

        Using timer As New PeriodicTimer(TimeSpan.FromSeconds(1))
            Try
                While Await timer.WaitForNextTickAsync(stoppingToken)
                    _recorder.Record("background-service-heartbeat")
                End While
            Catch ex As OperationCanceledException When stoppingToken.IsCancellationRequested
                _recorder.Record("background-service-cancellation-observed")
            End Try
        End Using
    End Function
End Class

''' <summary>
'''  Provides a visible form for the hosted-service sample.
''' </summary>
Friend NotInheritable Class SampleForm
    Inherits Form

    Public Sub New()
        Text = "BackgroundService"
        ClientSize = New Size(460, 120)

        Controls.Add(New Label With {
            .AutoSize = True,
            .Location = New Point(16, 20),
            .Text = "Watch the console and lifecycle.log for worker heartbeats."
        })
    End Sub
End Class
