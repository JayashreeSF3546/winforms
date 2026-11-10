' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Imports System.Windows.Forms
Imports Microsoft.Extensions.WinForms
Imports Microsoft.VisualBasic.ApplicationServices

Namespace ApplicationBuilderParity.VisualBasic
    ''' <summary>
    '''  Composes measured legacy and shared lifetimes directly in VB.
    ''' </summary>
    Friend Module PerformanceWorkload
        Friend Sub Run(scenario As String, args As String())
            If scenario = "measure-ipc" Then
                BenchmarkMeasurements.RunIpc(args, AddressOf RunCycle)
            Else
                BenchmarkMeasurements.Run(args, AddressOf RunCycle)
            End If
        End Sub

        Private Sub RunCycle(cycle As BenchmarkCycle)
            If cycle.Mode = "legacy" Then
                Dim legacy As New MeasuredLegacyApplication(cycle)
                cycle.ApplicationReference.Target = legacy
                legacy.Run(cycle.Tokens)
                Return
            End If

            Dim primary As WinFormsSingleInstance = Nothing
            If cycle.Ipc Then
                Dim selection = WinFormsSingleInstance.SelectAsync(
                    WinFormsSingleInstanceIdentity.Create(cycle.Identity, "14981"), cycle.Tokens,
                    False, TimeSpan.FromSeconds(10)).GetAwaiter().GetResult()
                cycle.DeliveryResult = selection.Result.ToString()
                primary = selection.Primary
                If primary Is Nothing Then
                    Return
                End If
            End If

            Using primary
                Dim network As New MeasuredNetworkSource()
                Dim builder = WinFormsApplication.CreateBuilder().
                    UseApplicationContext(Function() New ApplicationContext(cycle.CreateForm())).
                    ConfigureApplicationFeatures(
                        Sub(features)
                            If cycle.Splash Then
                                features.SplashScreenFactory = Function() New WinFormsSplashScreen()
                                features.MinimumSplashDisplayTime = TimeSpan.Zero
                            End If

                            If cycle.Network Then
                                features.NetworkSource = network
                                features.NetworkAvailabilityChanged = Sub(available)
                                                                      End Sub
                            End If
                        End Sub)
                If primary IsNot Nothing Then
                    builder.UseSingleInstance(
                        primary,
                        Sub(sender, request)
                            cycle.Activated(request.CommandLine)
                            request.BringToForeground = False
                        End Sub)
                End If

                Using application As WinFormsApplication = builder.Build()
                    cycle.ApplicationReference.Target = application
                    application.Run()
                End Using

                cycle.SubscriptionsAfterRun = network.Subscriptions
            End Using
        End Sub
    End Module

    ''' <summary>
    '''  Measures the unchanged VB application framework, including its own splash and IPC implementation.
    ''' </summary>
    Friend Class MeasuredLegacyApplication
        Inherits WindowsFormsApplicationBase

        Private ReadOnly _cycle As BenchmarkCycle

        Friend Sub New(cycle As BenchmarkCycle)
            MyBase.New(AuthenticationMode.ApplicationDefined)
            _cycle = cycle
            EnableVisualStyles = False
            SaveMySettingsOnExit = False
            IsSingleInstance = cycle.Ipc
            MinimumSplashScreenDisplayTime = 0
            If cycle.Network Then
                AddHandler NetworkAvailabilityChanged, AddressOf OnNetwork
            End If
        End Sub

        Protected Overrides Sub OnCreateMainForm()
            MainForm = _cycle.CreateForm()
        End Sub

        Protected Overrides Sub OnCreateSplashScreen()
            If _cycle.Splash Then
                SplashScreen = New Form With {.ShowInTaskbar = False}
            End If
        End Sub

        Protected Overrides Sub OnStartupNextInstance(e As StartupNextInstanceEventArgs)
            _cycle.Activated(e.CommandLine)
            e.BringToForeground = False
            MyBase.OnStartupNextInstance(e)
        End Sub

        Private Sub OnNetwork(sender As Object, e As Microsoft.VisualBasic.Devices.NetworkAvailableEventArgs)
        End Sub
    End Class
End Namespace
