// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.WinForms;

namespace System.Windows.Forms.Tests;

/// <summary>
///  Exercises only the internal shared lifecycle prototype on caller-owned STA threads.
/// </summary>
[Collection("Sequential")]
public class WinFormsApplicationFeaturesTests
{
    [Fact]
    public void Build_SnapshotsTokensAndCallbacksWithoutReparsingConfiguration()
    {
        RunOnSta(() =>
        {
            string[] arguments = [@"C:\quoted path\file.txt", "", "日本語", "--raw=value", "/nosplash"];
            string[] expected = [.. arguments];
            WinFormsApplicationFeatures? retained = null;
            int calls = 0;
            WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder()
                .UseApplicationContext(() => throw new InvalidOperationException("Canceled startup must skip UI."))
                .UseHost(new HostBuilder().ConfigureAppConfiguration(configuration =>
                    configuration.AddInMemoryCollection([new("explicit", "host-only")])))
                .ConfigureApplicationFeatures(features =>
                {
                    retained = features;
                    features.CommandLine = arguments;
                    features.Starting += (sender, e) =>
                    {
                        var application = (WinFormsApplication)sender!;
                        e.CommandLine.Should().Equal(expected);
                        Action mutate = () => ((IList<string>)e.CommandLine)[0] = "changed";
                        mutate.Should().Throw<NotSupportedException>();
                        var configuration = application.Host!.Services.GetRequiredService<IConfiguration>();
                        configuration["explicit"].Should().Be("host-only");
                        configuration["raw"].Should().BeNull();
                        calls++;
                        e.Cancel = true;
                    };
                });
            using WinFormsApplication application = builder.Build();
            arguments[0] = "changed";
            retained!.Starting = (_, _) => throw new InvalidOperationException("Build must freeze callbacks.");
            retained.CommandLine = ["replacement"];

            application.Run();

            calls.Should().Be(1);
            Assert.Throws<InvalidOperationException>(() => builder.ConfigureApplicationFeatures(_ => { }));
        });
    }

