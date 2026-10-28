// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.WinForms;

namespace WinFormsApplicationBuilder.CSharp;

/// <summary>
///  Selects a caller-owned STA application and its shutdown policy.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        string scenario = args.Length == 0 ? "form" : args[0];
        if (scenario is not ("form" or "context" or "background" or "cancellation" or "shutdown"))
        {
            throw new ArgumentException("Expected form, context, background, cancellation, or shutdown.");
        }

        bool dark = false;
        bool componentManager = false;
        bool smoke = false;
        foreach (string argument in args.Skip(1))
        {
            switch (argument)
            {
                case "--dark":
                    dark = true;
                    break;
                case "--component-manager":
                    componentManager = true;
                    break;
                case "--smoke":
                    smoke = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown option: {argument}");
            }
        }

        AppContext.SetSwitch("Switch.System.Windows.Forms.EnableMsoComponentManager", componentManager);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetColorMode(dark ? SystemColorMode.Dark : SystemColorMode.Classic);
        Debug.WriteLine($"Session: {(SystemInformation.TerminalServerSession ? "Terminal Services" : "local")}");
        Debug.WriteLine($"Architecture: {RuntimeInformation.ProcessArchitecture}");
        Debug.WriteLine($"Color: {(dark ? "dark" : "light")}");
        Debug.WriteLine($"Backend: {(componentManager ? "component-manager" : "light")}");

        using CancellationTokenSource cancellation = new();
        TaskCompletionSource uiReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        WinFormsApplication? application = null;
        SwitchingContext? context = null;
        Task? shutdown = null;
        var builder = WinFormsApplication.CreateBuilder();
        switch (scenario)
        {
            case "form":
                builder.UseStartupForm<MainForm>();
                break;
            case "context":
                builder.UseApplicationContext(() => context = new SwitchingContext());
                break;
            case "background":
                builder.UseHost(new HostBuilder().ConfigureServices((_, services) =>
                    services.AddSingleton<IHostedService>(provider => new SampleWorker(
                        provider.GetRequiredService<IHostApplicationLifetime>(), uiReady.Task, smoke))));
                builder.UseApplicationContext(() => new ApplicationContext(new MainForm(
                    "Background work stops the host after five updates. Close early to cancel it.",
                    "Close window",
                    requestShutdown: null)));
                break;
            case "cancellation":
                builder.UseApplicationContext(() => new ApplicationContext(new MainForm(
                    "Cancel Run; cleanup finishes before cancellation is reported.",
                    "Cancel application",
                    cancellation.Cancel)));
                break;
            case "shutdown":
                builder.UseHost(new HostBuilder());
                builder.UseApplicationContext(() => new ApplicationContext(new MainForm(
                    "Host shutdown cannot be vetoed. Do not block or await the terminating UI dispatcher.",
                    "Stop host",
                    () => shutdown = application!.Host!.StopAsync(CancellationToken.None),
                    vetoClose: true)));
                break;
        }

        using WinFormsApplication ownedApplication = builder.Build();
        application = ownedApplication;
        var hostLifetime = application.Host?.Services.GetRequiredService<IHostApplicationLifetime>();
        using CancellationTokenRegistration hostStopped = hostLifetime?.ApplicationStopped.Register(
            () => Debug.WriteLine("Host stopped")) ?? default;
        application.Lifetime.ApplicationStarted += (_, _) =>
        {
            Debug.WriteLine("Started");
            SynchronizationContext.Current!.Post(_ =>
            {
                Debug.WriteLine("UI dispatched");
                uiReady.SetResult();
                if (!smoke || scenario == "background")
                {
                    return;
                }

                switch (scenario)
                {
                    case "context":
                        context!.ShowNext();
                        context.MainForm!.BeginInvoke(context.MainForm.Close);
                        break;
                    case "cancellation":
                        cancellation.Cancel();
                        break;
                    case "shutdown":
                        shutdown = application.Host!.StopAsync(CancellationToken.None);
                        break;
                    default:
                        Application.ExitThread();
                        break;
                }
            }, null);
        };
        application.Lifetime.ApplicationStopping += (_, _) => Debug.WriteLine("Stopping");
        application.Lifetime.ApplicationStopped += (_, _) => Debug.WriteLine("Stopped");
        try
        {
            application.Run(cancellation.Token);
        }
        catch (OperationCanceledException exception) when (
            cancellation.IsCancellationRequested && exception.CancellationToken == cancellation.Token)
        {
            Debug.WriteLine("Canceled after UI cleanup");
        }

        // The UI dispatcher has terminated. Observe the shared stop task only after Run returns.
        shutdown?.GetAwaiter().GetResult();
    }
}
