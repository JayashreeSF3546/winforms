' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Drawing
Imports System.Windows.Forms
Imports Microsoft.Extensions.WinForms

''' <summary>
'''  Demonstrates shared Application Builder migration behavior in Visual Basic.
''' </summary>
Friend Module Program

    Private Const ApplicationId As String = "WinFormsApplicationBuilder.MigrationSample.VisualBasic"

    <STAThread>
    Friend Sub Main(args As String())
        Application.SetHighDpiMode(HighDpiMode.SystemAware)
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)

        Dim mainForm As New MainForm(args)
        Dim builder As WinFormsApplicationBuilder =
            WinFormsApplication.CreateBuilder(args).
                UseSingleInstance(ApplicationId).
                UseSplashScreen(Of SplashForm)(TimeSpan.FromMilliseconds(750)).
                UseStartupForm(mainForm)

        Using application As WinFormsApplication = builder.Build()
            AddHandler application.Lifetime.ApplicationStarting,
                Sub(sender, eventArgs) Debug.WriteLine("ApplicationStarting")
            AddHandler application.Lifetime.ApplicationStarted,
                Sub(sender, eventArgs) Debug.WriteLine("ApplicationStarted")
            AddHandler application.Lifetime.ApplicationStopping,
                Sub(sender, eventArgs) Debug.WriteLine("ApplicationStopping")
            AddHandler application.Lifetime.ApplicationStopped,
                Sub(sender, eventArgs) Debug.WriteLine("ApplicationStopped")
            AddHandler application.ActivationRequested,
                Sub(sender, eventArgs) mainForm.ShowActivation(eventArgs.Arguments)

            application.Run()
        End Using
    End Sub
End Module

''' <summary>
'''  Displays startup and forwarded activation arguments.
''' </summary>
Friend NotInheritable Class MainForm
    Inherits Form

    Private ReadOnly _activationLabel As New Label With {
        .AutoSize = True,
        .Location = New Point(16, 52),
        .Text = "No startup arguments supplied."
    }

    Public Sub New(arguments As String())
        Text = "Application Builder migration sample (Visual Basic)"
        ClientSize = New Size(520, 170)
        StartPosition = FormStartPosition.CenterScreen

        Dim description As New Label With {
            .AutoSize = True,
            .Location = New Point(16, 16),
            .Text = "Close this window to observe coordinated shutdown."
        }

        Dim closeButton As New Button With {
            .Anchor = AnchorStyles.Bottom Or AnchorStyles.Right,
            .Location = New Point(420, 120),
            .Text = "Close"
        }
        AddHandler closeButton.Click, Sub(sender, eventArgs) Close()

        Controls.Add(description)
        Controls.Add(_activationLabel)
        Controls.Add(closeButton)

        If arguments.Length > 0 Then
            _activationLabel.Text = $"Startup arguments: {String.Join(", ", arguments)}"
        End If
    End Sub

    Friend Sub ShowActivation(arguments As IReadOnlyList(Of String))
        _activationLabel.Text = $"Forwarded arguments: {String.Join(", ", arguments)}"
    End Sub
End Class

''' <summary>
'''  Provides a small splash form for the migration sample.
''' </summary>
Friend NotInheritable Class SplashForm
    Inherits Form

    Public Sub New()
        Text = "Starting"
        ClientSize = New Size(320, 100)
        FormBorderStyle = FormBorderStyle.FixedDialog
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterScreen

        Controls.Add(New Label With {
            .AutoSize = True,
            .Location = New Point(24, 34),
            .Text = "Starting the migrated application..."
        })
    End Sub
End Class
