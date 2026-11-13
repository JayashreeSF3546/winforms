' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Imports ApplicationBuilderAdoption

''' <summary>
''' Calls the C# context-owned tray sample without enabling the VB Application Framework.
''' </summary>
Friend Module Program
    <STAThread>
    Public Function Main(args As String()) As Integer
        Return SampleEntryPoint.Run(Function() TraySample.Run(args))
    End Function
End Module
