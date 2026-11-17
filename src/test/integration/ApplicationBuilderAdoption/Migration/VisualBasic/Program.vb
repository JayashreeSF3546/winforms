' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Imports ApplicationBuilderAdoption

''' <summary>
''' Keeps explicit Main and the original VB Form; does not enable the VB Application Framework.
''' </summary>
Friend Module Program
    <STAThread>
    Public Function Main(args As String()) As Integer
        Return SampleEntryPoint.Run(
            Function()
                Using form As New LegacyForm()
                    Return MigrationSample.Run(args, form)
                End Using
            End Function)
    End Function
End Module
