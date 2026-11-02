// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Forms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.WinForms;

namespace ApplicationBuilderInfrastructure;

/// <summary>
///  Composes only public prototype contracts for both language-specific sample front ends.
/// </summary>
public static class SampleApplication
{
    public static int Run(string[] args, Func<SampleSession, Form> createForm)
    {
        ArgumentNullException.ThrowIfNull(createForm);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using ILoggerFactory diagnosticsFactory = LoggerFactory.Create(logging => logging.AddDebug());
        SampleSession? session = null;
        try
        {
            IHostBuilder hostBuilder = new HostBuilder()
                .ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(
                    [new("hostBuilder:reloadConfigOnChange", "false")]))
                .ConfigureWinFormsConfiguration(args)
                .ConfigureLogging(logging =>
                {
                    logging.AddDebug();
                    logging.AddFilter("Microsoft.Extensions.Diagnostics.HealthChecks", LogLevel.None);
                })
                .ConfigureServices((context, services) =>
                {
                    services.AddOptions<SampleOptions>()
                        .Bind(context.Configuration.GetSection("Sample"))
                        .Validate(options => options.WorkDelayMilliseconds is >= 1 and <= 30000,
                            "Sample:WorkDelayMilliseconds must be between 1 and 30000.")
                        .ValidateOnStart();
                    services.AddJsonUserSettings("ApplicationBuilderInfrastructureSample", 2,
                        PreferencesJsonContext.Default.SamplePreferences, () => new SamplePreferences(), options =>
                        {
                            options.StorageDirectory = context.Configuration["Sample:StorageDirectory"];
                            options.Validate = preferences =>
                                ArgumentOutOfRangeException.ThrowIfNegative(preferences.Value);
                            options.Migrations.Add(1, old => old.Clone());
                        });
                    services.AddWinFormsHealthDiagnostics().AddCheck<OfflineHealthCheck>("offline", tags: ["diagnostic"]);
                    services.AddSingleton<SampleSession>();
                    services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<SampleSession>());
                    services.AddHostedService<SampleWorker>();
                    services.Configure<HostOptions>(options => options.ShutdownTimeout = Timeout.InfiniteTimeSpan);
                });
            WinFormsApplication? application = null;
            using (application = WinFormsApplication.CreateBuilder()
                .UseHost(hostBuilder)
                .UseApplicationContext(() =>
                {
                    session = application!.Host!.Services.GetRequiredService<SampleSession>();
                    Console.WriteLine($"STARTED {session.Describe()}");
                    Console.WriteLine($"TERMINALSERVER {SystemInformation.TerminalServerSession}");

                    return new ApplicationContext(createForm(session));
                })
                .Build())
            {
                using WinFormsExceptionLogging logging = WinFormsExceptionLogging.Attach(application,
                    diagnosticsFactory.CreateLogger("InfrastructureSample"), (_, arguments) =>
                    {
                        if (arguments.Exception is SampleUiException && session is not null)
                        {
                            session.RecordUiException();
                        }
                        else
                        {
                            Console.Error.WriteLine($"UI FAILURE: {arguments.Exception.GetType().Name}");
                            session?.AutomationFailure = arguments.Exception;

                            application.Host!.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();
                        }
                    }, failure => Console.Error.WriteLine($"LOGGING FAILURE: {failure.Exception.GetType().Name}"));
                try
                {
                    application.Run();
                }
                catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
                {
                    logging.LogRunFailure(exception);
                    throw;
                }
                finally
                {
                    logging.Dispose();
                    logging.DiagnosticCompletion.GetAwaiter().GetResult();
                }
            }

            if (session is null || session.AutomationFailure is not null || !session.WorkerStopped)
            {
                throw new InvalidOperationException("The sample did not complete its owned work successfully.",
                    session?.AutomationFailure);
            }

            Console.WriteLine($"PASS {session.Scenario}: worker stopped; owner operations drained; host disposed");

            return 0;
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            Console.Error.WriteLine($"APPLICATION FAILED: {exception.GetType().Name}");

            return 1;
        }
    }

    public static async Task RunScenarioAsync(SampleSession session, Action close, Action uiException)
    {
        switch (session.Scenario)
        {
            case "":
                return;
            case "smoke":
                foreach (string operation in new[] { "Save", "Load", "Health", "Reload", "Save" })
                {
                    SampleResult result = await session.ExecuteAsync(operation, 42);
                    if (result.Failed || (operation is "Save" or "Load" && result.Value != 42))
                    {
                        throw new InvalidOperationException($"Smoke operation {operation} failed.");
                    }
                }

                uiException();
                await session.UiExceptionHandled.Task;
                if (session.UiExceptions != 1)
                {
                    throw new InvalidOperationException("The explicit UI exception policy was not invoked.");
                }

                close();
                return;
            case "close-pending":
            case "host-stop":
            case "worker-failure":
                _ = session.ExecuteAsync("Wait");
                await session.OperationEntered.Task;
                if (session.Scenario == "host-stop")
                {
                    session.RequestHostStop();
                }
                else if (session.Scenario == "worker-failure")
                {
                    session.RequestWorkerFailure();
                }
                else
                {
                    close();
                }

                return;
            case "upgrade":
                SampleResult upgraded = await session.ExecuteAsync("Upgrade");
                if (upgraded.Failed || upgraded.Value != 42)
                {
                    throw new InvalidOperationException("Settings upgrade did not preserve the old value.");
                }

                close();
                return;
            case "recover":
                SampleResult damaged = await session.ExecuteAsync("Load");
                SampleResult recovered = await session.ExecuteAsync("Recover");
                SampleResult reset = await session.ExecuteAsync("Reset");
                if (!damaged.Failed || recovered.Failed || recovered.Value != 42 || reset.Failed || reset.Value != 7)
                {
                    throw new InvalidOperationException("Explicit recovery/reset did not produce the expected preferences.");
                }

                close();
                return;
            default:
                throw new ArgumentException("Unknown Sample:Scenario.");
        }
    }
}

/// <summary>
///  Identifies the one intentional exception for which the sample explicitly chooses to continue.
/// </summary>
public sealed class SampleUiException : Exception
{
    public SampleUiException() : base("Intentional infrastructure sample exception.")
    {
    }
}
