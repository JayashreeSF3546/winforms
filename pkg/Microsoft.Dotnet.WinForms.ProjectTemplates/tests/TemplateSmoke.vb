' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Imports System
Imports System.IO
Imports System.Linq
Imports System.Text.Json
Imports Microsoft.Extensions.Configuration
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.Hosting
Imports Microsoft.Extensions.Logging
Imports Microsoft.Extensions.Options
Imports Microsoft.Extensions.WinForms

''' <summary>
'''  Exercises the generated VB composition without a C# helper or application test switches.
''' </summary>
Friend Module TemplateSmoke
    <STAThread>
    Public Function Main(args As String()) As Integer
        Try
            Select Case args.Single()
                Case "services"
                    CheckServices()
                Case "invalid-options"
                    Using host As IHost = Program.CreateHostBuilder({"--Application:Title="}).Build()
                        Dim rejected As Boolean = False
                        Try
                            host.StartAsync().GetAwaiter().GetResult()
                        Catch exception As OptionsValidationException
                            rejected = True
                        End Try
                        Check(rejected, "Invalid options must fail host startup.")
                    End Using
                Case "designer"
                    Using form As New Form1()
                        Check(Not form.IsHandleCreated AndAlso form.Text = "Form1", "Parameterless construction must be UI-only.")
                    End Using
                Case "ui", "host-stop"
                    CheckUi(args(0) = "host-stop")
                Case Else
                    Throw New ArgumentException("Unknown template smoke scenario.")
            End Select

            Console.WriteLine($"PASS VB: {args(0)}")

            Return 0
        Catch exception As Exception
            Console.Error.WriteLine(exception)

            Return 1
        End Try
    End Function

    Private Sub CheckServices()
        Const prefix As String = "WinFormsApp."
        Dim identity As Guid
        Check(Program.SettingsApplicationId.StartsWith(prefix, StringComparison.Ordinal) AndAlso
              Guid.TryParse(Program.SettingsApplicationId.Substring(prefix.Length), identity), "Invalid generated settings identity.")
        Dim directoryPath As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                                   "WinForms", Program.SettingsApplicationId)
        Check(Not Directory.Exists(directoryPath), "The settings test requires a newly generated, unused application identity.")
        Try
            Using host As IHost = Program.CreateHostBuilder(Array.Empty(Of String)()).Build()
                host.StartAsync().GetAwaiter().GetResult()
                Using document As JsonDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json")))
                    Dim title As String = document.RootElement.GetProperty("Application").GetProperty("Title").GetString()
                    Check(host.Services.GetRequiredService(Of IOptions(Of ApplicationOptions))().Value.Title = title,
                          "Deployment JSON was not applied.")
                End Using
                Check(host.Services.GetRequiredService(Of IConfiguration)()("hostBuilder:reloadConfigOnChange") = "false",
                      "Watchers must default off.")
                Dim settings = host.Services.GetRequiredService(Of IUserSettingsService(Of UserPreferences))()
                Check(Not Directory.Exists(directoryPath), "Registration/resolution must not create storage.")
                Check(settings.LoadAsync().GetAwaiter().GetResult().LastFolder = String.Empty, "Incorrect fresh preferences.")
                Dim commit As UserSettingsCommit = settings.SaveAsync(New UserPreferences With {.LastFolder = "template-test"}).
                    GetAwaiter().GetResult()
                commit.NotificationCompletion.GetAwaiter().GetResult()
                Check(settings.LoadAsync().GetAwaiter().GetResult().LastFolder = "template-test", "Settings round trip failed.")
                host.StopAsync().GetAwaiter().GetResult()
            End Using
        Finally
            If Directory.Exists(directoryPath) Then
                Directory.Delete(directoryPath, recursive:=True)
            End If
        End Try

        Dim previous As String = Environment.GetEnvironmentVariable("Application__Title")
        Try
            Environment.SetEnvironmentVariable("Application__Title", "environment-title")
            CheckTitle(Array.Empty(Of String)(), "environment-title")
            CheckTitle({"--Application:Title=command-title"}, "command-title")
        Finally
            Environment.SetEnvironmentVariable("Application__Title", previous)
        End Try
    End Sub

    Private Sub CheckTitle(args As String(), expected As String)
        Using host As IHost = Program.CreateHostBuilder(args).Build()
            host.StartAsync().GetAwaiter().GetResult()
            Check(host.Services.GetRequiredService(Of IOptions(Of ApplicationOptions))().Value.Title = expected,
                  "Configuration precedence failed.")
            host.StopAsync().GetAwaiter().GetResult()
        End Using
    End Sub

    Private Sub CheckUi(hostStop As Boolean)
        Dim dispatched As Boolean = False
        Dim disposed As Boolean = False
        Dim titleMatches As Boolean = False
        Dim lifetime As IHostApplicationLifetime = Nothing
        Dim idle As EventHandler =
            Sub(sender, arguments)
                If dispatched OrElse Application.OpenForms.Count = 0 Then
                    Return
                End If

                dispatched = True
                Dim form As Form = Application.OpenForms(0)
                titleMatches = TypeOf form Is Form1 AndAlso form.Text = "template-ui"
                AddHandler form.Disposed, Sub(disposedSender, disposedArguments) disposed = True
                If hostStop Then
                    lifetime.StopApplication()
                Else
                    form.Close()
                End If
            End Sub
        AddHandler Application.Idle, idle
        Try
            Dim args As String() = {"--Application:Title=template-ui"}
            If hostStop Then
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException)
                Application.SetHighDpiMode(HighDpiMode.SystemAware)
                Application.EnableVisualStyles()
                Application.SetCompatibleTextRenderingDefault(False)
                Dim winFormsApplication As WinFormsApplication = Nothing
                winFormsApplication = WinFormsApplication.CreateBuilder().
                    UseHost(Program.CreateHostBuilder(args)).
                    UseApplicationContext(
                        Function()
                            Dim services As IServiceProvider = winFormsApplication.Host.Services

                            Return New ApplicationContext(New Form1(services.GetRequiredService(Of IOptions(Of ApplicationOptions))().Value,
                                                                   services.GetRequiredService(Of ILogger(Of Form1))()))
                        End Function).
                    Build()
                Using winFormsApplication
                    lifetime = winFormsApplication.Host.Services.GetRequiredService(Of IHostApplicationLifetime)()
                    winFormsApplication.Run()
                End Using
            Else
                Check(Program.Main(args) = 0, "Generated Main failed.")
            End If
        Finally
            RemoveHandler Application.Idle, idle
        End Try

        Check(dispatched AndAlso titleMatches AndAlso disposed AndAlso Application.OpenForms.Count = 0, "UI dispatch/cleanup failed.")
    End Sub

    Private Sub Check(condition As Boolean, message As String)
        If Not condition Then
            Throw New InvalidOperationException(message)
        End If
    End Sub
End Module
