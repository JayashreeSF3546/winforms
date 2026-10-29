' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.Hosting
Imports Microsoft.Extensions.WinForms

''' <summary>
''' Selects a caller-owned STA application and its shutdown policy.
''' </summary>
Friend Module Program
    <STAThread>
    Public Sub Main(args As String())
        Dim scenario As String = If(args.Length = 0, "form", args(0))
        Select Case scenario
            Case "form", "context", "background", "cancellation", "shutdown"
            Case Else
                Throw New ArgumentException("Expected form, context, background, cancellation, or shutdown.")
        End Select

        Dim dark As Boolean = False
        Dim componentManager As Boolean = False
        Dim smoke As Boolean = False
        For Each argument As String In args.Skip(1)
            Select Case argument
                Case "--dark"
                    dark = True
                Case "--component-manager"
                    componentManager = True
                Case "--smoke"
                    smoke = True
                Case Else
                    Throw New ArgumentException($"Unknown option: {argument}")
            End Select
        Next

        AppContext.SetSwitch("Switch.System.Windows.Forms.EnableMsoComponentManager", componentManager)
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)
        Application.SetColorMode(If(dark, SystemColorMode.Dark, SystemColorMode.Classic))
        Debug.WriteLine($"Session: {If(SystemInformation.TerminalServerSession, "Terminal Services", "local")}")
        Debug.WriteLine($"Architecture: {RuntimeInformation.ProcessArchitecture}")
        Debug.WriteLine($"Color: {If(dark, "dark", "light")}")
        Debug.WriteLine($"Backend: {If(componentManager, "component-manager", "light")}")

        Using cancellation As New CancellationTokenSource()
            Dim uiReady As New TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
            Dim runner As WinFormsApplication = Nothing
            Dim context As SwitchingContext = Nothing
            Dim shutdown As Task = Nothing
            Dim builder = WinFormsApplication.CreateBuilder()
            Select Case scenario
                Case "form"
                    builder.UseStartupForm(Of MainForm)()
                Case "context"
                    builder.UseApplicationContext(
                        Function()
                            context = New SwitchingContext()
                            Return context
                        End Function)
                Case "background"
                    builder.UseHost(
                        New HostBuilder().ConfigureServices(
                            Sub(hostContext, services)
                                services.AddSingleton(Of IHostedService)(
                                    Function(provider) New SampleWorker(
                                        provider.GetRequiredService(Of IHostApplicationLifetime)(), uiReady.Task, smoke))
                            End Sub))
                    builder.UseApplicationContext(
                        Function() New ApplicationContext(New MainForm(
                            "Background work stops the host after five updates. Close early to cancel it.",
                            "Close window",
                            requestShutdown:=Nothing)))
                Case "cancellation"
                    builder.UseApplicationContext(
                        Function() New ApplicationContext(New MainForm(
                            "Cancel Run; cleanup finishes before cancellation is reported.",
                            "Cancel application",
                            AddressOf cancellation.Cancel)))
                Case "shutdown"
                    builder.UseHost(New HostBuilder())
                    builder.UseApplicationContext(
                        Function() New ApplicationContext(New MainForm(
                            "Host shutdown cannot be vetoed. Do not block or await the terminating UI dispatcher.",
                            "Stop host",
                            Sub() shutdown = runner.Host.StopAsync(CancellationToken.None),
                            vetoClose:=True)))
            End Select

            Using ownedApplication As WinFormsApplication = builder.Build()
                runner = ownedApplication
                Dim hostLifetime As IHostApplicationLifetime =
                    runner.Host?.Services.GetRequiredService(Of IHostApplicationLifetime)()
                Dim registration As CancellationTokenRegistration
                If hostLifetime IsNot Nothing Then
                    registration = hostLifetime.ApplicationStopped.Register(Sub() Debug.WriteLine("Host stopped"))
                End If

                Using registration
                    AddHandler runner.Lifetime.ApplicationStarted,
                        Sub(sender, e)
                            Debug.WriteLine("Started")
                            SynchronizationContext.Current.Post(
                                Sub(state)
                                    Debug.WriteLine("UI dispatched")
                                    uiReady.SetResult()
                                    If Not smoke OrElse scenario = "background" Then
                                        Return
                                    End If

                                    Select Case scenario
                                        Case "context"
                                            context.ShowNext()
                                            context.MainForm.BeginInvoke(New Action(AddressOf context.MainForm.Close))
                                        Case "cancellation"
                                            cancellation.Cancel()
                                        Case "shutdown"
                                            shutdown = runner.Host.StopAsync(CancellationToken.None)
                                        Case Else
                                            Application.ExitThread()
                                    End Select
                                End Sub,
                                Nothing)
                        End Sub
                    AddHandler runner.Lifetime.ApplicationStopping, Sub(sender, e) Debug.WriteLine("Stopping")
                    AddHandler runner.Lifetime.ApplicationStopped, Sub(sender, e) Debug.WriteLine("Stopped")
                    Try
                        runner.Run(cancellation.Token)
                    Catch exception As OperationCanceledException When _
                        cancellation.IsCancellationRequested AndAlso exception.CancellationToken = cancellation.Token
                        Debug.WriteLine("Canceled after UI cleanup")
                    End Try

                    ' The UI dispatcher has terminated. Observe the stop task only after Run returns.
                    shutdown?.GetAwaiter().GetResult()
                End Using
            End Using
        End Using
    End Sub
End Module
