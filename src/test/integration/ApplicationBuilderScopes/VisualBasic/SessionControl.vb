' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Imports System
Imports System.Windows.Forms

Namespace ApplicationBuilderScopes.VisualBasic
    ''' <summary>
    ''' Borrows a typed service without resolving services or creating a control-owned scope.
    ''' </summary>
    Public Partial Class SessionControl
        Inherits UserControl

        Private _session As ScopeSession

        Public Sub New()
            InitializeComponent()
        End Sub

        Friend ReadOnly Property Session As ScopeSession
            Get
                Return _session
            End Get
        End Property

        Friend Sub InitializeServices(session As ScopeSession)
            ObjectDisposedException.ThrowIf(IsDisposed, Me)
            ArgumentNullException.ThrowIfNull(session)
            If _session IsNot Nothing Then
                Throw New InvalidOperationException("Services have already been assigned.")
            End If

            _session = session
            identityLabel.Text = $"Form/UserControl/component share session: {session.Id}"
            AddHandler Disposed,
                Sub(sender, e)
                    session.VerifyAlive()
                    Console.WriteLine($"UserControl disposed: {session.Id}")
                End Sub
        End Sub
    End Class
End Namespace
