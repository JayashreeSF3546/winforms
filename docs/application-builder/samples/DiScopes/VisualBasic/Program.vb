' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Imports System.Diagnostics
Imports System.Threading
Imports System.Windows.Forms
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.Hosting
Imports Microsoft.Extensions.WinForms

Friend Module Program
    <STAThread>
    Friend Sub Main(args As String())
        System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.SystemAware)
        System.Windows.Forms.Application.EnableVisualStyles()
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(False)

        Dim hostBuilder As HostApplicationBuilder =
            Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args)
        hostBuilder.Services.AddScoped(Of SessionService)()
        hostBuilder.Services.AddHostedService(Of ShutdownDiagnosticsService)()
        Dim host As IHost = hostBuilder.Build()

        Dim application As WinFormsApplication = Nothing
        Dim applicationBuilder As WinFormsApplicationBuilder =
            WinFormsApplication.CreateBuilder() _
                .UseHost(host) _
                .UseStartupForm(
                    Function(services)
                        If application Is Nothing Then
                            Throw New InvalidOperationException("The application has not been built.")
                        End If

                        Return ActivatorUtilities.CreateInstance(Of MainForm)(services, application)
                    End Function)

        Using builtApplication As WinFormsApplication = applicationBuilder.Build()
            application = builtApplication
            builtApplication.Run()
        End Using
    End Sub
End Module

''' <summary>
'''  Represents one Form activation's scoped service.
''' </summary>
Friend NotInheritable Class SessionService
    Implements IDisposable

    Private _disposed As Integer

    Public Sub New()
        Id = Guid.NewGuid()
        Debug.WriteLine($"Session service {Id:N} created.")
    End Sub

    Public ReadOnly Property Id As Guid

    Public ReadOnly Property IsDisposed As Boolean
        Get
            Return Volatile.Read(_disposed) <> 0
        End Get
    End Property

    Public Sub Dispose() Implements IDisposable.Dispose
        If Interlocked.Exchange(_disposed, 1) = 0 Then
            Debug.WriteLine($"Session service {Id:N} disposed.")
        End If
    End Sub
End Class

''' <summary>
'''  Logs host cancellation while the WinForms application shuts down.
''' </summary>
Friend NotInheritable Class ShutdownDiagnosticsService
    Inherits BackgroundService

    Protected Overrides Async Function ExecuteAsync(stoppingToken As CancellationToken) As Task
        Using timer As New PeriodicTimer(TimeSpan.FromSeconds(1))
            Try
                While Await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(False)
                    Debug.WriteLine($"Background service heartbeat at {DateTimeOffset.Now}.")
                End While
            Catch ex As OperationCanceledException When stoppingToken.IsCancellationRequested
                Debug.WriteLine("Background service observed host shutdown.")
            End Try
        End Using
    End Function
End Class
