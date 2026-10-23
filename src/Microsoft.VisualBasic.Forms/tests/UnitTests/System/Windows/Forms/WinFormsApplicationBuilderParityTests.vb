' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Imports System.Collections.Generic
Imports System.Linq
Imports System.Runtime.ExceptionServices
Imports System.Threading
Imports System.Windows.Forms
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.WinForms
Imports Xunit

Namespace Microsoft.VisualBasic.Forms.Tests

    ''' <summary>
    '''  Verifies shared Application Builder behavior when consumed from Visual Basic.
    ''' </summary>
    Public Class WinFormsApplicationBuilderParityTests

        <WinFormsFact>
        Public Sub Run_StartupFormClose_RaisesLifetimeInOrder()
            RunOnStaThread(
                Sub()
                    Dim events As New List(Of String)()
                    Using startupForm As New Form()
                        AddHandler startupForm.Shown, Sub(sender, eventArgs) startupForm.Close()

                        Dim application As WinFormsApplication =
                            WinFormsApplication.CreateBuilder().
                                UseStartupForm(startupForm).
                                Build()
                        Using application
                            AddHandler application.Lifetime.ApplicationStarting,
                                Sub(sender, eventArgs) events.Add("Starting")
                            AddHandler application.Lifetime.ApplicationStarted,
                                Sub(sender, eventArgs) events.Add("Started")
                            AddHandler application.Lifetime.ApplicationStopping,
                                Sub(sender, eventArgs) events.Add("Stopping")
                            AddHandler application.Lifetime.ApplicationStopped,
                                Sub(sender, eventArgs) events.Add("Stopped")

                            application.Run()
                        End Using
                    End Using

                    Assert.Equal(
                        New String() {"Starting", "Started", "Stopping", "Stopped"},
                        events)
                End Sub)
        End Sub

        <WinFormsFact>
        Public Sub Run_StartingCancellationSkipsStartupAndCleansUp()
            RunOnStaThread(
                Sub()
                    Dim events As New List(Of String)()
                    Dim startupForm As New Form()
                    Dim application As WinFormsApplication =
                        WinFormsApplication.CreateBuilder().
                            UseStartupForm(startupForm).
                            Build()
                    Using application
                        AddHandler application.Lifetime.ApplicationStarting,
                            Sub(sender, eventArgs)
                                events.Add("Starting")
                                eventArgs.Cancel = True
                            End Sub
                        AddHandler application.Lifetime.ApplicationStarted,
                            Sub(sender, eventArgs) events.Add("Started")
                        AddHandler application.Lifetime.ApplicationStopping,
                            Sub(sender, eventArgs) events.Add("Stopping")
                        AddHandler application.Lifetime.ApplicationStopped,
                            Sub(sender, eventArgs) events.Add("Stopped")

                        application.Run()
                    End Using

                    Assert.Equal(New String() {"Starting", "Stopping", "Stopped"}, events)
                    Assert.True(startupForm.IsDisposed)
                End Sub)
        End Sub

        <WinFormsFact>
        Public Sub Run_StartingHandlerFailureCleansUpAndPropagates()
            RunOnStaThread(
                Sub()
                    Dim events As New List(Of String)()
                    Dim expectedException As New InvalidOperationException("Starting handler failed.")
                    Dim startupForm As New Form()
                    Dim application As WinFormsApplication =
                        WinFormsApplication.CreateBuilder().
                            UseStartupForm(startupForm).
                            Build()
                    Using application
                        AddHandler application.Lifetime.ApplicationStarting,
                            Sub(sender, eventArgs)
                                events.Add("Starting")
                                Throw expectedException
                            End Sub
                        AddHandler application.Lifetime.ApplicationStopping,
                            Sub(sender, eventArgs) events.Add("Stopping")
                        AddHandler application.Lifetime.ApplicationStopped,
                            Sub(sender, eventArgs) events.Add("Stopped")

                        Dim exception As InvalidOperationException =
                            Assert.Throws(Of InvalidOperationException)(
                                Sub() application.Run())

                        Assert.Same(expectedException, exception)
                    End Using

                    Assert.True(startupForm.IsDisposed)
                    Assert.Equal(New String() {"Starting", "Stopping", "Stopped"}, events)
                End Sub)
        End Sub

        <Fact>
        Public Sub CreateBuilder_SnapshotsCommandLineArguments()
            Dim args As String() = {"--BuilderParity:Activation=first"}
            Dim builder As WinFormsApplicationBuilder = WinFormsApplication.CreateBuilder(args)
            args(0) = "--BuilderParity:Activation=changed"

            Dim application As WinFormsApplication =
                builder.UseApplicationContext().Build()
            Using application
                Assert.Equal(
                    New String() {"--BuilderParity:Activation=first"},
                    application.CommandLineArgs)
            End Using
        End Sub

        <WinFormsFact>
        Public Sub Run_WithSplashScreen_CoordinatesStartupAndDisposesSplash()
            RunOnStaThread(
                Sub()
                    Dim events As New List(Of String)()
                    Using splashForm As New Form()
                        Using startupForm As New Form()
                            AddHandler startupForm.Shown,
                                Sub(sender, eventArgs)
                                    Assert.True(splashForm.IsDisposed)
                                    events.Add("MainFormShown")
                                    startupForm.Close()
                                End Sub

                            Dim application As WinFormsApplication =
                                WinFormsApplication.CreateBuilder().
                                    UseStartupForm(startupForm).
                                    UseSplashScreen(Function() splashForm, TimeSpan.Zero).
                                    Build()
                            Using application
                                AddHandler application.Lifetime.ApplicationStarted,
                                    Sub(sender, eventArgs)
                                        Assert.True(splashForm.IsDisposed)
                                        events.Add("ApplicationStarted")
                                    End Sub

                                application.Run()
                            End Using

                            Assert.Equal(
                                New String() {"ApplicationStarted", "MainFormShown"},
                                events)
                            Assert.True(splashForm.IsDisposed)
                        End Using
                    End Using
                End Sub)
        End Sub

        <WinFormsFact>
        Public Sub AddApplicationServices_AndUserSettings_AreAvailableToStartupForm()
            RunOnStaThread(
                Sub()
                    Dim applicationInfo As IWinFormsApplicationInfo = Nothing
                    Dim runtimeInfo As IWinFormsRuntimeInfo = Nothing
                    Dim settingsService As IUserSettingsService = Nothing
                    Dim builder As WinFormsApplicationBuilder =
                        WinFormsApplication.CreateBuilder().
                            AddApplicationServices().
                            AddUserSettings()
                    Dim startupFormFactory As Func(Of IServiceProvider, Form) =
                        Function(services As IServiceProvider)
                            applicationInfo = services.GetRequiredService(Of IWinFormsApplicationInfo)()
                            runtimeInfo = services.GetRequiredService(Of IWinFormsRuntimeInfo)()
                            settingsService = services.GetRequiredService(Of IUserSettingsService)()

                            Dim startupForm As New Form()
                            AddHandler startupForm.Shown, Sub(sender, eventArgs) startupForm.Close()

                            Return startupForm
                        End Function
                    Dim application As WinFormsApplication =
                        builder.UseStartupForm(startupFormFactory).Build()
                    Using application
                        application.Run()
                    End Using

                    Assert.NotNull(applicationInfo)
                    Assert.Equal(AppContext.BaseDirectory, applicationInfo.BaseDirectory)
                    Assert.Contains(
                        GetType(WinFormsApplicationBuilderParityTests).Assembly,
                        applicationInfo.LoadedAssemblies)
                    Assert.NotNull(runtimeInfo)
                    Assert.Equal(Environment.ProcessId, runtimeInfo.ProcessId)
                    Assert.NotNull(settingsService)
                    Assert.Equal(1, settingsService.SchemaVersion)
                End Sub)
        End Sub

        <WinFormsFact>
        Public Sub Run_SingleInstance_ForwardsActivationArguments()
            RunOnStaThread(
                Sub()
                    Dim applicationId As String = $"BuilderParity-{Guid.NewGuid():N}"
                    Dim forwardedArguments As String() = {"open", "document.txt"}
                    Using applicationContext As New ApplicationContext()
                        Using timeoutTimer As New System.Windows.Forms.Timer() With {
                            .Interval = 10000
                        }
                            Dim application As WinFormsApplication =
                                WinFormsApplication.CreateBuilder().
                                    UseApplicationContext(applicationContext).
                                    UseSingleInstance(applicationId).
                                    Build()
                            Using application
                                Dim receivedArguments As String() = Nothing
                                Dim activationThreadId As Integer = 0

                                AddHandler timeoutTimer.Tick,
                                    Sub(sender, eventArgs)
                                        timeoutTimer.Stop()
                                        applicationContext.ExitThread()
                                    End Sub
                                AddHandler application.Lifetime.ApplicationStarting,
                                    Sub(sender, eventArgs)
                                        RunOnStaThread(
                                            Sub()
                                                Dim secondary As WinFormsApplication =
                                                    WinFormsApplication.CreateBuilder(forwardedArguments).
                                                        UseApplicationContext().
                                                        UseSingleInstance(applicationId).
                                                        Build()
                                                Using secondary
                                                    secondary.Run()
                                                End Using
                                            End Sub)

                                        timeoutTimer.Start()
                                    End Sub
                                AddHandler application.ActivationRequested,
                                    Sub(sender, eventArgs)
                                        receivedArguments = eventArgs.Arguments.ToArray()
                                        activationThreadId = Environment.CurrentManagedThreadId
                                        eventArgs.BringToForeground = False
                                        timeoutTimer.Stop()
                                        applicationContext.ExitThread()
                                    End Sub

                                application.Run()

                                Assert.Equal(forwardedArguments, receivedArguments)
                                Assert.Equal(Environment.CurrentManagedThreadId, activationThreadId)
                                Assert.Null(applicationContext.MainForm)
                            End Using
                        End Using
                    End Using
                End Sub)
        End Sub

        Private Shared Sub RunOnStaThread(action As Action)
            Dim failure As Exception = Nothing
            Dim thread As New Thread(
                Sub()
                    Try
                        action()
                    Catch exception As Exception
                        failure = exception
                    End Try
                End Sub)
            thread.SetApartmentState(ApartmentState.STA)
            thread.Start()
            thread.Join()

            If failure IsNot Nothing Then
                ExceptionDispatchInfo.Capture(failure).Throw()
            End If
        End Sub

    End Class

End Namespace
