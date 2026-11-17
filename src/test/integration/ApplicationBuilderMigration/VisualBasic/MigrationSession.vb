' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Namespace ApplicationBuilderMigration.VisualBasic
    ''' <summary>
    '''  Keeps the same UI-free work logic for both legacy and builder selections.
    ''' </summary>
    Friend NotInheritable Class MigrationSession
        Private _arguments As String()
        Private _count As Integer

        Friend ReadOnly Property Arguments As String()
            Get
                Return _arguments
            End Get
        End Property

        Friend ReadOnly Property Events As New List(Of String)()

        Friend Property Count As Integer
            Get
                Return _count
            End Get
            Private Set(value As Integer)
                _count = value
            End Set
        End Property

        Friend Sub New(tokens As String())
            _arguments = tokens.ToArray()
        End Sub

        Friend Function Start(commandLine As IEnumerable(Of String)) As Boolean
            _arguments = commandLine.ToArray()
            Events.Add("startup")
            Dim canceled As Boolean = Arguments.Contains("--cancel-startup", StringComparer.Ordinal)
            Events.Add(If(canceled, "canceled", "accepted"))

            Return Not canceled
        End Function

        Friend Sub ProcessItem()
            Count += 1
            Events.Add("processed")
        End Sub
    End Class
End Namespace
