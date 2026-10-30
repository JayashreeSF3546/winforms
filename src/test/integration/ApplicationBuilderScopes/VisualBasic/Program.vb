' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports System.Runtime.ExceptionServices
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Windows.Forms
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.Hosting
Imports Microsoft.Extensions.WinForms

Namespace ApplicationBuilderScopes.VisualBasic
    ''' <summary>
    ''' Runs the repository-only scoped activation prototype with observable service disposal.
    ''' </summary>
    Friend Module Program
        <STAThread>
        Public Sub Main(args As String())
            Dim smoke As Boolean = False
            Dim cancel As Boolean = False
            Dim componentManager As Boolean = False
            Dim theme As String = "classic"
            Dim color As SystemColorMode = SystemColorMode.Classic
            For Each argument As String In args
                Select Case argument
                    Case "--smoke" : smoke = True
                    Case "--cancel" : cancel = True
                    Case "--component-manager" : componentManager = True
                    Case "--classic" : color = SystemColorMode.Classic : theme = "classic"
                    Case "--light" : color = SystemColorMode.Classic : theme = "light"
                    Case "--dark" : color = SystemColorMode.Dark : theme = "dark"
                    Case Else : Throw New ArgumentException($"Unknown option: {argument}")
                End Select
            Next

            If cancel AndAlso Not smoke Then
                Throw New ArgumentException("--cancel requires --smoke; interactive windows have a Cancel application button.")
            End If

            AppContext.SetSwitch("Switch.System.Windows.Forms.EnableMsoComponentManager", componentManager)
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)
            If theme <> "classic" Then
                Application.EnableVisualStyles()
            End If

            Application.SetCompatibleTextRenderingDefault(False)
            Application.SetColorMode(color)
            Console.WriteLine($"Environment: {RuntimeInformation.ProcessArchitecture}; terminal={SystemInformation.TerminalServerSession}")
            Console.WriteLine($"Color: {theme}; component-manager={componentManager}")
            Dim sessions As New List(Of ScopeSession)
            Using cancellation As New CancellationTokenSource
                Dim context As SampleContext = Nothing
                Dim hostBuilder As New HostBuilder
                hostBuilder.ConfigureServices(
                    Sub(hostContext, services)
                        services.AddScoped(
                            Function(provider)
                                Dim session As New ScopeSession
                                sessions.Add(session)
                                Return session
                            End Function)
                    End Sub)
                Using application As WinFormsApplication = WinFormsApplication.CreateBuilder().
                    UseHost(hostBuilder).
                    UseScopedApplicationContext(
                        Function(services, scopes)
                            context = New SampleContext(scopes, services.GetRequiredService(Of ScopeSession)(), cancellation, smoke, cancel)
                            Return context
                        End Function).
                    Build()
                    Try
                        application.Run(cancellation.Token)
                    Catch exception As OperationCanceledException When cancellation.IsCancellationRequested AndAlso exception.CancellationToken = cancellation.Token
                        Console.WriteLine("Run canceled after UI cleanup")
                    End Try

                    If context IsNot Nothing AndAlso context.Failure IsNot Nothing Then
                        ExceptionDispatchInfo.Throw(context.Failure)
                    End If

                    If sessions.Count = 0 OrElse sessions.Any(Function(session) session.DisposeCount <> 1) Then
                        Throw New InvalidOperationException("Every created session must be disposed exactly once.")
                    End If

                    Console.WriteLine($"PASS: {sessions.Count} sessions disposed exactly once")
                End Using
            End Using
        End Sub
    End Module

    ''' <summary>
    ''' Keeps navigation and explicit lifetime ownership at the composition boundary, not on controls.
    ''' </summary>
    Friend NotInheritable Class SampleContext
        Inherits ApplicationContext

        Private ReadOnly _scopes As WinFormsUiScope
        Private ReadOnly _applicationSession As ScopeSession
        Private ReadOnly _cancellation As CancellationTokenSource
        Private ReadOnly _cancelSmoke As Boolean
        Private _current As WinFormsFormScope
        Private _page As Integer = 1

        Friend Sub New(scopes As WinFormsUiScope, applicationSession As ScopeSession,
                       cancellation As CancellationTokenSource, smoke As Boolean, cancelSmoke As Boolean)
            _scopes = scopes
            _applicationSession = applicationSession
            _cancellation = cancellation
            _cancelSmoke = cancelSmoke
            _current = CreateWindow("Page 1", Nothing)
            MainForm = _current.Form
            If smoke Then
                AddHandler MainForm.Shown, Sub(sender, e) MainForm.BeginInvoke(New Action(AddressOf RunSmoke))
            End If
        End Sub

        Friend Property Failure As Exception

        Private Function CreateWindow(title As String, owner As WinFormsFormScope) As WinFormsFormScope
            Dim lifetime As WinFormsFormScope = _scopes.CreateForm(
                Function(services) New ScopeForm(services.GetRequiredService(Of ScopeSession)(), title), owner)
            Dim form = DirectCast(lifetime.Form, ScopeForm)
            lifetime.Assign(form.SessionView, Sub(target, services) target.InitializeServices(services.GetRequiredService(Of ScopeSession)()))
            lifetime.Assign(form.SessionWorker, Sub(target, services) target.InitializeServices(services.GetRequiredService(Of ScopeSession)()))
            form.VerifyAssignment()
            If ReferenceEquals(form.Session, _applicationSession) Then
                Throw New InvalidOperationException("A form must not borrow the application scope.")
            End If

            Dim nextPage As Action = Nothing
            If owner Is Nothing Then
                nextPage = AddressOf Navigate
            End If

            form.ConfigureNavigation(
                Sub() CreateWindow("Modeless", lifetime).Show(),
                Sub() ShowModal(lifetime, automatic:=False),
                nextPage,
                Sub() _cancellation.Cancel())
            Return lifetime
        End Function

        Private Sub ShowModal(owner As WinFormsFormScope, automatic As Boolean)
            Using dialog As WinFormsFormScope = CreateWindow("Modal", owner)
                If automatic Then
                    AddHandler dialog.Form.Shown,
                        Sub(sender, e) dialog.Form.BeginInvoke(
                            New Action(Sub() dialog.Form.DialogResult = DialogResult.OK))
                End If

                Dim result As DialogResult = dialog.ShowDialog()
                Console.WriteLine($"Modal result: {result}")
                DirectCast(owner.Form, ScopeForm).Session.VerifyAlive()
                _applicationSession.VerifyAlive()
            End Using
        End Sub

        Private Sub Navigate()
            Dim previous As WinFormsFormScope = _current
            Dim nextWindow As WinFormsFormScope = CreateWindow($"Page {_page + 1}", Nothing)
            _page += 1
            _current = nextWindow
            MainForm = nextWindow.Form
            nextWindow.Show()
            previous.Form.Close()
            Console.WriteLine("Navigation completed")
        End Sub

        Private Sub RunSmoke()
            Try
                Using modeless As WinFormsFormScope = CreateWindow("Modeless", _current)
                    modeless.Show()
                    Dim child = DirectCast(modeless.Form, ScopeForm)
                    If ReferenceEquals(child.Session, DirectCast(_current.Form, ScopeForm).Session) Then
                        Throw New InvalidOperationException("Independent forms must have distinct scoped services.")
                    End If

                    modeless.Form.Close()
                    If child.Session.DisposeCount <> 1 Then
                        Throw New InvalidOperationException("Closing a modeless window must release its scope.")
                    End If

                    Console.WriteLine("Modeless scope completed")
                    ShowModal(_current, automatic:=True)
                    Navigate()
                    _current.Form.BeginInvoke(
                        New Action(
                            Sub()
                                If _cancelSmoke Then
                                    _cancellation.Cancel()
                                Else
                                    _current.Form.Close()
                                End If
                            End Sub))
                End Using
            Catch exception As Exception
                Failure = exception
                ExitThread()
            End Try
        End Sub
    End Class

    ''' <summary>
    ''' Supplies a scoped identity and deterministic disposal diagnostics without external resources.
    ''' </summary>
    Friend NotInheritable Class ScopeSession
        Implements IDisposable

        Private _disposeCount As Integer
        Friend ReadOnly Property Id As Guid = Guid.NewGuid()
        Friend ReadOnly Property DisposeCount As Integer
            Get
                Return _disposeCount
            End Get
        End Property

        Friend Sub New()
            Console.WriteLine($"Session created: {Id}")
        End Sub

        Friend Sub VerifyAlive()
            ObjectDisposedException.ThrowIf(_disposeCount <> 0, Me)
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            _disposeCount += 1
            Console.WriteLine($"Session disposed: {Id}")
        End Sub
    End Class
End Namespace
