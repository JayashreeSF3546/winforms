' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Imports ApplicationBuilderAdoption

''' <summary>
''' Calls the shared C# composition and presentation from an explicit VB STA entry point.
''' </summary>
Friend Module Program
    <STAThread>
    Public Function Main(args As String()) As Integer
        Return SampleEntryPoint.Run(Function() EnterpriseSample.Run(args))
    End Function
End Module
