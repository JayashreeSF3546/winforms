' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.Extensions.Hosting

''' <summary>
''' Requests host shutdown from background work without blocking UI dispatch.
''' </summary>
Friend NotInheritable Class SampleWorker
    Inherits BackgroundService

    Private ReadOnly _lifetime As IHostApplicationLifetime
    Private ReadOnly _uiReady As Task
    Private ReadOnly _smoke As Boolean

    Friend Sub New(lifetime As IHostApplicationLifetime, uiReady As Task, smoke As Boolean)
        _lifetime = lifetime
        _uiReady = uiReady
        _smoke = smoke
    End Sub

    Protected Overrides Async Function ExecuteAsync(stoppingToken As CancellationToken) As Task
        Debug.WriteLine("Background service started")
        Try
            Await _uiReady.WaitAsync(stoppingToken).ConfigureAwait(False)
            If Not _smoke Then
                For update As Integer = 1 To 5
                    Await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(False)
                    Debug.WriteLine($"Background update: {update}")
                Next
            End If
        Catch exception As OperationCanceledException When _
            stoppingToken.IsCancellationRequested AndAlso exception.CancellationToken = stoppingToken
            Debug.WriteLine("Background work canceled during shutdown")
            Return
        End Try

        Debug.WriteLine("Background requesting shutdown")
        _lifetime.StopApplication()
    End Function

    Public Overrides Async Function StopAsync(cancellationToken As CancellationToken) As Task
        Await MyBase.StopAsync(cancellationToken).ConfigureAwait(False)
        Debug.WriteLine("Background service stopped")
    End Function
End Class
