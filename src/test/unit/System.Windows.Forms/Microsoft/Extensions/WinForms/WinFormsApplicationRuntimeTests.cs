// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.WinForms;

namespace System.Windows.Forms.Tests;

/// <summary>
///  Exercises the runner on dedicated caller-owned threads without sleeps or polling.
/// </summary>
[Collection("Sequential")]
public class WinFormsApplicationRuntimeTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void Run_StartupSelection_RaisesOrderedEventsAndDisposesUi(int selection, bool componentManager)
    {
        using AppContextSwitchScope scope = new("Switch.System.Windows.Forms.EnableMsoComponentManager", componentManager);
        RunOnThread(() =>
        {
            List<string> events = [];
            Form? form = null;
            WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder();
            switch (selection)
            {
                case 0:
                    builder.UseStartupForm<Form>();
                    break;
                case 1:
                    form = new Form();
                    builder.UseStartupForm(form);
                    break;
                default:
                    builder.UseApplicationContext(() =>
                    {
                        SynchronizationContext.Current.Should().BeOfType<WindowsFormsSynchronizationContext>();
                        form = new Form();
                        return new ApplicationContext(form);
                    });
                    break;
            }

            using WinFormsApplication application = builder.Build();
            application.Lifetime.ApplicationStarted += (_, _) =>
            {
                SynchronizationContext.Current.Should().BeOfType<WindowsFormsSynchronizationContext>();
                Application.MessageLoop.Should().BeFalse();
                events.Add("started");
                form = application.StartupContext!.MainForm!;
                form.Disposed += (_, _) => events.Add("disposed");
                form.Shown += (_, _) =>
                {
                    events.Add("shown");
                    form.BeginInvoke(form.Close);
                };
            };
            application.Lifetime.ApplicationStopping += (_, _) => events.Add("stopping");
            application.Lifetime.ApplicationStopped += (_, _) =>
            {
                form!.IsDisposed.Should().BeTrue();
                Application.MessageLoop.Should().BeFalse();
                events.Add("stopped");
            };
            SynchronizationContext? previousContext = SynchronizationContext.Current;

            application.Run();

            events.Should().Equal("started", "shown", "stopping", "disposed", "stopped");
            SynchronizationContext.Current.Should().BeSameAs(previousContext);
            WindowsFormsSynchronizationContext.AutoInstall.Should().BeTrue();
            Assert.Throws<ObjectDisposedException>(() => application.Run(TestContext.Current.CancellationToken));
            (previousContext as IDisposable)?.Dispose();
        });
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void Run_StartupFailure_CleansUpAndPreservesLegacySequentialRuns(int failureKind, bool componentManager)
    {
        using AppContextSwitchScope scope = new("Switch.System.Windows.Forms.EnableMsoComponentManager", componentManager);
        RunOnThread(() =>
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            InvalidOperationException expected = new("Startup failed.");
            Form? form = null;
            int started = 0;
            List<string> events = [];
            using WinFormsApplication application = WinFormsApplication.CreateBuilder().UseApplicationContext(() =>
            {
                if (failureKind == 0)
                {
                    throw expected;
                }

                form = new Form();
                form.CreateControl(ignoreVisible: true);
                form.Load += (_, _) => throw expected;
                return new ApplicationContext(form);
            }).Build();
            application.Lifetime.ApplicationStarted += (_, _) => started++;
            application.Lifetime.ApplicationStopping += (_, _) => events.Add("stopping");
            application.Lifetime.ApplicationStopped += (_, _) => events.Add("stopped");

            Assert.Throws<InvalidOperationException>(() => application.Run(TestContext.Current.CancellationToken)).Should().BeSameAs(expected);

            started.Should().Be(failureKind);
            events.Should().Equal("stopping", "stopped");
            form?.IsDisposed.Should().BeTrue();

            SynchronizationContext.Current.Should().BeNull();
            RunLegacyForm();
        });
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    public void Run_ShutdownSources_NotifyBeforeCleanupAndExitOnce(int source, bool componentManager)
    {
        using AppContextSwitchScope scope = new("Switch.System.Windows.Forms.EnableMsoComponentManager", componentManager);
        RunOnThread(() =>
        {
            TrackingContext context = new();
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseApplicationContext(() => context)
                .Build();
            Task? stop = null;
            List<string> events = [];
            application.Lifetime.ApplicationStarted += (_, _) =>
            {
                events.Add("started");
                SynchronizationContext.Current!.Post(_ =>
                {
                    switch (source)
                    {
                        case 0:
                            stop = application.StopAsync();
                            break;
                        case 1:
                            stop = Task.Run(() => application.StopAsync());
                            break;
                        case 2:
                            Application.Exit();
                            break;
                        case 3:
                            context.ExitThread();
                            break;
                        default:
                            PInvoke.PostQuitMessage(0);
                            break;
                    }
                }, null);
            };
            application.Lifetime.ApplicationStopping += (_, _) =>
            {
                Application.MessageLoop.Should().BeTrue();
                context.DisposeCount.Should().Be(0);
                events.Add("stopping");
            };
            application.Lifetime.ApplicationStopped += (_, _) =>
            {
                context.DisposeCount.Should().Be(1);
                events.Add("stopped");
            };

            application.Run();
            stop?.GetAwaiter().GetResult();
            application.StopAsync().GetAwaiter().GetResult();

            events.Should().Equal("started", "stopping", "stopped");
            context.DisposeCount.Should().Be(1);
            RunLegacyForm();
        });
    }

    [Fact]
    public void StopAsync_StartedHandler_CompletesWithoutEnteringMessageLoop()
    {
        RunOnThread(() =>
        {
            TrackingContext context = new();
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseApplicationContext(() => context)
                .Build();
            Task? stop = null;
            List<string> events = [];
            application.Lifetime.ApplicationStarted += (_, _) =>
            {
                events.Add("started");
                stop = application.StopAsync();
            };
            application.Lifetime.ApplicationStarted += (_, _) => events.Add("second-started");
            application.Lifetime.ApplicationStopping += (_, _) =>
            {
                Application.MessageLoop.Should().BeFalse();
                events.Add("stopping");
            };
            application.Lifetime.ApplicationStopped += (_, _) => events.Add("stopped");

            application.Run();
            stop!.GetAwaiter().GetResult();

            events.Should().Equal("started", "second-started", "stopping", "stopped");
            context.DisposeCount.Should().Be(1);
        });
    }

    [Fact]
    public void Run_CustomContext_KeepsRunningAfterMainFormCloses()
    {
        RunOnThread(() =>
        {
            Form form = new();
            using KeepAliveContext context = new(form);
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseApplicationContext(() => context)
                .Build();
            int stopping = 0;
            int stoppingAfterClose = -1;
            bool loopAfterClose = false;
            application.Lifetime.ApplicationStopping += (_, _) => stopping++;
            form.Shown += (_, _) => form.BeginInvoke(() =>
            {
                form.Close();
                stoppingAfterClose = stopping;
                loopAfterClose = Application.MessageLoop;
                context.ExitThread();
            });

            application.Run();

            stoppingAfterClose.Should().Be(0);
            loopAfterClose.Should().BeTrue();
            stopping.Should().Be(1);
            form.IsDisposed.Should().BeTrue();
        });
    }

    [Fact]
    public void Run_LightThreadContextMessageFilterFailure_CleansUpAndPreservesEscapingException()
    {
        using AppContextSwitchScope scope = new("Switch.System.Windows.Forms.EnableMsoComponentManager", enable: false);
        RunOnThread(() =>
        {
            InvalidOperationException expected = new("Dispatch failed.");
            ThrowingMessageFilter filter = new(expected);
            TrackingContext context = new();
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseApplicationContext(() => context)
                .Build();
            int stopped = 0;
            application.Lifetime.ApplicationStarted += (_, _) =>
            {
                Application.AddMessageFilter(filter);
                SynchronizationContext.Current!.Post(_ => { }, null);
            };
            application.Lifetime.ApplicationStopped += (_, _) => stopped++;
            try
            {
                Assert.Throws<InvalidOperationException>(() => application.Run(TestContext.Current.CancellationToken)).Should().BeSameAs(expected);
            }
            finally
            {
                Application.RemoveMessageFilter(filter);
            }

            stopped.Should().Be(1);
            context.DisposeCount.Should().Be(1);
            RunLegacyForm();
        });
    }

    [Fact]
    public void StopAsync_CloseVeto_DoesNotVetoCommittedShutdown()
    {
        RunOnThread(() =>
        {
            Form form = new();
            int closing = 0;
            int stopping = 0;
            int stoppingAfterUserClose = -1;
            Task? stop = null;
            using WinFormsApplication application = WinFormsApplication.CreateBuilder().UseStartupForm(form).Build();
            application.Lifetime.ApplicationStopping += (_, _) => stopping++;
            form.FormClosing += (_, e) =>
            {
                closing++;
                e.Cancel = true;
            };
            form.Shown += (_, _) => form.BeginInvoke(() =>
            {
                form.Close();
                stoppingAfterUserClose = stopping;
                stop = application.StopAsync();
            });

            application.Run();
            stop!.GetAwaiter().GetResult();

            stoppingAfterUserClose.Should().Be(0);
            stopping.Should().Be(1);
            closing.Should().Be(2);
            form.IsDisposed.Should().BeTrue();
        });
    }

    [Fact]
    public void Run_AlreadyCanceled_DoesNotAcceptRunOrActivateStartup()
    {
        RunOnThread(() =>
        {
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            int activated = 0;
            int stopping = 0;
            using WinFormsApplication application = WinFormsApplication.CreateBuilder().UseApplicationContext(() =>
            {
                activated++;
                return new ApplicationContext();
            }).Build();
            application.Lifetime.ApplicationStopping += (_, _) => stopping++;

            Assert.Throws<OperationCanceledException>(() => application.Run(cancellation.Token))
                .CancellationToken.Should().Be(cancellation.Token);

            activated.Should().Be(0);
            stopping.Should().Be(0);
            SynchronizationContext.Current.Should().BeNull();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_CancellationDuringStartupOrDispatch_CleansUpBeforePropagating(bool duringDispatch)
    {
        RunOnThread(() =>
        {
            using CancellationTokenSource cancellation = new();
            TrackingContext context = new();
            List<string> events = [];
            using WinFormsApplication application = WinFormsApplication.CreateBuilder().UseApplicationContext(() =>
            {
                if (!duringDispatch)
                {
                    cancellation.Cancel();
                }

                return context;
            }).Build();
            application.Lifetime.ApplicationStarted += (_, _) =>
            {
                events.Add("started");
                SynchronizationContext.Current!.Post(_ => Task.Run(cancellation.Cancel), null);
            };
            application.Lifetime.ApplicationStopping += (_, _) => events.Add("stopping");
            application.Lifetime.ApplicationStopped += (_, _) =>
            {
                context.DisposeCount.Should().Be(1);
                events.Add("stopped");
            };

            Assert.Throws<OperationCanceledException>(() => application.Run(cancellation.Token))
                .CancellationToken.Should().Be(cancellation.Token);

            events.Should().Equal(duringDispatch
                ? ["started", "stopping", "stopped"]
                : ["stopping", "stopped"]);
            context.DisposeCount.Should().Be(1);
            SynchronizationContext.Current.Should().BeNull();
        });
    }

    [Fact]
    public void StopAsync_CanceledWait_RetainsRequestBeforeRun()
    {
        RunOnThread(() =>
        {
            int activated = 0;
            using WinFormsApplication application = WinFormsApplication.CreateBuilder().UseApplicationContext(() =>
            {
                activated++;
                return new ApplicationContext();
            }).Build();
            Task canceledWait = application.StopAsync(new CancellationToken(canceled: true));

            Assert.ThrowsAny<OperationCanceledException>(() => canceledWait.GetAwaiter().GetResult());
            application.Run();
            application.StopAsync().GetAwaiter().GetResult();
            activated.Should().Be(0);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Run_LifecycleSubscriberFailure_DoesNotPreventCleanupOrTerminalNotification(int milestone)
    {
        RunOnThread(() =>
        {
            InvalidOperationException expected = new("Lifecycle failed.");
            TrackingContext context = new();
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseApplicationContext(() => context)
                .Build();
            EventHandler fail = (_, _) => throw expected;
            switch (milestone)
            {
                case 0:
                    application.Lifetime.ApplicationStarted += fail;
                    break;
                case 1:
                    application.Lifetime.ApplicationStopping += fail;
                    break;
                default:
                    application.Lifetime.ApplicationStopped += fail;
                    break;
            }

            List<string> events = [];
            application.Lifetime.ApplicationStarted += (_, _) =>
            {
                events.Add("started");
                if (milestone != 0)
                {
                    SynchronizationContext.Current!.Post(_ => context.ExitThread(), null);
                }
            };
            application.Lifetime.ApplicationStopping += (_, _) => events.Add("stopping");
            application.Lifetime.ApplicationStopped += (_, _) => events.Add("stopped");

            Assert.Throws<InvalidOperationException>(() => application.Run(TestContext.Current.CancellationToken)).Should().BeSameAs(expected);
            Assert.Throws<InvalidOperationException>(() => application.StopAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult())
                .Should().BeSameAs(expected);
            context.DisposeCount.Should().Be(1);
            events.Should().Equal("started", "stopping", "stopped");
        });
    }

    [Fact]
    public void Run_ContextDisposalFailure_IsReportedAndDoesNotSuppressStopped()
    {
        RunOnThread(() =>
        {
            InvalidOperationException expected = new("Cleanup failed.");
            TrackingContext context = new(expected);
            int stopped = 0;
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseApplicationContext(() => context)
                .Build();
            application.Lifetime.ApplicationStarted += (_, _) =>
                SynchronizationContext.Current!.Post(_ => context.ExitThread(), null);
            application.Lifetime.ApplicationStopped += (_, _) => stopped++;

            Assert.Throws<InvalidOperationException>(() => application.Run(TestContext.Current.CancellationToken)).Should().BeSameAs(expected);

            stopped.Should().Be(1);
            context.DisposeCount.Should().Be(1);
            RunLegacyForm();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StopAsync_OpenModalDialog_UnwindsBothLoopsAndAllowsAnotherRun(bool componentManager)
    {
        using AppContextSwitchScope scope = new("Switch.System.Windows.Forms.EnableMsoComponentManager", componentManager);
        RunOnThread(() =>
        {
            Form form = new();
            using Form dialog = new();
            bool modalReturned = false;
            using WinFormsApplication application = WinFormsApplication.CreateBuilder().UseStartupForm(form).Build();
            dialog.Shown += (_, _) => dialog.BeginInvoke(() => _ = application.StopAsync());
            form.Shown += (_, _) => form.BeginInvoke(() =>
            {
                dialog.ShowDialog(form);
                modalReturned = true;
            });

            application.Run();

            modalReturned.Should().BeTrue();
            form.IsDisposed.Should().BeTrue();
            dialog.IsDisposed.Should().BeTrue();
            RunLegacyForm();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_FreshApplicationsOnSameSta_ReleaseHiddenAndDisabledWindows(bool componentManager)
    {
        using AppContextSwitchScope scope = new("Switch.System.Windows.Forms.EnableMsoComponentManager", componentManager);
        RunOnThread(() =>
        {
            for (int iteration = 0; iteration < 3; iteration++)
            {
                SynchronizationContext? previousContext = SynchronizationContext.Current;
                Form? main = null;
                Form? hidden = null;
                Form? disabled = null;
                List<HWND> handles = [];
                int disposed = 0;
                bool dispatched = false;
                using WinFormsApplication application = WinFormsApplication.CreateBuilder().UseApplicationContext(() =>
                {
                    main = new Form();
                    hidden = new Form();
                    disabled = new Form { Enabled = false };
                    foreach (Form form in new[] { main, hidden, disabled })
                    {
                        form.Disposed += (_, _) => disposed++;
                        handles.Add((HWND)form.Handle);
                    }

                    disabled.Show();
                    return new ApplicationContext(main);
                }).Build();
                application.Lifetime.ApplicationStarted += (_, _) =>
                    SynchronizationContext.Current!.Post(_ =>
                    {
                        dispatched = true;
                        application.StartupContext!.ExitThread();
                    }, null);

                application.Run();

                dispatched.Should().BeTrue();
                disposed.Should().Be(3);
                main!.IsDisposed.Should().BeTrue();
                hidden!.IsDisposed.Should().BeTrue();
                disabled!.IsDisposed.Should().BeTrue();
                handles.Should().OnlyContain(handle => !PInvoke.IsWindow(handle));
                Application.MessageLoop.Should().BeFalse();
                SynchronizationContext.Current.Should().BeSameAs(previousContext);
                RunLegacyForm();
            }
        });
    }

    [Fact]
    public void Run_StartupAndCleanupFailuresWithCancellation_PreservesFailuresAndTerminalEvents()
    {
        RunOnThread(() =>
        {
            using CancellationTokenSource cancellation = new();
            InvalidOperationException startupFailure = new("Startup notification failed.");
            ArgumentException cleanupFailure = new("Context cleanup failed.");
            TrackingContext context = new(cleanupFailure);
            List<string> events = [];
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseApplicationContext(() => context)
                .Build();
            application.Lifetime.ApplicationStarted += (_, _) =>
            {
                cancellation.Cancel();
                throw startupFailure;
            };
            application.Lifetime.ApplicationStarted += (_, _) => events.Add("started");
            application.Lifetime.ApplicationStopping += (_, _) => events.Add("stopping");
            application.Lifetime.ApplicationStopped += (_, _) => events.Add("stopped");

            AggregateException failure = Assert.Throws<AggregateException>(() => application.Run(cancellation.Token));

            failure.InnerExceptions.Should().Equal(startupFailure, cleanupFailure);
            context.DisposeCount.Should().Be(1);
            events.Should().Equal("started", "stopping", "stopped");
            Assert.Throws<AggregateException>(() => application.StopAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult())
                .Should().BeSameAs(failure);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_HandledDispatchFailure_PreservesThreadExceptionPolicy(bool componentManager)
    {
        using AppContextSwitchScope scope = new("Switch.System.Windows.Forms.EnableMsoComponentManager", componentManager);
        RunOnThread(() =>
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            InvalidOperationException expected = new("Posted work failed.");
            Exception? observed = null;
            bool dispatchedAfterFailure = false;
            int threadExceptions = 0;
            int stopped = 0;
            Form form = new();
            using WinFormsApplication application = WinFormsApplication.CreateBuilder().UseStartupForm(form).Build();
            ThreadExceptionEventHandler handler = (_, e) =>
            {
                observed = e.Exception;
                threadExceptions++;
                form.BeginInvoke(() =>
                {
                    dispatchedAfterFailure = Application.MessageLoop;
                    _ = application.StopAsync();
                });
            };
            Application.ThreadException += handler;
            application.Lifetime.ApplicationStopped += (_, _) => stopped++;
            form.Shown += (_, _) => form.BeginInvoke(() => throw expected);
            try
            {
                application.Run();
            }
            finally
            {
                Application.ThreadException -= handler;
            }

            observed.Should().BeSameAs(expected);
            dispatchedAfterFailure.Should().BeTrue();
            threadExceptions.Should().Be(1);
            stopped.Should().Be(1);
            form.IsDisposed.Should().BeTrue();
            RunLegacyForm();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_UnsupportedSynchronizationContext_RejectsWithoutMutation(bool disableAutoInstall)
    {
        RunOnThread(() =>
        {
            SynchronizationContext? previous = disableAutoInstall ? null : new CustomSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(previous);
            WindowsFormsSynchronizationContext.AutoInstall = !disableAutoInstall;
            try
            {
                int activated = 0;
                using WinFormsApplication application = WinFormsApplication.CreateBuilder().UseApplicationContext(() =>
                {
                    activated++;
                    return new ApplicationContext();
                }).Build();

                Assert.Throws<InvalidOperationException>(() => application.Run(TestContext.Current.CancellationToken));

                activated.Should().Be(0);
                SynchronizationContext.Current.Should().BeSameAs(previous);
                WindowsFormsSynchronizationContext.AutoInstall.Should().Be(!disableAutoInstall);
            }
            finally
            {
                WindowsFormsSynchronizationContext.AutoInstall = true;
                SynchronizationContext.SetSynchronizationContext(null);
            }
        });
    }

    [Fact]
    public void Run_MtaThread_RejectsBeforeActivation()
    {
        RunOnThread(() =>
        {
            int activated = 0;
            using WinFormsApplication application = WinFormsApplication.CreateBuilder().UseApplicationContext(() =>
            {
                activated++;
                return new ApplicationContext();
            }).Build();

            Assert.Throws<InvalidOperationException>(() => application.Run(TestContext.Current.CancellationToken));
            activated.Should().Be(0);
        }, ApartmentState.MTA);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_UnusableWinFormsContext_RejectsWithoutActivation(bool foreignThread)
    {
        RunOnThread(() =>
        {
            using WindowsFormsSynchronizationContext context = new();
            void VerifyRejected()
            {
                SynchronizationContext? previous = SynchronizationContext.Current;
                SynchronizationContext.SetSynchronizationContext(context);
                try
                {
                    int activated = 0;
                    using WinFormsApplication application = WinFormsApplication.CreateBuilder().UseApplicationContext(() =>
                    {
                        activated++;
                        return new ApplicationContext();
                    }).Build();

                    Assert.Throws<InvalidOperationException>(() => application.Run());

                    activated.Should().Be(0);
                    SynchronizationContext.Current.Should().BeSameAs(context);
                }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(previous);
                }
            }

            if (foreignThread)
            {
                RunOnThread(VerifyRejected);
            }
            else
            {
                context.Dispose();
                VerifyRejected();
            }
        });
    }

    [Fact]
    public void Run_ExistingWinFormsContext_RestoresUsableCallerContext()
    {
        RunOnThread(() =>
        {
            WindowsFormsSynchronizationContext.InstallIfNeeded();
            var previous = (WindowsFormsSynchronizationContext)SynchronizationContext.Current!;
            try
            {
                using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                    .UseApplicationContext(() => new ApplicationContext())
                    .Build();
                application.Lifetime.ApplicationStarted += (_, _) =>
                    SynchronizationContext.Current!.Post(_ => application.StartupContext!.ExitThread(), null);

                application.Run();

                SynchronizationContext.Current.Should().BeSameAs(previous);
                bool sent = false;
                previous.Send(_ => sent = true, null);
                sent.Should().BeTrue();
            }
            finally
            {
                WindowsFormsSynchronizationContext.Uninstall(turnOffAutoInstall: false);
                previous.Dispose();
                SynchronizationContext.SetSynchronizationContext(null);
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_InternalHostSeam_CoordinatesBothShutdownDirectionsOffUiThread(bool hostInitiated)
    {
        RunOnThread(() =>
        {
            ConcurrentQueue<string> trace = new();
            int uiThread = Environment.CurrentManagedThreadId;
            TestHost host = new(trace, uiThread);
            TrackingContext context = new();
            using WinFormsApplication application = WinFormsApplication.CreateBuilder().UseApplicationContext(() =>
            {
                trace.Enqueue("factory");
                return context;
            }).Build(host);
            host.UiCompletion = application.UiCompletion;
            application.Lifetime.ApplicationStarted += (_, _) =>
            {
                trace.Enqueue("started");
                SynchronizationContext.Current!.Post(_ =>
                {
                    if (hostInitiated)
                    {
                        Task.Run(host.RequestExternalStop);
                    }
                    else
                    {
                        context.ExitThread();
                    }
                }, null);
            };
            application.Lifetime.ApplicationStopped += (_, _) => trace.Enqueue("stopped");

            application.Run();

            string[] entries = trace.ToArray();
            Array.IndexOf(entries, "host-start").Should().BeLessThan(Array.IndexOf(entries, "factory"));
            Array.IndexOf(entries, "host-stop").Should().BeLessThan(Array.IndexOf(entries, "stopped"));
            entries[^1].Should().Be("host-dispose");
            host.RanOnUiThread.Should().BeFalse();
            host.StoppedBeforeUiCleanup.Should().BeFalse();
            host.StopCount.Should().Be(1);
            context.DisposeCount.Should().Be(1);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_InternalHostFailure_PreservesFailureAndCompletesCleanup(bool startupFailure)
    {
        RunOnThread(() =>
        {
            InvalidOperationException expected = new("Host failed.");
            ConcurrentQueue<string> trace = new();
            TestHost host = new(trace, Environment.CurrentManagedThreadId)
            {
                StartFailure = startupFailure ? expected : null,
                StopFailure = startupFailure ? null : expected
            };
            int activated = 0;
            int stopped = 0;
            using WinFormsApplication application = WinFormsApplication.CreateBuilder().UseApplicationContext(() =>
            {
                activated++;
                return new ApplicationContext();
            }).Build(host);
            host.UiCompletion = application.UiCompletion;
            application.Lifetime.ApplicationStarted += (_, _) =>
                SynchronizationContext.Current!.Post(_ => application.StartupContext!.ExitThread(), null);
            application.Lifetime.ApplicationStopped += (_, _) => stopped++;

            Assert.Throws<InvalidOperationException>(() => application.Run(TestContext.Current.CancellationToken)).Should().BeSameAs(expected);

            activated.Should().Be(startupFailure ? 0 : 1);
            stopped.Should().Be(1);
            trace.Last().Should().Be("host-dispose");
            host.StopCount.Should().Be(1);
        });
    }

    private static void RunLegacyForm()
    {
        using Form form = new();
        bool shown = false;
        bool dispatched = false;
        form.Shown += (_, _) =>
        {
            shown = true;
            form.BeginInvoke(() =>
            {
                dispatched = true;
                form.Close();
            });
        };
        Application.Run(form);
        shown.Should().BeTrue();
        dispatched.Should().BeTrue();
        Application.MessageLoop.Should().BeFalse();
    }

    private static void RunOnThread(Action action, ApartmentState apartment = ApartmentState.STA)
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
        thread.SetApartmentState(apartment);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(60)).Should().BeTrue("the caller-owned thread must finish shutdown");
        failure?.Throw();
    }

    /// <summary>
    ///  Distinguishes unsupported custom contexts from the default context.
    /// </summary>
    private sealed class CustomSynchronizationContext : SynchronizationContext
    {
    }

    /// <summary>
    ///  Keeps the root loop alive independently of its initial main form.
    /// </summary>
    private sealed class KeepAliveContext(Form form) : ApplicationContext(form)
    {
        protected override void OnMainFormClosed(object? sender, EventArgs e) => MainForm = null;
    }

    /// <summary>
    ///  Raises a failure directly from managed message dispatch.
    /// </summary>
    private sealed class ThrowingMessageFilter(Exception failure) : IMessageFilter
    {
        public bool PreFilterMessage(ref Message m) => throw failure;
    }

    /// <summary>
    ///  Tracks once-only context cleanup, including exceptional cleanup.
    /// </summary>
    private sealed class TrackingContext(Exception? disposeFailure = null) : ApplicationContext
    {
        internal int DisposeCount { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCount++;
            }

            base.Dispose(disposing);
            if (disposing && disposeFailure is not null)
            {
                throw disposeFailure;
            }
        }
    }

    /// <summary>
    ///  Exercises the internal coordination boundary without implementing Generic Host services.
    /// </summary>
    private sealed class TestHost(ConcurrentQueue<string> trace, int uiThread) : IHost, IHostApplicationLifetime, IServiceProvider
    {
        private readonly CancellationTokenSource _stopping = new();

        public IServiceProvider Services => this;

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => _stopping.Token;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public object? GetService(Type serviceType)
            => serviceType == typeof(IHostApplicationLifetime) ? this : null;

        internal Exception? StartFailure { get; init; }

        internal Exception? StopFailure { get; init; }

        internal Task UiCompletion { get; set; } = Task.CompletedTask;

        internal bool RanOnUiThread { get; private set; }

        internal bool StoppedBeforeUiCleanup { get; private set; }

        internal int StopCount { get; private set; }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await Task.Yield();
            RanOnUiThread |= Environment.CurrentManagedThreadId == uiThread;
            trace.Enqueue("host-start");
            cancellationToken.ThrowIfCancellationRequested();
            if (StartFailure is not null)
            {
                throw StartFailure;
            }
        }

        public void StopApplication()
        {
            RanOnUiThread |= Environment.CurrentManagedThreadId == uiThread;
            trace.Enqueue("host-request");
            _stopping.Cancel();
        }

        internal void RequestExternalStop() => _stopping.Cancel();

        public Task StopAsync(CancellationToken cancellationToken)
        {
            RanOnUiThread |= Environment.CurrentManagedThreadId == uiThread;
            StoppedBeforeUiCleanup = !UiCompletion.IsCompleted;
            StopCount++;
            trace.Enqueue("host-stop");
            if (StopFailure is not null)
            {
                throw StopFailure;
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            RanOnUiThread |= Environment.CurrentManagedThreadId == uiThread;
            trace.Enqueue("host-dispose");
            _stopping.Dispose();
        }
    }
}
