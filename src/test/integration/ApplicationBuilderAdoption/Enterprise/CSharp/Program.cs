// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ApplicationBuilderAdoption.Enterprise.CSharp;

/// <summary>
///  Enters the shared layered sample on the application's STA thread.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
        => SampleEntryPoint.Run(() => EnterpriseSample.Run(args));
}
