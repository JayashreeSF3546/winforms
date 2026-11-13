// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Forms;

namespace ApplicationBuilderAdoption;

/// <summary>
///  Applies the same native UI setup and explicit process failure reporting to each STA entry point.
/// </summary>
public static class SampleEntryPoint
{
    public static int Run(Func<int> run)
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            return run();
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            Console.Error.WriteLine($"APPLICATION FAILED: {exception.GetType().Name}: {exception.Message}");

            return 1;
        }
    }
}
