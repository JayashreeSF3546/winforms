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
'''  Demonstrates asynchronous hosted-service cleanup during application shutdown.
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
        hostBuilder.Services.AddHostedService(Of GracefulShutdownService)()
        Dim genericHost As IHost = hostBuilder.Build()
        Dim form As New SampleForm(recorder, genericHost)

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
'''  Performs visible asynchronous cleanup when the host shuts down.
''' </summary>
Friend NotInheritable Class GracefulShutdownService
    Implements IHostedService

    Private ReadOnly _recorder As LifecycleRecorder

    Public Sub New(recorder As LifecycleRecorder)
        _recorder = recorder
    End Sub

    Public Function StartAsync(cancellationToken As CancellationToken) As Task Implements IHostedService.StartAsync
        _recorder.Record("graceful-service-started")
        Return Task.CompletedTask
    End Function

    Public Async Function StopAsync(cancellationToken As CancellationToken) As Task Implements IHostedService.StopAsync
        _recorder.Record("graceful-cleanup-started")
        Await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(False)
        _recorder.Record("graceful-cleanup-completed")
    End Function
End Class

''' <summary>
'''  Keeps a UI timer running while the host completes asynchronous shutdown.
''' </summary>
Friend NotInheritable Class SampleForm
    Inherits Form

    Public Sub New(recorder As LifecycleRecorder, host As IHost)
        Text = "Graceful shutdown"
        ClientSize = New Size(500, 190)

        Dim status As New Label With {
            .AutoSize = True,
            .Location = New Point(16, 18),
            .Text = "UI message-loop ticks: 0"
        }

        Dim instructions As New Label With {
            .AutoSize = True,
            .Location = New Point(16, 44),
            .Text = "Request shutdown, then move this window while cleanup runs."
        }

        Dim stopButton As New Button With {
            .AutoSize = True,
            .Location = New Point(16, 82),
            .Text = "Request graceful shutdown"
        }

        AddHandler stopButton.Click,
            Async Sub(sender, e)
                stopButton.Enabled = False
                recorder.Record("graceful-shutdown-requested")
                Await host.StopAsync()
            End Sub

        Dim tickCount As Integer
        Dim uiTimer As New System.Windows.Forms.Timer With {.Interval = 100}
        AddHandler uiTimer.Tick,
            Sub(sender, e)
                tickCount += 1
                status.Text = $"UI message-loop ticks: {tickCount}"
            End Sub
        AddHandler FormClosed, Sub(sender, e) uiTimer.Dispose()
        uiTimer.Start()

        Controls.Add(status)
        Controls.Add(instructions)
        Controls.Add(stopButton)
    End Sub
End Class
