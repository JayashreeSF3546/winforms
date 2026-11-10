' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Imports System.Text.Json
Imports System.Windows.Forms
Imports Microsoft.Extensions.WinForms

Namespace ApplicationBuilderMigration.VisualBasic
    ''' <summary>
    '''  Selects exactly one legacy or public-prototype builder driver on the caller's STA.
    ''' </summary>
    Friend Module Program
        <STAThread>
        Friend Sub Main(args As String())
            Dim probe As Boolean = args.Length > 1 AndAlso args(1) = "--probe"
            Dim separator As Integer = If(probe, 2, 1)
            If args.Length <= separator OrElse
                (args(0) <> "legacy" AndAlso args(0) <> "shared") OrElse args(separator) <> "--" Then
                Throw New ArgumentException("Expected: legacy|shared [--probe] -- [argument tokens]")
            End If

            Dim session As New MigrationSession(args.Skip(separator + 1).ToArray())
            If args(0) = "legacy" Then
                Dim application As New MyApplication(session, probe)
                application.Run(session.Arguments)
            Else
                Application.EnableVisualStyles()
                Application.SetCompatibleTextRenderingDefault(False)
                ' Application-owned preflight, NOT the internal Starting feature.
                If session.Start(session.Arguments) Then
                    Dim builder = WinFormsApplication.CreateBuilder().UseApplicationContext(
                        Function()
                            session.Events.Add("activate")
                            Return New ApplicationContext(New MainForm(session, probe, "shared"))
                        End Function)
                    Using application As WinFormsApplication = builder.Build()
                        AddHandler application.Lifetime.ApplicationStarted, Sub(sender, e) session.Events.Add("started")
                        AddHandler application.Lifetime.ApplicationStopping, Sub(sender, e) session.Events.Add("stopping")
                        AddHandler application.Lifetime.ApplicationStopped, Sub(sender, e) session.Events.Add("stopped")
                        application.Run()
                    End Using
                End If
            End If

            session.Events.Add("returned")
            Console.WriteLine(JsonSerializer.Serialize(New With {
                .Arguments = session.Arguments,
                .Events = session.Events,
                .Count = session.Count,
                .FormsModule = GetType(Form).Module.ModuleVersionId,
                .FormsPath = GetType(Form).Assembly.Location
            }))
        End Sub
    End Module
End Namespace
