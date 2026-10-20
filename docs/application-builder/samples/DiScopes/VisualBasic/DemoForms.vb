' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Imports System.ComponentModel
Imports System.Diagnostics
Imports System.Windows.Forms
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.WinForms

''' <summary>
'''  The startup Form demonstrates root activation and Designer-created descendants.
''' </summary>
Friend NotInheritable Partial Class MainForm
    Inherits Form

    Private _session As SessionService
    Private _application As WinFormsApplication

    Public Sub New()
        InitializeComponent()
    End Sub

    Public Sub New(session As SessionService, application As WinFormsApplication)
        Me.New()
        _session = session
        _application = application
        _sessionSummary.Session = session
        _sessionDiagnostics.Session = session
        _statusLabel.Text = $"Startup Form scope: {session.Id:N}"
    End Sub

    Private Sub OnOpenModelessClick(sender As Object, e As EventArgs)
        OpenDetails()
    End Sub

    Private Sub OnReviewClick(sender As Object, e As EventArgs)
        Dim session As SessionService = GetSession()
        Dim application As WinFormsApplication = GetApplication()
        Dim result As DialogResult = application.ShowDialog(
            Function(services) ActivatorUtilities.CreateInstance(Of ReviewDialog)(services, session.Id),
            Me)

        If result = DialogResult.OK Then
            OpenDetails()
        Else
            Debug.WriteLine("Navigation canceled in the modal dialog.")
        End If
    End Sub

    Private Sub OnCloseClick(sender As Object, e As EventArgs)
        Close()
    End Sub

    Private Sub OpenDetails()
        Dim session As SessionService = GetSession()
        Dim application As WinFormsApplication = GetApplication()
        Dim detailsForm As DetailsForm = application.CreateForm(
            Function(services) ActivatorUtilities.CreateInstance(Of DetailsForm)(services, session.Id))

        detailsForm.Show(Me)
    End Sub

    Private Function GetSession() As SessionService
        If _session Is Nothing Then
            Throw New InvalidOperationException("Runtime services are not assigned to this Form.")
        End If

        Return _session
    End Function

    Private Function GetApplication() As WinFormsApplication
        If _application Is Nothing Then
            Throw New InvalidOperationException("The Form is not running in the sample application.")
        End If

        Return _application
    End Function
End Class

''' <summary>
'''  A modeless Form activated with an independent service scope.
''' </summary>
Friend NotInheritable Partial Class DetailsForm
    Inherits Form

    Public Sub New()
        InitializeComponent()
    End Sub

    Public Sub New(session As SessionService, parentSessionId As Guid)
        Me.New()
        _sessionLabel.Text = $"Modeless Form scope: {session.Id:N}"
        _parentSessionLabel.Text = $"Opened from scope: {parentSessionId:N}"
    End Sub
End Class

''' <summary>
'''  A modal Form that accepts or cancels navigation.
''' </summary>
Friend NotInheritable Partial Class ReviewDialog
    Inherits Form

    Public Sub New()
        InitializeComponent()
    End Sub

    Public Sub New(session As SessionService, parentSessionId As Guid)
        Me.New()
        _scopeLabel.Text = $"Modal dialog scope: {session.Id:N}"
        _parentSessionLabel.Text = $"Opened from scope: {parentSessionId:N}"
    End Sub
End Class

''' <summary>
'''  A Designer-created UserControl that receives its Form's scoped service explicitly.
''' </summary>
Friend NotInheritable Partial Class SessionSummaryControl
    Inherits UserControl

    Private _session As SessionService

    Public Sub New()
        InitializeComponent()
    End Sub

    Friend Property Session As SessionService
        Get
            Return _session
        End Get
        Set(value As SessionService)
            _session = value
            UpdateSummary()
        End Set
    End Property

    Private Sub UpdateSummary()
        If _summaryLabel IsNot Nothing Then
            If _session Is Nothing Then
                _summaryLabel.Text = "Designer-created UserControl (no runtime scope assigned)"
            Else
                _summaryLabel.Text = $"UserControl uses Form scope: {_session.Id:N}"
            End If
        End If
    End Sub
End Class

''' <summary>
'''  A Designer-created component that reports disposal while its Form scope is alive.
''' </summary>
Friend NotInheritable Class SessionDiagnosticsComponent
    Inherits Component

    Public Sub New()
    End Sub

    Public Sub New(container As IContainer)
        If container Is Nothing Then
            Throw New ArgumentNullException(NameOf(container))
        End If

        container.Add(Me)
    End Sub

    Friend Property Session As SessionService

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            Dim state As String
            If Session Is Nothing Then
                state = "unassigned"
            ElseIf Session.IsDisposed Then
                state = "already disposed"
            Else
                state = "still alive"
            End If

            Debug.WriteLine($"Designer-created component disposed; Form scope is {state}.")
        End If

        MyBase.Dispose(disposing)
    End Sub
End Class
