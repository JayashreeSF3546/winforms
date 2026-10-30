' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Imports System
Imports System.Windows.Forms

Namespace ApplicationBuilderScopes.VisualBasic
    ''' <summary>
    ''' Preserves service-free Designer construction alongside explicit runtime constructor injection.
    ''' </summary>
    Public Partial Class ScopeForm
        Inherits Form

        Public Sub New()
            InitializeComponent()
        End Sub

        Friend Sub New(session As ScopeSession, title As String)
            Me.New()
            Me.Session = session
            Text = $"{title} - VB - {session.Id}"
            AddHandler Disposed,
                Sub(sender, e)
                    session.VerifyAlive()
                    Console.WriteLine($"Form UI disposed: {session.Id}")
                End Sub
        End Sub

        Friend ReadOnly Property Session As ScopeSession
        Friend ReadOnly Property SessionView As SessionControl
            Get
                Return sessionViewControl
            End Get
        End Property
        Friend ReadOnly Property SessionWorker As SessionComponent
            Get
                Return sessionWorkerComponent
            End Get
        End Property

        Friend Sub VerifyAssignment()
            If Session Is Nothing OrElse Not ReferenceEquals(Session, SessionView.Session) OrElse
                Not ReferenceEquals(Session, SessionWorker.Session) Then
                Throw New InvalidOperationException("Designer-created children must borrow their form's typed service.")
            End If

            Console.WriteLine($"Typed assignment verified: {Session.Id}")
        End Sub

        Friend Sub ConfigureNavigation(modeless As Action, modal As Action, nextPage As Action, cancel As Action)
            AddHandler modelessButton.Click, Sub(sender, e) modeless()
            AddHandler modalButton.Click, Sub(sender, e) modal()
            nextButton.Enabled = nextPage IsNot Nothing
            AddHandler nextButton.Click, Sub(sender, e) nextPage()
            AddHandler cancelApplicationButton.Click, Sub(sender, e) cancel()
            AddHandler closeButton.Click, Sub(sender, e) Close()
        End Sub
    End Class

End Namespace
