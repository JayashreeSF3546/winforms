// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Windows.Forms;

namespace WinFormsApplicationBuilder.CSharp;

/// <summary>
///  Replaces its main form without ending the root application loop.
/// </summary>
internal sealed class SwitchingContext : ApplicationContext
{
    internal SwitchingContext()
    {
        MainForm = new MainForm("The custom context can replace this window without ending the run.", "Continue", ShowNext);
    }

    internal void ShowNext()
    {
        Form previous = MainForm!;
        MainForm = new MainForm("The old window is closed. Closing this window now ends the run.", "Close window", requestShutdown: null);
        MainForm.Show();
        previous.Close();
        Debug.WriteLine("Context switched");
    }
}
