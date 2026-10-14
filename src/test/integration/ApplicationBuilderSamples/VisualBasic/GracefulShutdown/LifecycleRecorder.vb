' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Imports System.Collections.Concurrent
Imports System.IO
Imports System.Threading

''' <summary>
'''  Records timestamped lifecycle events to memory and a per-sample log file.
''' </summary>
Friend NotInheritable Class LifecycleRecorder
    Private ReadOnly _fileLock As New Object()
    Private ReadOnly _entries As New ConcurrentQueue(Of String)()

    Public ReadOnly Property LogPath As String =
        Path.Combine(AppContext.BaseDirectory, "lifecycle.log")

    Public Event EntryRecorded As EventHandler(Of String)

    Public Sub Record(eventName As String)
        Dim entry As String = String.Join(
            "|",
            DateTimeOffset.UtcNow.ToString("O"),
            Environment.ProcessId,
            Environment.CurrentManagedThreadId,
            Thread.CurrentThread.GetApartmentState(),
            eventName)

        _entries.Enqueue(entry)

        SyncLock _fileLock
            File.AppendAllLines(LogPath, {entry})
        End SyncLock

        RaiseEvent EntryRecorded(Me, entry)
    End Sub

    Public Function GetEntries() As String()
        Return _entries.ToArray()
    End Function
End Class
