' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.Hosting
Imports Microsoft.Extensions.Logging.Abstractions
Imports Microsoft.Extensions.WinForms
Imports Check = ApplicationBuilderParity.Probe

Namespace ApplicationBuilderParity.VisualBasic
    ''' <summary>
    '''  Composes shared features directly in VB; C# supplies only test probes.
    ''' </summary>
    Friend Module Program
        <STAThread>
        Friend Function Main(args As String()) As Integer
            Try
                Dim probe As New Probe()
                Dim scenario As String = args(0)
                Dim tokens As String() = args.Skip(1).ToArray()
                If scenario.StartsWith("legacy-", StringComparison.Ordinal) Then
                    Dim app As New LegacyApplication(probe, scenario)
                    app.Run(tokens)
                    probe.Ui("returned")
                ElseIf scenario.StartsWith("ipc-", StringComparison.Ordinal) Then
                    RunIpc(probe, scenario, tokens)
                Else
                    RunShared(probe, scenario, tokens)
                End If

                probe.Finish()
                Return 0
            Catch exception As Exception
                Console.Error.WriteLine(exception)
                Return 1
            End Try
        End Function

        Private Sub RunShared(probe As Probe, scenario As String, tokens As String())
            Dim network As New NetworkSource()
            Dim clock As New GateClock()
            Dim fault As New FaultService(probe)
            Dim splash As WinFormsSplashScreen = Nothing
            Dim main As Form = Nothing
            Dim app As WinFormsApplication = Nothing
            Dim builder = WinFormsApplication.CreateBuilder().
                UseHost(New HostBuilder().ConfigureServices(
                    Sub(services)
                        services.AddSingleton(Of IHostedService)(Function(provider) New DrainService(probe))
                        If scenario = "background-failure" Then
                            services.AddSingleton(Of IHostedService)(Function(provider) fault)
                        End If
                    End Sub)).
                UseApplicationContext(
                    Function()
                        probe.Ui("activate")
                        main = New Form With {.ShowInTaskbar = False}
                        AddHandler main.Shown,
                            Sub(sender, e)
                                probe.Guard(
                                    Sub()
                                        probe.Ui("shown")
                                        If scenario = "splash-gate" Then
                                            Check.Require(clock.GetTimestamp() = TimeSpan.TicksPerSecond * 2, "Early main window.")
                                            Check.Require(splash.IsDisposed, "Splash still owned.")
                                        End If

                                        If scenario = "network" Then
                                            Dim notification As Task = Task.Run(AddressOf network.Raise)
                                        ElseIf scenario = "ui-exception" Then
                                            ' Managed routing stimulus, not an exception escaping a native callback.
                                            probe.Post(Sub() Application.OnThreadException(probe.Failure))
                                        ElseIf scenario = "background-failure" Then
                                            fault.Release.TrySetResult()
                                        Else
                                            main.Close()
                                        End If
                                    End Sub)
                            End Sub
                        Return New ApplicationContext(main)
                    End Function).
                ConfigureApplicationFeatures(
                    Sub(features)
                        features.CommandLine = tokens
                        features.Starting =
                            Sub(sender, e)
                                probe.Ui("starting")
                                Check.Require(Not Application.MessageLoop, "Startup entered the root pump.")
                                probe.Tokens(e.CommandLine)
                                e.Cancel = scenario = "cancel"
                                If scenario = "startup-failure" Then
                                    Throw probe.Failure
                                End If
                            End Sub
                        features.Shutdown =
                            Sub(sender, e)
                                Check.Require(TypeOf SynchronizationContext.Current Is WindowsFormsSynchronizationContext,
                                              "Shared shutdown must retain owner context.")
                                probe.Ui("shutdown")
                            End Sub
                        features.ReportFailure =
                            Sub(exception, phase)
                                Check.Require(ReferenceEquals(exception, probe.Failure), "Lost original failure.")
                                probe.Ui($"report:{phase}")
                            End Sub
                        features.PrepareSettingsSave =
                            Function(owner)
                                probe.Ui("prepare")
                                Return Async Function()
                                           Check.Require(main.IsDisposed, "Save preceded UI cleanup.")
                                           Await Task.Yield()
                                           probe.Add("save")
                                           If scenario = "settings-failure" Then
                                               Throw probe.Failure
                                           End If

                                           probe.Add("saved")
                                       End Function
                            End Function
                        If scenario = "network" Then
                            features.NetworkSource = network
                            features.NetworkAvailabilityChanged =
                                Sub(available)
                                    Check.Require(available, "Wrong network transition.")
                                    probe.Ui("network")
                                    Dim stopping As Task = app.StopAsync()
                                End Sub
                        End If

                        If scenario.StartsWith("splash-", StringComparison.Ordinal) Then
                            features.SplashScreenFactory =
                                Function()
                                    splash = New WinFormsSplashScreen()
                                    Return splash
                                End Function
                            features.SplashTimeProvider = clock
                            features.MinimumSplashDisplayTime = TimeSpan.FromSeconds(2)
                        End If
                    End Sub)
            Using owned As WinFormsApplication = builder.Build(),
                logging As WinFormsExceptionLogging = WinFormsExceptionLogging.Attach(
                    owned, NullLogger.Instance,
                    Sub(sender, e)
                        Check.Require(ReferenceEquals(e.Exception, probe.Failure), "Lost UI exception.")
                        probe.Ui("ui-handled")
                        main.Close()
                    End Sub,
                    Sub(failure) probe.Add("logging-failure"))
                app = owned
                tokens(0) = "mutated after Build"
                AddHandler app.Lifetime.ApplicationStarted,
                    Sub(sender, e)
                        probe.Ui("started")
                        If scenario.StartsWith("splash-", StringComparison.Ordinal) Then
                            probe.Post(
                                Sub()
                                    Check.Require(splash.Visible AndAlso Not main.Visible, "Missing splash gate.")
                                    probe.Ui("gated")
                                    If scenario = "splash-stop" Then
                                        Dim stopping As Task = app.StopAsync()
                                        Return
                                    End If

                                    clock.Advance()
                                    probe.Post(
                                        Sub()
                                            Check.Require(Not main.Visible, "Early timer bypassed minimum.")
                                            probe.Ui("still-gated")
                                            clock.Advance()
                                        End Sub)
                                End Sub)
                        End If
                    End Sub
                AddHandler app.Lifetime.ApplicationStopped, Sub(sender, e) probe.Ui("stopped")
                Try
                    app.Run()
                    Check.Require(scenario <> "startup-failure" AndAlso scenario <> "settings-failure", "Failure was swallowed.")
                Catch exception As Exception When ReferenceEquals(exception, probe.Failure)
                    probe.Ui("caught-original")
                End Try
            End Using

            Check.Require(network.Subscriptions = 0, "Network subscription leaked.")
            If splash IsNot Nothing Then
                Check.Require(splash.IsDisposed AndAlso clock.TimerDisposed, "Splash/timer leaked.")
                probe.Ui("splash-clean")
            End If

            probe.Ui("returned")
        End Sub

        Private Sub RunIpc(probe As Probe, scenario As String, tokens As String())
            Dim validation As Boolean = scenario.StartsWith("ipc-validate", StringComparison.Ordinal)
            If validation Then
                ' Parent-owned redirected stdin is the barrier; no global event or polling delay.
                Console.WriteLine("ARMED")
                Check.Require(Console.ReadLine() = "go", "Missing election release.")
            End If

            Dim identity = WinFormsSingleInstanceIdentity.Create(
                tokens(0), If(scenario = "ipc-validate-version", "14980-other", "14978"))
            Dim selection = WinFormsSingleInstance.SelectAsync(
                identity, tokens.Skip(1), False, TimeSpan.FromSeconds(10)).GetAwaiter().GetResult()
            probe.Add(selection.Result.ToString())
            If selection.Primary Is Nothing Then
                Return
            End If

            Using primary As WinFormsSingleInstance = selection.Primary
                If scenario = "ipc-secondary" OrElse scenario = "ipc-notready" Then
                    If scenario = "ipc-notready" Then
                        Console.WriteLine("READY")
                        Console.ReadLine()
                    End If

                    Return
                End If

                Dim app As WinFormsApplication = Nothing
                Dim builder = WinFormsApplication.CreateBuilder()
                If validation Then
                    builder.UseHost(New HostBuilder().ConfigureServices(
                        Sub(services)
                            services.AddSingleton(Of IHostedService)(Function(provider) New DrainService(probe))
                        End Sub)).
                        UseApplicationContext(
                            Function()
                                probe.Ui("activate")
                                Dim form As New Form With {.ShowInTaskbar = False}
                                AddHandler form.Disposed, Sub(sender, e) probe.Ui("form-dispose")
                                Return New ApplicationContext(form)
                            End Function)
                Else
                    builder.UseStartupForm(Of Form)()
                End If

                Using owned = builder.
                    UseSingleInstance(
                        primary,
                        Sub(sender, request)
                            probe.Ui("next")
                            probe.Tokens(request.CommandLine)
                            probe.Requests.Add(request.CommandLine.ToArray())
                            request.BringToForeground = False
                            If scenario = "ipc-failure" Then
                                Throw probe.Failure
                            End If
                        End Sub).Build()
                    app = owned
                    AddHandler app.Lifetime.ApplicationStarted,
                        Sub(sender, e)
                            probe.Post(
                                Sub()
                                    If validation Then
                                        probe.Ui("started")
                                    End If

                                    Console.WriteLine("READY")
                                    Dim listener As Task = Task.Run(
                                        Async Function()
                                            Await Console.In.ReadLineAsync()
                                            Await app.StopAsync()
                                        End Function)
                                End Sub)
                        End Sub
                    app.Run()
                End Using
            End Using
        End Sub
    End Module
End Namespace
