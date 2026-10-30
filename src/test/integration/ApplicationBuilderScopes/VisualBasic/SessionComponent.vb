' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Imports System
Imports System.ComponentModel

Namespace ApplicationBuilderScopes.VisualBasic
    ''' <summary>
    ''' Is created by the Designer container and receives a borrowed dependency only at runtime.
    ''' </summary>
    Public NotInheritable Class SessionComponent
        Inherits Component

        Private _session As ScopeSession
        Private _disposed As Boolean

        Public Sub New()
            AddHandler Disposed, Sub(sender, e) _disposed = True
        End Sub

        Public Sub New(container As IContainer)
            Me.New()
            container.Add(Me)
        End Sub

        Friend ReadOnly Property Session As ScopeSession
            Get
                Return _session
            End Get
        End Property

        Friend Sub InitializeServices(session As ScopeSession)
            ObjectDisposedException.ThrowIf(_disposed, Me)
            ArgumentNullException.ThrowIfNull(session)
            If _session IsNot Nothing Then
                Throw New InvalidOperationException("Services have already been assigned.")
            End If

            _session = session
            AddHandler Disposed,
                Sub(sender, e)
                    session.VerifyAlive()
                    Console.WriteLine($"Component disposed: {session.Id}")
                End Sub
        End Sub
    End Class
End Namespace
