// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ApplicationBuilderAdoption.Tray.CSharp;

/// <summary>
///  Enters a context-owned tray lifetime without a startup Form.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
        => SampleEntryPoint.Run(() => TraySample.Run(args));
}
