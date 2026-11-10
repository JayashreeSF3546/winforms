// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.WinForms;
using Microsoft.VisualBasic.ApplicationServices;

namespace ApplicationBuilderParity.CSharp;

/// <summary>
///  Composes real compiled framework scenarios in C# on a dedicated process STA.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Probe probe = new();
            string scenario = args[0];
            string[] tokens = args.Skip(1).ToArray();
            if (scenario.StartsWith("legacy-", StringComparison.Ordinal))
            {
                new LegacyApplication(probe, scenario).Run(tokens);
                probe.Ui("returned");
            }
            else if (scenario.StartsWith("ipc-", StringComparison.Ordinal))
            {
                RunIpc(probe, scenario, tokens);
            }
            else
            {
                RunShared(probe, scenario, tokens);
            }

            probe.Finish();
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void RunShared(Probe probe, string scenario, string[] tokens)
    {
        NetworkSource network = new();
        GateClock clock = new();
        FaultService fault = new(probe);
        WinFormsSplashScreen? splash = null;
        Form? main = null;
        WinFormsApplication? app = null;
        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder()
            .UseHost(new HostBuilder().ConfigureServices(services =>
            {
                services.AddSingleton<IHostedService>(_ => new DrainService(probe));
                if (scenario == "background-failure")
                {
                    services.AddSingleton<IHostedService>(_ => fault);
                }
            }))
            .UseApplicationContext(() =>
            {
                probe.Ui("activate");
                main = new Form { ShowInTaskbar = false };
                main.Shown += (_, _) => probe.Guard(() =>
                {
                    probe.Ui("shown");
                    if (scenario == "splash-gate")
                    {
                        Probe.Require(clock.GetTimestamp() == TimeSpan.TicksPerSecond * 2, "Early main window.");
                        Probe.Require(splash!.IsDisposed, "Splash still owned.");
                    }

                    if (scenario == "network")
                    {
                        _ = Task.Run(network.Raise);
                    }
                    else if (scenario == "ui-exception")
                    {
                        // Managed routing stimulus: do not throw across an unmanaged window callback.
                        probe.Post(() => Application.OnThreadException(probe.Failure));
                    }
                    else if (scenario == "background-failure")
                    {
                        fault.Release.TrySetResult();
                    }
                    else
                    {
                        main.Close();
                    }
                });
                return new ApplicationContext(main);
            })
            .ConfigureApplicationFeatures(features =>
            {
                features.CommandLine = tokens;
                features.Starting = (_, e) =>
                {
                    probe.Ui("starting");
                    Probe.Require(!Application.MessageLoop, "Startup entered the root pump.");
                    probe.Tokens(e.CommandLine);
                    e.Cancel = scenario == "cancel";
                    if (scenario == "startup-failure")
                    {
                        throw probe.Failure;
                    }
                };
                features.Shutdown = (_, _) =>
                {
                    Probe.Require(SynchronizationContext.Current is WindowsFormsSynchronizationContext,
                        "Shared shutdown must retain owner context.");
                    probe.Ui("shutdown");
                };
                features.ReportFailure = (exception, phase) =>
                {
                    Probe.Require(ReferenceEquals(exception, probe.Failure), "Lost original failure.");
                    probe.Ui($"report:{phase}");
                };
                features.PrepareSettingsSave = _ =>
                {
                    probe.Ui("prepare");
                    return async () =>
                    {
                        Probe.Require(main!.IsDisposed, "Save preceded UI cleanup.");
                        await Task.Yield();
                        probe.Add("save");
                        if (scenario == "settings-failure")
                        {
                            throw probe.Failure;
                        }

                        probe.Add("saved");
                    };
                };
                if (scenario == "network")
                {
                    features.NetworkSource = network;
                    features.NetworkAvailabilityChanged = available =>
                    {
                        Probe.Require(available, "Wrong network transition.");
                        probe.Ui("network");
                        _ = app!.StopAsync();
                    };
                }

                if (scenario.StartsWith("splash-", StringComparison.Ordinal))
                {
                    features.SplashScreenFactory = () => splash = new WinFormsSplashScreen();
                    features.SplashTimeProvider = clock;
                    features.MinimumSplashDisplayTime = TimeSpan.FromSeconds(2);
                }
            });
        using (app = builder.Build())
        {
            using WinFormsExceptionLogging logging = WinFormsExceptionLogging.Attach(app, NullLogger.Instance,
                (_, e) =>
                {
                    Probe.Require(ReferenceEquals(e.Exception, probe.Failure), "Lost UI exception.");
                    probe.Ui("ui-handled");
                    main!.Close();
                }, _ => probe.Add("logging-failure"));
            tokens[0] = "mutated after Build";
            app.Lifetime.ApplicationStarted += (_, _) =>
            {
                probe.Ui("started");
                if (scenario.StartsWith("splash-", StringComparison.Ordinal))
                {
                    probe.Post(() =>
                    {
                        Probe.Require(splash!.Visible && !main!.Visible, "Missing splash gate.");
                        probe.Ui("gated");
                        if (scenario == "splash-stop")
                        {
                            _ = app.StopAsync();
                            return;
                        }

                        clock.Advance();
                        probe.Post(() =>
                        {
                            Probe.Require(!main!.Visible, "Early timer bypassed minimum.");
                            probe.Ui("still-gated");
                            clock.Advance();
                        });
                    });
                }
            };
            app.Lifetime.ApplicationStopped += (_, _) => probe.Ui("stopped");
            try
            {
                app.Run();
                Probe.Require(scenario is not ("startup-failure" or "settings-failure"), "Failure was swallowed.");
            }
            catch (Exception exception) when (ReferenceEquals(exception, probe.Failure))
            {
                probe.Ui("caught-original");
            }
        }

        Probe.Require(network.Subscriptions == 0, "Network subscription leaked.");
        if (splash is not null)
        {
            Probe.Require(splash.IsDisposed && clock.TimerDisposed, "Splash/timer leaked.");
            probe.Ui("splash-clean");
        }

        probe.Ui("returned");
    }

    private static void RunIpc(Probe probe, string scenario, string[] tokens)
    {
        WinFormsSingleInstanceIdentity identity = WinFormsSingleInstanceIdentity.Create(tokens[0], "14978");
        WinFormsSingleInstanceSelection selection = WinFormsSingleInstance.SelectAsync(
            identity, tokens.Skip(1), false, TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        probe.Add(selection.Result.ToString());
        if (selection.Primary is null)
        {
            return;
        }

        using WinFormsSingleInstance primary = selection.Primary;
        if (scenario is "ipc-secondary" or "ipc-notready")
        {
            if (scenario == "ipc-notready")
            {
                Console.WriteLine("READY");
                Console.ReadLine();
            }

            return;
        }

        WinFormsApplication? app = null;
        using (app = WinFormsApplication.CreateBuilder().UseStartupForm<Form>()
            .UseSingleInstance(primary, (_, request) =>
            {
                probe.Ui("next");
                probe.Tokens(request.CommandLine);
                request.BringToForeground = false;
                if (scenario == "ipc-failure")
                {
                    throw probe.Failure;
                }
            }).Build())
        {
            app.Lifetime.ApplicationStarted += (_, _) => probe.Post(() =>
            {
                Console.WriteLine("READY");
                _ = Task.Run(async () =>
                {
                    await Console.In.ReadLineAsync();
                    await app.StopAsync();
                });
            });
            app.Run();
        }
    }

    /// <summary>
    ///  Consumes the unchanged public VB framework from C#, without a migration adapter.
    /// </summary>
    private sealed class LegacyApplication : WindowsFormsApplicationBase
    {
        private readonly Probe _probe;

        internal LegacyApplication(Probe probe, string scenario)
            : base(AuthenticationMode.ApplicationDefined)
        {
            _probe = probe;
            EnableVisualStyles = false;
            SaveMySettingsOnExit = false;
            Startup += (_, e) =>
            {
                probe.Ui("starting");
                probe.Tokens(e.CommandLine);
                e.Cancel = scenario == "legacy-cancel";
                if (scenario == "legacy-failure")
                {
                    throw probe.Failure;
                }
            };
            Shutdown += (_, _) => probe.Ui("shutdown");
            UnhandledException += (_, e) =>
            {
                Probe.Require(ReferenceEquals(e.Exception, probe.Failure), "Lost legacy failure.");
                probe.Ui("legacy-handled");
                e.ExitApplication = false;
            };
        }

        protected override void OnCreateMainForm()
        {
            _probe.Ui("activate");
            MainForm = new Form { ShowInTaskbar = false };
            MainForm.Shown += (_, _) => _probe.Guard(() =>
            {
                _probe.Ui("shown");
                MainForm.Close();
            });
        }
    }
}
