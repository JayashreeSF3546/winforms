// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using WinFormsControlsTest;

// Set STAThread
Thread.CurrentThread.SetApartmentState(ApartmentState.Unknown);
Thread.CurrentThread.SetApartmentState(ApartmentState.STA);
ApplicationConfiguration.Initialize();

Application.SetColorMode(SystemColorMode.Classic);
Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
Thread.CurrentThread.CurrentUICulture = Thread.CurrentThread.CurrentCulture;

try
{
    MainForm form = new()
    {
        Icon = SystemIcons.GetStockIcon(StockIconId.Shield, StockIconOptions.SmallIcon)
    };

    Application.Run(form);
}
catch (Exception)
{
    Environment.Exit(-1);
}

Environment.Exit(0);


// ~~~~~~~~~~~~~~~~~~ builder
//Thread.CurrentThread.CurrentUICulture = Thread.CurrentThread.CurrentCulture;

//try
//{
//    WinFormsApplicationBuilder builder =
//        WinFormsApplication.CreateBuilder(args);

//    builder
//        .UseHighDpiMode(HighDpiMode.SystemAware)
//        .UseVisualStyles()
//        .UseColorMode(SystemColorMode.Classic)
//        .UseStartupForm<MainForm>();

//    using WinFormsApplication application = builder.Build();
//    application.Run();

//    Environment.ExitCode = 0;
//}
//catch (Exception exception)
//{
//    Console.Error.WriteLine(exception);
//    Environment.ExitCode = -1;
//}
