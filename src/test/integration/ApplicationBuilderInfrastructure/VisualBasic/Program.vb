' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Imports System
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
'''  Starts the VB front end without the Visual Basic application framework or a second message loop.
''' </summary>
Public Module Program
    <STAThread>
    Public Function Main(args As String()) As Integer
        Return SampleApplication.Run(args, Function(session) New InfrastructureForm(session))
    End Function
End Module

''' <summary>
'''  Presents native VB UI with explicitly composed services and tracked shutdown.
''' </summary>
Public NotInheritable Class InfrastructureForm
    Inherits Form

    Private ReadOnly _session As SampleSession
    Private ReadOnly _value As New NumericUpDown With {.Minimum = 0, .Maximum = Integer.MaxValue, .Value = 7, .Width = 120}
    Private ReadOnly _status As New Label With {.AutoSize = True, .MaximumSize = New System.Drawing.Size(660, 0)}
    Private ReadOnly _commands As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = True, .Dock = DockStyle.Top}
    Private _draining As Boolean
    Private _allowClose As Boolean

    Public Sub New(session As SampleSession)
        _session = session
        Text = "VB infrastructure sample"
        Width = 740
        Height = 300
        _status.Text = session.Describe()
        Dim content As New FlowLayoutPanel With {
            .Dock = DockStyle.Fill, .FlowDirection = FlowDirection.TopDown, .Padding = New Padding(12)}
        content.Controls.Add(_status)
        content.Controls.Add(_value)
        content.Controls.Add(_commands)
        Controls.Add(content)
        _commands.Width = 680
        For Each command As String In {"Load", "Save", "Reset", "Upgrade", "Recover", "Health", "Reload", "Wait"}
            Dim button As New Button With {.Text = command, .AutoSize = True}
            AddHandler button.Click, Async Sub(sender, arguments) Await RunCommandAsync(command)
            _commands.Controls.Add(button)
        Next

        Dim exceptionButton As New Button With {.Text = "Controlled exception", .AutoSize = True}
        AddHandler exceptionButton.Click, Sub(sender, arguments)
                                              Throw New SampleUiException()
                                          End Sub
        _commands.Controls.Add(exceptionButton)
        Dim stopButton As New Button With {.Text = "Host stop", .AutoSize = True}
        AddHandler stopButton.Click, Sub(sender, arguments) _session.RequestHostStop()
        content.Controls.Add(stopButton)
        Dim failureButton As New Button With {.Text = "Worker failure", .AutoSize = True}
        AddHandler failureButton.Click, Sub(sender, arguments) _session.RequestWorkerFailure()
        content.Controls.Add(failureButton)
        Dim closeButton As New Button With {.Text = "Close / cancel pending", .AutoSize = True}
        AddHandler closeButton.Click, Sub(sender, arguments) Close()
        content.Controls.Add(closeButton)
    End Sub

    Protected Overrides Async Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        Try
            If _session.Scenario.Length = 0 Then
                Await RunCommandAsync("Load")
            Else
                Await SampleApplication.RunScenarioAsync(_session, AddressOf Close, AddressOf PostControlledException)
            End If
        Catch exception As Exception When Not TypeOf exception Is OutOfMemoryException AndAlso
            Not TypeOf exception Is StackOverflowException AndAlso Not TypeOf exception Is AccessViolationException
            _session.AutomationFailure = exception
            Console.Error.WriteLine($"SCENARIO FAILED: {exception.GetType().Name}")
            _session.RequestHostStop()
        End Try
    End Sub

    Private Sub PostControlledException()
        BeginInvoke(New Action(Sub()
                                   Throw New SampleUiException()
                               End Sub))
    End Sub

    Private Async Function RunCommandAsync(command As String) As Task
        _commands.Enabled = False
        Try
            Dim result As SampleResult = Await _session.ExecuteAsync(command, CInt(_value.Value))
            If Not IsDisposed AndAlso Not Disposing AndAlso Not _draining Then
                _status.Text = result.Text
                If result.Value.HasValue Then
                    _value.Value = result.Value.Value
                End If
            End If
        Finally
            If Not IsDisposed AndAlso Not Disposing AndAlso Not _draining Then
                _commands.Enabled = True
            End If
        End Try
    End Function

    Protected Overrides Async Sub OnFormClosing(e As FormClosingEventArgs)
        MyBase.OnFormClosing(e)
        If e.Cancel OrElse _allowClose Then
            Return
        End If

        e.Cancel = True
        If _draining Then
            Return
        End If

        _draining = True
        _commands.Enabled = False
        _session.StopAccepting()
        Try
            Await _session.DrainAsync()
        Catch exception As Exception When Not TypeOf exception Is OutOfMemoryException AndAlso
            Not TypeOf exception Is StackOverflowException AndAlso Not TypeOf exception Is AccessViolationException
            _session.AutomationFailure = exception
            Console.Error.WriteLine($"DRAIN FAILED: {exception.GetType().Name}")
        End Try

        If Not IsDisposed AndAlso Not Disposing Then
            _allowClose = True
            BeginInvoke(New Action(AddressOf Close))
        End If
    End Sub
End Class
