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
'''  Demonstrates requesting application stop and observing worker cancellation.
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
        hostBuilder.Services.AddHostedService(Of CancellableService)()
        Dim genericHost As IHost = hostBuilder.Build()
        Dim form As New CancellationForm(recorder, genericHost)

        Using application As WinFormsApplication =
            WinFormsApplication.CreateBuilder().
                UseHost(genericHost).
                UseStartupForm(form).
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
'''  Records a long-running operation and the cancellation signal from the host.
''' </summary>
Friend NotInheritable Class CancellableService
    Inherits BackgroundService

    Private ReadOnly _recorder As LifecycleRecorder

    Public Sub New(recorder As LifecycleRecorder)
        _recorder = recorder
    End Sub

    Protected Overrides Async Function ExecuteAsync(stoppingToken As CancellationToken) As Task
        _recorder.Record("cancellable-service-started")

        Try
            Await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken)
        Catch ex As OperationCanceledException When stoppingToken.IsCancellationRequested
            _recorder.Record("cancellation-token-observed")
        End Try
    End Function
End Class

''' <summary>
'''  Provides a button that signals the host to stop and cancel its workers.
''' </summary>
Friend NotInheritable Class CancellationForm
    Inherits Form

    Public Sub New(recorder As LifecycleRecorder, host As IHost)
        Text = "Cancellation"
        ClientSize = New Size(460, 150)

        Dim instructions As New Label With {
            .AutoSize = True,
            .Location = New Point(16, 18),
            .Text = "Request host stop to cancel the background worker."
        }

        Dim cancelButton As New Button With {
            .AutoSize = True,
            .Location = New Point(16, 58),
            .Text = "Cancel worker and stop"
        }

        AddHandler cancelButton.Click,
            Async Sub(sender, e)
                cancelButton.Enabled = False
                recorder.Record("stop-requested-from-form")
                Await host.StopAsync()
            End Sub

        Controls.Add(instructions)
        Controls.Add(cancelButton)
    End Sub
End Class
