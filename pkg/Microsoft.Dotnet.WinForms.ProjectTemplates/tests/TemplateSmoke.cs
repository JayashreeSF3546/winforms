// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.WinForms;

/// <summary>
///  Exercises the generated entry point and observes its host without adding application test hooks.
/// </summary>
internal sealed class TemplateSmoke : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
{
    private readonly List<IDisposable> _subscriptions = [];
    private readonly IDisposable _listeners;
    private IHost? _host;

    private TemplateSmoke()
    {
        _listeners = DiagnosticListener.AllListeners.Subscribe(this);
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string scenario = args.Single();
            switch (scenario)
            {
                case "services":
                    string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "WinForms", Program.SettingsApplicationId);
                    Check(!Directory.Exists(directory), "The settings test requires a newly generated application identity.");
                    try
                    {
                        CheckUi(scenario);
                    }
                    finally
                    {
                        if (Directory.Exists(directory))
                        {
                            Directory.Delete(directory, recursive: true);
                        }
                    }

                    break;
                case "designer":
                    using (Form1 form = new())
                    {
                        Check(!form.IsHandleCreated && form.Text == "Form1", "Parameterless construction must be UI-only.");
                    }

                    break;
                case "ui":
                case "host-stop":
                    CheckUi(scenario);
                    break;
                default:
                    throw new ArgumentException("Unknown template smoke scenario.");
            }

            Console.WriteLine($"PASS C#: {scenario}");

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);

            return 1;
        }
    }

    private static void CheckServices(IHost host)
    {
        Check(host.Services.GetServices<ILoggerProvider>()
            .OfType<Microsoft.Extensions.Logging.Debug.DebugLoggerProvider>().Count() == 1,
            "The WinForms logging helper must register one Debug provider.");
        _ = host.Services.GetRequiredService<WinFormsExceptionLogging>();
        var settings = host.Services.GetRequiredService<IUserSettingsService<StartupPreferences>>();
        Task.Run(async () =>
        {
            Check((await settings.LoadAsync()).UseCompactLayout, "Incorrect fresh preferences.");
            UserSettingsCommit commit = await settings.SaveAsync(new StartupPreferences { UseCompactLayout = false });
            await commit.NotificationCompletion;
            Check(!(await settings.LoadAsync()).UseCompactLayout, "Settings round trip failed.");
        }).GetAwaiter().GetResult();
    }

    private static void CheckUi(string scenario)
    {
        using TemplateSmoke capture = new();
        bool dispatched = false;
        bool disposed = false;
        Exception? failure = null;
        EventHandler idle = (_, _) =>
        {
            if (dispatched || Application.OpenForms.Count == 0)
            {
                return;
            }

            dispatched = true;
            Form form = Application.OpenForms[0]!;
            form.Disposed += (_, _) => disposed = true;
            try
            {
                Check(form is Form1 && form.Text == "Form1", "The generated startup form was not shown.");
                Check(Application.HighDpiMode == HighDpiMode.SystemAware
                    && Application.DefaultFormRevealMode == FormRevealMode.Deferred
                    && Application.DefaultVisualStylesMode == VisualStylesMode.Net11,
                    "The fluent startup configuration was not applied.");
                IHost host = capture._host ?? throw new InvalidOperationException("The generated host was not built.");
                if (scenario == "services")
                {
                    CheckServices(host);
                }

                if (scenario == "host-stop")
                {
                    host.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();
                }
                else
                {
                    form.Close();
                }
            }
            catch (Exception exception)
            {
                failure = exception;
                form.Close();
            }
        };
        Application.Idle += idle;
        try
        {
            Program.Main();
        }
        finally
        {
            Application.Idle -= idle;
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Throw(failure);
        }

        Check(dispatched && disposed && Application.OpenForms.Count == 0, "UI dispatch/cleanup failed.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public void OnNext(DiagnosticListener listener)
    {
        if (listener.Name == "Microsoft.Extensions.Hosting")
        {
            _subscriptions.Add(listener.Subscribe(this));
        }
    }

    public void OnNext(KeyValuePair<string, object?> notification)
    {
        if (notification.Key == "HostBuilt" && notification.Value is IHost host)
        {
            _host = host;
        }
    }

    public void OnCompleted()
    {
    }

    public void OnError(Exception error) => ExceptionDispatchInfo.Throw(error);

    public void Dispose()
    {
        _listeners.Dispose();
        foreach (IDisposable subscription in _subscriptions)
        {
            subscription.Dispose();
        }
    }
}
