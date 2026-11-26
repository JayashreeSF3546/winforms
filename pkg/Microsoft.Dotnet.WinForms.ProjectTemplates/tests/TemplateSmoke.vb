' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Runtime.ExceptionServices
Imports System.Threading.Tasks
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.Hosting
Imports Microsoft.Extensions.Logging
Imports Microsoft.Extensions.WinForms

''' <summary>
'''  Exercises the generated entry point and observes its host without adding application test hooks.
''' </summary>
Friend NotInheritable Class TemplateSmoke
    Implements IObserver(Of DiagnosticListener), IObserver(Of KeyValuePair(Of String, Object)), IDisposable

    Private ReadOnly _subscriptions As New List(Of IDisposable)()
    Private ReadOnly _listeners As IDisposable
    Private _host As IHost

    Private Sub New()
        _listeners = DiagnosticListener.AllListeners.Subscribe(Me)
    End Sub

    <STAThread>
    Public Shared Function Main(args As String()) As Integer
        Try
            Dim scenario As String = args.Single()
            Select Case scenario
                Case "services"
                    Dim directoryPath As String = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "WinForms", Program.SettingsApplicationId)
                    Check(Not Directory.Exists(directoryPath), "The settings test requires a newly generated application identity.")
                    Try
                        CheckUi(scenario)
                    Finally
                        If Directory.Exists(directoryPath) Then
                            Directory.Delete(directoryPath, recursive:=True)
                        End If
                    End Try
                Case "designer"
                    Using form As New Form1()
                        Check(Not form.IsHandleCreated AndAlso form.Text = "Form1", "Parameterless construction must be UI-only.")
                    End Using
                Case "ui", "host-stop"
                    CheckUi(scenario)
                Case Else
                    Throw New ArgumentException("Unknown template smoke scenario.")
            End Select

            Console.WriteLine($"PASS VB: {scenario}")

            Return 0
        Catch exception As Exception
            Console.Error.WriteLine(exception)

            Return 1
        End Try
    End Function

    Private Shared Sub CheckServices(host As IHost)
        Check(host.Services.GetServices(Of ILoggerProvider)().
            OfType(Of Microsoft.Extensions.Logging.Debug.DebugLoggerProvider)().Count() = 1,
            "The WinForms logging helper must register one Debug provider.")
        host.Services.GetRequiredService(Of WinFormsExceptionLogging)()
        Dim settings = host.Services.GetRequiredService(Of IUserSettingsService(Of StartupPreferences))()
        Task.Run(
            Async Function()
                Check((Await settings.LoadAsync()).UseCompactLayout, "Incorrect fresh preferences.")
                Dim commit As UserSettingsCommit = Await settings.SaveAsync(
                    New StartupPreferences With {.UseCompactLayout = False})
                Await commit.NotificationCompletion
                Check(Not (Await settings.LoadAsync()).UseCompactLayout, "Settings round trip failed.")
            End Function).GetAwaiter().GetResult()
    End Sub

    Private Shared Sub CheckUi(scenario As String)
        Using capture As New TemplateSmoke()
            Dim dispatched As Boolean = False
            Dim disposed As Boolean = False
            Dim failure As Exception = Nothing
            Dim idle As EventHandler =
                Sub(sender, arguments)
                    If dispatched OrElse Application.OpenForms.Count = 0 Then
                        Return
                    End If

                    dispatched = True
                    Dim form As Form = Application.OpenForms(0)
                    AddHandler form.Disposed, Sub(disposedSender, disposedArguments) disposed = True
                    Try
                        Check(TypeOf form Is Form1 AndAlso form.Text = "Form1", "The generated startup form was not shown.")
                        Check(Application.HighDpiMode = HighDpiMode.SystemAware AndAlso
                              Application.DefaultFormRevealMode = FormRevealMode.Deferred AndAlso
                              Application.DefaultVisualStylesMode = VisualStylesMode.Net11,
                              "The fluent startup configuration was not applied.")
                        Dim host As IHost = capture._host
                        Check(host IsNot Nothing, "The generated host was not built.")
                        If scenario = "services" Then
                            CheckServices(host)
                        End If

                        If scenario = "host-stop" Then
                            host.Services.GetRequiredService(Of IHostApplicationLifetime)().StopApplication()
                        Else
                            form.Close()
                        End If
                    Catch exception As Exception
                        failure = exception
                        form.Close()
                    End Try
                End Sub
            AddHandler Application.Idle, idle
            Try
                Program.Main()
            Finally
                RemoveHandler Application.Idle, idle
            End Try

            If failure IsNot Nothing Then
                ExceptionDispatchInfo.Throw(failure)
            End If

            Check(dispatched AndAlso disposed AndAlso Application.OpenForms.Count = 0, "UI dispatch/cleanup failed.")
        End Using
    End Sub

    Private Shared Sub Check(condition As Boolean, message As String)
        If Not condition Then
            Throw New InvalidOperationException(message)
        End If
    End Sub

    Public Sub OnNext(listener As DiagnosticListener) Implements IObserver(Of DiagnosticListener).OnNext
        If listener.Name = "Microsoft.Extensions.Hosting" Then
            _subscriptions.Add(listener.Subscribe(Me))
        End If
    End Sub

    Public Sub OnNext(notification As KeyValuePair(Of String, Object)) Implements IObserver(Of KeyValuePair(Of String, Object)).OnNext
        If notification.Key = "HostBuilt" AndAlso TypeOf notification.Value Is IHost Then
            _host = DirectCast(notification.Value, IHost)
        End If
    End Sub

    Public Sub OnCompleted() Implements IObserver(Of DiagnosticListener).OnCompleted,
        IObserver(Of KeyValuePair(Of String, Object)).OnCompleted
    End Sub

    Public Sub OnError(exception As Exception) Implements IObserver(Of DiagnosticListener).OnError,
        IObserver(Of KeyValuePair(Of String, Object)).OnError
        ExceptionDispatchInfo.Throw(exception)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        _listeners.Dispose()
        For Each subscription As IDisposable In _subscriptions
            subscription.Dispose()
        Next
    End Sub
End Class