    [Fact]
    public void Build_RejectsNullTokensAndDuplicateConfiguration()
    {
        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder()
            .UseStartupForm<Form>()
            .ConfigureApplicationFeatures(features => features.CommandLine = [null!]);

        Assert.Throws<InvalidOperationException>(() => builder.ConfigureApplicationFeatures(_ => { }));
        Assert.Throws<ArgumentNullException>(() => builder.Build());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Run_StartingCancellation_DrainsHostWithoutActivatingUi(bool cancel, bool componentManager)
    {
        using AppContextSwitchScope scope = new("Switch.System.Windows.Forms.EnableMsoComponentManager", componentManager);
        RunOnSta(() =>
        {
            List<string> events = [];
            int uiThread = Environment.CurrentManagedThreadId;
            CallbackService service = new(start: async () =>
            {
                await Task.Yield();
                events.Add("host-start");
            }, stop: () =>
            {
                events.Add("host-stop");
                return Task.CompletedTask;
            });
            WinFormsApplication? application = null;
            using (application = WinFormsApplication.CreateBuilder()
                .UseHost(new HostBuilder().ConfigureServices(services =>
                    services.AddSingleton<IHostedService>(_ => service)))
                .UseApplicationContext(() =>
                {
                    events.Add("activate");
                    return new ApplicationContext();
                })
                .ConfigureApplicationFeatures(features =>
                {
                    features.Starting += (sender, e) =>
                    {
                        sender.Should().BeSameAs(application);
                        Environment.CurrentManagedThreadId.Should().Be(uiThread);
                        SynchronizationContext.Current.Should().BeOfType<WindowsFormsSynchronizationContext>();
                        Application.MessageLoop.Should().BeFalse();
                        e.CommandLine.Should().Equal(Environment.GetCommandLineArgs().Skip(1));
                        Assert.Throws<InvalidOperationException>(() => application!.Run(TestContext.Current.CancellationToken));
                        Assert.Throws<InvalidOperationException>(() => application!.Dispose());
                        events.Add("starting-1");
                        e.Cancel = true;
                    };
                    features.Starting += (_, e) =>
                    {
                        events.Add("starting-2");
                        e.Cancel = cancel;
                    };
                    features.Shutdown += (_, _) => throw new InvalidOperationException("No pump was entered.");
                    features.PrepareSettingsSave = _ => throw new InvalidOperationException("No eligible shutdown.");
                })
                .Build())
            {
                application.Lifetime.ApplicationStarted += (_, _) =>
                {
                    events.Add("started");
                    _ = application.StopAsync();
                };
                application.Lifetime.ApplicationStopping += (_, _) => events.Add("stopping");
                application.Lifetime.ApplicationStopped += (_, _) => events.Add("stopped");

                application.Run();
            }

            events.Should().Equal(cancel
                ? ["host-start", "starting-1", "starting-2", "stopping", "host-stop", "stopped"]
                : ["host-start", "starting-1", "starting-2", "activate", "started", "stopping", "host-stop", "stopped"]);
            service.StopCount.Should().Be(1);
            service.DisposeCount.Should().Be(1);
        });
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void Run_NormalShutdown_OrdersCallbacksAndJoinsSettingsBeforeHostStop(int source, bool componentManager)
    {
        using AppContextSwitchScope scope = new("Switch.System.Windows.Forms.EnableMsoComponentManager", componentManager);
        RunOnSta(() =>
        {
            using CancellationTokenSource cancellation = new();
            List<string> events = [];
            int uiThread = Environment.CurrentManagedThreadId;
            TrackingContext? context = null;
            Task? stop = null;
            TaskCompletionSource saveEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource releaseSave = new(TaskCreationOptions.RunContinuationsAsynchronously);
            CallbackService service = new(stop: () =>
            {
                events.Add("host-stop");
                return Task.CompletedTask;
            });
            WinFormsApplication? application = null;
            using (application = WinFormsApplication.CreateBuilder()
                .UseHost(new HostBuilder().ConfigureServices(services =>
                    services.AddSingleton<IHostedService>(_ => service)))
                .UseApplicationContext(() => context = new TrackingContext(() => events.Add("dispose-ui")))
                .ConfigureApplicationFeatures(features =>
                {
                    features.Starting += (_, _) => events.Add("starting");
                    features.Shutdown += (sender, e) =>
                    {
                        sender.Should().BeSameAs(application);
                        Environment.CurrentManagedThreadId.Should().Be(uiThread);
                        SynchronizationContext.Current.Should().BeOfType<WindowsFormsSynchronizationContext>();
                        context!.DisposeCount.Should().Be(0);
                        e.Reason.Should().Be(source switch
                        {
                            0 => WinFormsShutdownReason.UserExit,
                            2 => WinFormsShutdownReason.RunTokenCancellation,
                            _ => WinFormsShutdownReason.RequestedStop
                        });
                        events.Add("shutdown");
                        // Reentrant committed stop must neither rerun shutdown nor change its reason.
                        stop ??= application!.StopAsync();
                    };
                    features.PrepareSettingsSave = owner =>
                    {
                        Environment.CurrentManagedThreadId.Should().Be(uiThread);
                        context!.DisposeCount.Should().Be(0);
                        events.Add("prepare");
                        CallbackService selected = owner.Host!.Services.GetServices<IHostedService>()
                            .OfType<CallbackService>().Single();
                        return async () =>
                        {
                            Environment.CurrentManagedThreadId.Should().NotBe(uiThread);
                            SynchronizationContext.Current.Should().BeNull();
                            context.DisposeCount.Should().Be(1);
                            selected.DisposeCount.Should().Be(0);
                            events.Add("save");
                            saveEntered.TrySetResult();
                            await releaseSave.Task;
                            selected.StopCount.Should().Be(0);
                            events.Add("saved");
                        };
                    };
                })
                .Build())
            {
                application.Lifetime.ApplicationStarted += (_, _) =>
                    SynchronizationContext.Current!.Post(_ =>
                    {
                        switch (source)
                        {
                            case 0:
                                context!.ExitThread();
                                break;
                            case 1:
                                stop = application.StopAsync();
                                break;
                            case 2:
                                cancellation.Cancel();
                                break;
                            default:
                                stop = application.Host!.StopAsync();
                                break;
                        }
                    }, null);
                application.Lifetime.ApplicationStopping += (_, _) => events.Add("stopping");
                application.Lifetime.ApplicationStopped += (_, _) => events.Add("stopped");
                Task observer = Task.Run(async () =>
                {
                    try
                    {
                        await saveEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
                        application.UiCompletion.IsCompleted.Should().BeTrue();
                        service.StopCount.Should().Be(0);
                        service.DisposeCount.Should().Be(0);
                    }
                    finally
                    {
                        releaseSave.TrySetResult();
                    }
                });

                if (source == 2)
                {
                    Assert.Throws<OperationCanceledException>(() => application.Run(cancellation.Token));
                    Assert.Throws<OperationCanceledException>(() => stop!.GetAwaiter().GetResult());
                }
                else
                {
                    application.Run();
                    stop!.GetAwaiter().GetResult();
                }

                observer.GetAwaiter().GetResult();
            }

            events.Should().Equal("starting", "stopping", "shutdown", "prepare", "dispose-ui",
                "save", "saved", "host-stop", "stopped");
            context!.DisposeCount.Should().Be(1);
            service.StopCount.Should().Be(1);
            service.DisposeCount.Should().Be(1);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Run_StartupFailures_ReportOriginalOnOwnerThreadAndSkipShutdown(int source)
    {
        RunOnSta(() =>
        {
            InvalidOperationException original = new("Startup failure.");
            InvalidOperationException reporting = new("Reporter failure.");
            InvalidOperationException stopping = new("Host stop failure.");
            int uiThread = Environment.CurrentManagedThreadId;
            int reports = 0;
            CallbackService service = new(
                start: () => source == 0 ? Task.FromException(original) : Task.CompletedTask,
                stop: () => Task.FromException(stopping));
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseHost(new HostBuilder().ConfigureServices(services =>
                    services.AddSingleton<IHostedService>(_ => service)))
                .UseApplicationContext(() => throw original)
                .ConfigureApplicationFeatures(features =>
                {
                    features.Starting += (_, _) =>
                    {
                        if (source == 1)
                        {
                            throw original;
                        }
                    };
                    features.Starting += (_, _) => source.Should().Be(2);
                    features.Shutdown += (_, _) => throw new InvalidOperationException("Not eligible.");
                    features.ReportFailure = (failure, phase) =>
                    {
                        Environment.CurrentManagedThreadId.Should().Be(uiThread);
                        failure.Should().BeSameAs(original);
                        phase.Should().Be(WinFormsLifecyclePhase.Startup);
                        reports++;
                        throw reporting;
                    };
                })
                .Build();

            AggregateException failure = Assert.Throws<AggregateException>(() => application.Run(TestContext.Current.CancellationToken));

            failure.Flatten().InnerExceptions.Should().Equal(original, reporting, stopping);
            reports.Should().Be(1);
            service.StopCount.Should().Be(1);
            service.DisposeCount.Should().Be(1);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Run_ShutdownOrSaveFailure_StillStopsHostAndPreservesFailure(int source)
    {
        RunOnSta(() =>
        {
            InvalidOperationException original = new("Shutdown or persistence failure.");
            int saves = 0;
            int reports = 0;
            TrackingContext? context = null;
            CallbackService service = new();
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseHost(new HostBuilder().ConfigureServices(services =>
                    services.AddSingleton<IHostedService>(_ => service)))
                .UseApplicationContext(() => context = new TrackingContext())
                .ConfigureApplicationFeatures(features =>
                {
                    features.Shutdown += (_, _) =>
                    {
                        if (source == 0)
                        {
                            throw original;
                        }
                    };
                    features.Shutdown += (_, _) => source.Should().NotBe(0);
                    features.PrepareSettingsSave = _ =>
                    {
                        if (source == 1)
                        {
                            throw original;
                        }

                        return async () =>
                        {
                            await Task.Yield();
                            saves++;
                            throw original;
                        };
                    };
                    features.ReportFailure = (failure, phase) =>
                    {
                        failure.Should().BeSameAs(original);
                        phase.Should().Be(WinFormsLifecyclePhase.Shutdown);
                        reports++;
                    };
                })
                .Build();
            application.Lifetime.ApplicationStarted += (_, _) =>
                SynchronizationContext.Current!.Post(_ => context!.ExitThread(), null);

            Assert.Throws<InvalidOperationException>(() => application.Run(TestContext.Current.CancellationToken)).Should().BeSameAs(original);

            saves.Should().Be(source == 2 ? 1 : 0);
            reports.Should().Be(source == 2 ? 0 : 1);
            context!.DisposeCount.Should().Be(1);
            service.StopCount.Should().Be(1);
            service.DisposeCount.Should().Be(1);
        });
    }

    [Fact]
    public void Run_ExistingUiExceptionAdapter_HandlesDispatchWithoutDuplicatedPolicy()
    {
        RunOnSta(() =>
        {
            InvalidOperationException expected = new("Recoverable routed UI exception.");
            int handled = 0;
            int shutdown = 0;
            ApplicationContext? context = null;
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseApplicationContext(() => context = new ApplicationContext())
                .ConfigureApplicationFeatures(features =>
                {
                    features.Shutdown += (_, _) => shutdown++;
                    features.ReportFailure = (_, _) => throw new InvalidOperationException("Already handled.");
                })
                .Build();
            using WinFormsExceptionLogging logging = WinFormsExceptionLogging.Attach(application, NullLogger.Instance,
                (_, e) =>
                {
                    e.Exception.Should().BeSameAs(expected);
                    handled++;
                    context!.ExitThread();
                }, _ => throw new InvalidOperationException("Unexpected diagnostic failure."));
            application.Lifetime.ApplicationStarted += (_, _) =>
                SynchronizationContext.Current!.Post(_ => Application.OnThreadException(expected), null);

            application.Run();

            handled.Should().Be(1);
            shutdown.Should().Be(1);
            logging.Dispose();
            logging.DiagnosticCompletion.GetAwaiter().GetResult();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_CloseVeto_IsNotShutdownButCommittedStopCannotBeVetoed(bool componentManager)
    {
        using AppContextSwitchScope scope = new("Switch.System.Windows.Forms.EnableMsoComponentManager", componentManager);
        RunOnSta(() =>
        {
            int shutdown = 0;
            int stopping = 0;
            Task? stop = null;
            Form form = new();
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseStartupForm(form)
                .ConfigureApplicationFeatures(features => features.Shutdown += (_, _) => shutdown++)
                .Build();
            form.FormClosing += (_, e) => e.Cancel = true;
            application.Lifetime.ApplicationStopping += (_, _) => stopping++;
            form.Shown += (_, _) => form.BeginInvoke(() =>
            {
                form.Close();
                shutdown.Should().Be(0);
                stopping.Should().Be(0);
                form.IsDisposed.Should().BeFalse();
                stop = application.StopAsync();
            });

            application.Run();
            stop!.GetAwaiter().GetResult();

            shutdown.Should().Be(1);
            stopping.Should().Be(1);
            form.IsDisposed.Should().BeTrue();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_SettingsSave_IsExplicitAndUsesExistingTypedService(bool enableSave)
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("WinFormsLifecycle14975-");
        try
        {
            RunOnSta(() =>
            {
                int prepared = 0;
                ApplicationContext? context = null;
                using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                    .UseHost(new HostBuilder().ConfigureServices(services =>
                        services.AddJsonUserSettings("lifecycle-product", 1, SettingsJsonContext.Default.SettingsModel,
                            () => new SettingsModel(), options => options.StorageDirectory = directory.FullName)))
                    .UseApplicationContext(() => context = new ApplicationContext())
                    .ConfigureApplicationFeatures(features =>
                    {
                        if (enableSave)
                        {
                            features.PrepareSettingsSave = owner =>
                            {
                                prepared++;
                                var settings = owner.Host!.Services.GetRequiredService<IUserSettingsService<SettingsModel>>();
                                SettingsModel snapshot = new() { Value = 42 };
                                return async () =>
                                {
                                    UserSettingsCommit commit = await settings.SaveAsync(snapshot);
                                    // Notification completion is explicitly observed outside any Changed handler.
                                    await commit.NotificationCompletion;
                                };
                            };
                        }
                    })
                    .Build();
                application.Lifetime.ApplicationStarted += (_, _) =>
                    SynchronizationContext.Current!.Post(_ => context!.ExitThread(), null);

                application.Run();

                prepared.Should().Be(enableSave ? 1 : 0);
                string path = Path.Combine(directory.FullName, "settings.json");
                File.Exists(path).Should().Be(enableSave);
                if (enableSave)
                {
                    File.ReadAllText(path).Should().Contain("\"Value\":42");
                }
            });
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_CanceledStartup_DisposesTransferredFormAndJoinsReentrantStop(bool requestStop)
    {
        RunOnSta(() =>
        {
            int disposals = 0;
            int started = 0;
            Task? stop = null;
            Form form = new();
            form.Disposed += (_, _) => disposals++;
            WinFormsApplication? application = null;
            using (application = WinFormsApplication.CreateBuilder()
                .UseStartupForm(form)
                .ConfigureApplicationFeatures(features =>
                {
                    features.Starting += (_, e) =>
                    {
                        if (requestStop)
                        {
                            stop = application!.StopAsync();
                        }
                        else
                        {
                            e.Cancel = true;
                        }
                    };
                    features.Shutdown += (_, _) => throw new InvalidOperationException("Startup was canceled.");
                })
                .Build())
            {
                application.Lifetime.ApplicationStarted += (_, _) => started++;
                application.Run();
                stop?.GetAwaiter().GetResult();
            }

            disposals.Should().Be(1);
            started.Should().Be(0);
        });
    }

    [Fact]
    public void Run_HostlessSave_JoinsUiFreeCleanupWithoutOwningBorrowedResources()
    {
        RunOnSta(() =>
        {
            int uiThread = Environment.CurrentManagedThreadId;
            int saved = 0;
            TrackingContext? context = null;
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseApplicationContext(() => context = new TrackingContext())
                .ConfigureApplicationFeatures(features => features.PrepareSettingsSave = _ =>
                {
                    context!.DisposeCount.Should().Be(0);
                    return async () =>
                    {
                        await Task.Yield();
                        Environment.CurrentManagedThreadId.Should().NotBe(uiThread);
                        SynchronizationContext.Current.Should().BeNull();
                        context.DisposeCount.Should().Be(1);
                        saved++;
                    };
                })
                .Build();
            application.Lifetime.ApplicationStarted += (_, _) =>
                SynchronizationContext.Current!.Post(_ => context!.ExitThread(), null);

            application.Run();

            saved.Should().Be(1);
            application.Host.Should().BeNull();
            context!.DisposeCount.Should().Be(1);
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Run_StopAndCancellationRace_FirstReasonWinsAndSaveFailurePrecedesCancellation(bool cancelFirst, bool failSave)
    {
        RunOnSta(() =>
        {
            using CancellationTokenSource cancellation = new();
            InvalidOperationException expected = new("Save failed during canceled shutdown.");
            int shutdown = 0;
            Task? stop = null;
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseApplicationContext(() => new ApplicationContext())
                .ConfigureApplicationFeatures(features =>
                {
                    features.Shutdown += (_, e) =>
                    {
                        shutdown++;
                        e.Reason.Should().Be(cancelFirst
                            ? WinFormsShutdownReason.RunTokenCancellation
                            : WinFormsShutdownReason.RequestedStop);
                    };
                    features.PrepareSettingsSave = _ => () =>
                        failSave ? Task.FromException(expected) : Task.CompletedTask;
                })
                .Build();
            application.Lifetime.ApplicationStarted += (_, _) =>
                SynchronizationContext.Current!.Post(_ =>
                {
                    if (cancelFirst)
                    {
                        cancellation.Cancel();
                    }

                    stop = application.StopAsync();
                    cancellation.Cancel();
                }, null);

            if (failSave)
            {
                Assert.Throws<InvalidOperationException>(() => application.Run(cancellation.Token)).Should().BeSameAs(expected);
                Assert.Throws<InvalidOperationException>(() => stop!.GetAwaiter().GetResult()).Should().BeSameAs(expected);
            }
            else
            {
                Assert.Throws<OperationCanceledException>(() => application.Run(cancellation.Token));
                Assert.Throws<OperationCanceledException>(() => stop!.GetAwaiter().GetResult());
            }

            shutdown.Should().Be(1);
        });
    }

    [Fact]
    public void Run_ManagedPumpFailure_PrecedesCleanupFailureAndIsReportedOnlyOnce()
    {
        using AppContextSwitchScope scope = new("Switch.System.Windows.Forms.EnableMsoComponentManager", enable: false);
        RunOnSta(() =>
        {
            InvalidOperationException original = new("Managed pump failure.");
            InvalidOperationException cleanup = new("Context cleanup failure.");
            ThrowingMessageFilter filter = new(original);
            int reports = 0;
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseApplicationContext(() => new TrackingContext(() => throw cleanup))
                .ConfigureApplicationFeatures(features =>
                {
                    features.Shutdown += (_, _) => throw new InvalidOperationException("Pump failed.");
                    features.ReportFailure = (failure, phase) =>
                    {
                        failure.Should().BeSameAs(original);
                        phase.Should().Be(WinFormsLifecyclePhase.MessageLoop);
                        reports++;
                    };
                })
                .Build();
            application.Lifetime.ApplicationStarted += (_, _) =>
            {
                Application.AddMessageFilter(filter);
                SynchronizationContext.Current!.Post(_ => { }, null);
            };
            try
            {
                AggregateException failure = Assert.Throws<AggregateException>(() => application.Run(TestContext.Current.CancellationToken));
                failure.InnerExceptions.Should().Equal(original, cleanup);
                reports.Should().Be(1);
            }
            finally
            {
                Application.RemoveMessageFilter(filter);
            }
        });
    }

    /// <summary>
    ///  Fails in managed message filtering, not across a native callback boundary.
    /// </summary>
    private sealed class ThrowingMessageFilter(Exception failure) : IMessageFilter
    {
        public bool PreFilterMessage(ref Message m) => throw failure;
    }

    private static void RunOnSta(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(60)).Should().BeTrue("the owned STA must drain shutdown");
        failure?.Throw();
    }

    /// <summary>
    ///  Counts root context disposal without relying on a Form that closes and disposes itself.
    /// </summary>
    private sealed class TrackingContext(Action? onDispose = null) : ApplicationContext
    {
        internal int DisposeCount { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCount++;
                onDispose?.Invoke();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    ///  Exercises asynchronous host participation and records provider-owned disposal.
    /// </summary>
    private sealed class CallbackService(Func<Task>? start = null, Func<Task>? stop = null) : IHostedService, IDisposable
    {
        internal int StopCount { get; private set; }
        internal int DisposeCount { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
            => start?.Invoke() ?? Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            return stop?.Invoke() ?? Task.CompletedTask;
        }

        public void Dispose() => DisposeCount++;
    }
}
