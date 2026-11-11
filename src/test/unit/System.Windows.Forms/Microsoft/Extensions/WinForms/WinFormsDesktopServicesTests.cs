// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net.NetworkInformation;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.WinForms;

namespace System.Windows.Forms.Tests;

/// <summary>
///  Validates the internal desktop-service prototype using isolated owner STAs and bounded pumps.
/// </summary>
[Collection("Sequential")]
public class WinFormsDesktopServicesTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Splash_MinimumGate_PumpsOwnerUiAndRejectsActivationUntilReady(bool componentManager)
    {
        using AppContextSwitchScope scope = new("Switch.System.Windows.Forms.EnableMsoComponentManager", componentManager);
        RunOnSta(token =>
        {
            ControlledClock clock = new();
            TrackingSplash splash = new();
            QuietForm main = new();
            FakeNetwork source = new();
            int owner = Environment.CurrentManagedThreadId;
            using WinFormsSingleInstance primary = WinFormsSingleInstance.SelectAsync(
                WinFormsSingleInstanceIdentity.Create(Guid.NewGuid().ToString(), "14977"),
                [], false, TimeSpan.FromSeconds(5), token).GetAwaiter().GetResult().Primary!;
            WinFormsApplication? app = null;
            Task<WinFormsSingleInstanceResult>? activation = null;
            using (app = WinFormsApplication.CreateBuilder().UseStartupForm(main)
                .UseSingleInstance(primary, (_, request) =>
                {
                    Environment.CurrentManagedThreadId.Should().Be(owner);
                    request.BringToForeground = false;
                    Post(() => _ = app!.StopAsync());
                })
                .ConfigureApplicationFeatures(features =>
                {
                    features.SplashScreenFactory = () => splash;
                    features.SplashTimeProvider = clock;
                    features.MinimumSplashDisplayTime = TimeSpan.FromSeconds(1);
                    features.NetworkSource = source;
                    features.NetworkAvailabilityChanged = _ => throw new InvalidOperationException("No transition expected.");
                }).Build())
            {
                main.Load += (_, _) =>
                {
                    Environment.CurrentManagedThreadId.Should().Be(owner);
                    clock.GetTimestamp().Should().Be(TimeSpan.TicksPerSecond);
                    splash.IsDisposed.Should().BeTrue();
                    source.Adds.Should().Be(0);
                };
                main.Shown += (_, _) =>
                    activation = app.DispatchNextInstanceAsync(new(Guid.NewGuid(), [], false), token);
                app.Lifetime.ApplicationStarted += (_, _) => Post(() =>
                {
                    main.Visible.Should().BeFalse();
                    splash.Visible.Should().BeTrue();
                    source.Adds.Should().Be(0);
                    app.DispatchNextInstanceAsync(new(Guid.NewGuid(), [], false), token)
                        .GetAwaiter().GetResult().Should().Be(WinFormsSingleInstanceResult.NotReady);
                    source.Raise();
                    clock.Advance(TimeSpan.FromMilliseconds(999));
                    clock.Fire();
                    Post(() =>
                    {
                        main.Visible.Should().BeFalse("an early timer invalidation cannot end the minimum");
                        clock.Advance(TimeSpan.FromMilliseconds(1));
                        clock.Fire();
                    });
                });
                app.Run(token);
            }

            activation!.GetAwaiter().GetResult().Should().Be(WinFormsSingleInstanceResult.Handled);
            splash.DisposeCount.Should().Be(1);
            main.IsDisposed.Should().BeTrue();
            clock.TimerDisposed.Should().BeTrue();
            source.Adds.Should().Be(1);
            source.Removes.Should().Be(1);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Splash_RealTimerDispatchesWithoutBlockingTheExistingPump(bool componentManager)
    {
        using AppContextSwitchScope scope = new("Switch.System.Windows.Forms.EnableMsoComponentManager", componentManager);
        RunOnSta(token =>
        {
            TrackingSplash splash = new();
            QuietForm main = new();
            TimeSpan minimum = TimeSpan.FromMilliseconds(30);
            long visible = 0;
            splash.VisibleChanged += (_, _) =>
            {
                if (splash.Visible)
                {
                    visible = TimeProvider.System.GetTimestamp();
                }
            };
            main.Load += (_, _) =>
            {
                TimeProvider.System.GetElapsedTime(visible).Should().BeGreaterThanOrEqualTo(minimum);
                splash.IsDisposed.Should().BeTrue();
            };
            main.Shown += (_, _) => main.Close();
            using WinFormsApplication app = WinFormsApplication.CreateBuilder().UseStartupForm(main)
                .ConfigureApplicationFeatures(features =>
                {
                    features.SplashScreenFactory = () => splash;
                    features.MinimumSplashDisplayTime = minimum;
                }).Build();

            app.Run(token);

            splash.DisposeCount.Should().Be(1);
            main.IsDisposed.Should().BeTrue();
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("/nosplash")]
    [InlineData("-nosplash")]
    [InlineData("/NOSPLASH")]
    public void Splash_ZeroMinimumAndExactSuppressionPreserveRawArguments(string argument)
    {
        RunOnSta(token =>
        {
            ControlledClock clock = new();
            TrackingSplash? splash = null;
            QuietForm main = new();
            main.Shown += (_, _) => main.Close();
            using WinFormsApplication app = WinFormsApplication.CreateBuilder().UseStartupForm(main)
                .ConfigureApplicationFeatures(features =>
                {
                    features.CommandLine = [argument];
                    features.Starting = (_, e) => e.CommandLine.Should().Equal(argument);
                    features.SplashScreenFactory = () => splash = new TrackingSplash();
                    features.MinimumSplashDisplayTime = TimeSpan.Zero;
                    features.SplashTimeProvider = clock;
                }).Build();

            app.Run(token);

            (splash is null).Should().Be(argument is "/nosplash" or "-nosplash");
            splash?.DisposeCount.Should().Be(1);
            clock.TimerCreated.Should().BeFalse();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Splash_CanceledStartingOrRootFailureDoesNotWaitForMinimum(bool cancel)
    {
        RunOnSta(token =>
        {
            TrackingSplash? splash = null;
            ControlledClock clock = new();
            InvalidOperationException failure = new("Root activation failed.");
            using WinFormsApplication app = WinFormsApplication.CreateBuilder()
                .UseApplicationContext(() => throw failure)
                .ConfigureApplicationFeatures(features =>
                {
                    features.Starting = (_, e) => e.Cancel = cancel;
                    features.SplashScreenFactory = () => splash = new TrackingSplash();
                    features.SplashTimeProvider = clock;
                    features.MinimumSplashDisplayTime = TimeSpan.FromDays(1);
                    features.Shutdown = (_, _) => throw new InvalidOperationException("No ready root.");
                }).Build();

            if (cancel)
            {
                app.Run(token);
                splash.Should().BeNull();
            }
            else
            {
                Assert.Throws<InvalidOperationException>(() => app.Run(token)).Should().BeSameAs(failure);
                splash!.DisposeCount.Should().Be(1);
            }

            clock.TimerCreated.Should().BeFalse();
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Splash_StopDuringMinimumReleasesOwnedResourcesWithoutShowingMain(int stopKind)
    {
        RunOnSta(token =>
        {
            TrackingSplash splash = new();
            ControlledClock clock = new();
            QuietForm main = new();
            using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            main.Load += (_, _) => throw new InvalidOperationException("Stop must not reveal the main UI.");
            using WinFormsApplication app = WinFormsApplication.CreateBuilder().UseStartupForm(main)
                .UseHost(new HostBuilder())
                .ConfigureApplicationFeatures(features =>
                {
                    features.SplashScreenFactory = () => splash;
                    features.SplashTimeProvider = clock;
                    features.MinimumSplashDisplayTime = TimeSpan.FromDays(1);
                    features.Shutdown = (_, _) => throw new InvalidOperationException("Not a ready startup.");
                }).Build();
            app.Lifetime.ApplicationStarted += (_, _) => Post(() =>
            {
                clock.TimerCreated.Should().BeTrue();
                switch (stopKind)
                {
                    case 0:
                        _ = app.StopAsync();
                        break;
                    case 1:
                        app.Host!.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();
                        break;
                    case 2:
                        cancellation.Cancel();
                        break;
                    case 3:
                        splash.Close();
                        break;
                    default:
                        splash.Hide();
                        break;
                }
            });

            if (stopKind == 2)
            {
                Assert.ThrowsAny<OperationCanceledException>(() => app.Run(cancellation.Token));
            }
            else
            {
                app.Run(cancellation.Token);
            }

            clock.TimerDisposed.Should().BeTrue();
            splash.DisposeCount.Should().Be(1);
            main.IsDisposed.Should().BeTrue();
            // A timer callback already acquired by a worker must be harmless after cleanup.
            clock.Fire();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Splash_RequiresDistinctHiddenMainForm(bool alreadyVisible)
    {
        RunOnSta(token =>
        {
            TrackingSplash splash = new();
            Form? root = null;
            using WinFormsApplication app = WinFormsApplication.CreateBuilder().UseApplicationContext(() =>
                {
                    if (alreadyVisible)
                    {
                        root = new QuietForm();
                        root.Show();
                    }
                    else
                    {
                        root = splash;
                    }

                    return new ApplicationContext(root);
                })
                .ConfigureApplicationFeatures(features => features.SplashScreenFactory = () => splash).Build();

            Assert.Throws<InvalidOperationException>(() => app.Run(token)).Message
                .Should().Be(SR.WinFormsApplication_SplashRequiresHiddenMainForm);

            splash.DisposeCount.Should().Be(1);
            root!.IsDisposed.Should().BeTrue();
        });
    }

    [Fact]
    public void Splash_TimerSetupFailureIsReportedAndCleanedUp()
    {
        RunOnSta(token =>
        {
            ControlledClock clock = new() { ThrowOnCreate = true };
            TrackingSplash splash = new();
            QuietForm main = new();
            List<WinFormsLifecyclePhase> phases = [];
            using WinFormsApplication app = WinFormsApplication.CreateBuilder().UseStartupForm(main)
                .ConfigureApplicationFeatures(features =>
                {
                    features.SplashScreenFactory = () => splash;
                    features.SplashTimeProvider = clock;
                    features.ReportFailure = (_, phase) => phases.Add(phase);
                }).Build();

            Assert.Throws<InvalidOperationException>(() => app.Run(token)).Message.Should().Be("Timer setup failed.");

            phases.Should().Equal(WinFormsLifecyclePhase.ApplicationService);
            splash.DisposeCount.Should().Be(1);
            main.IsDisposed.Should().BeTrue();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Network_ReReadsLatestStateOnOwnerAndRejectsLateNotificationsWithoutOwningSource(bool initial)
    {
        RunOnSta(token =>
        {
            FakeNetwork source = new() { Available = initial };
            QuietForm main = new();
            List<bool> values = [];
            int owner = Environment.CurrentManagedThreadId;
            WinFormsApplication? app = null;
            using (app = WinFormsApplication.CreateBuilder().UseStartupForm(main)
                .ConfigureApplicationFeatures(features =>
                {
                    features.NetworkSource = source;
                    features.NetworkAvailabilityChanged = available =>
                    {
                        Environment.CurrentManagedThreadId.Should().Be(owner);
                        SynchronizationContext.Current.Should().BeOfType<WindowsFormsSynchronizationContext>();
                        values.Add(available);
                        if (available == initial)
                        {
                            _ = app!.StopAsync();
                            source.Raise();
                        }
                    };
                }).Build())
            {
                app.Lifetime.ApplicationStarted += (_, _) =>
                {
                    source.Adds.Should().Be(0);
                    source.Raise();
                    Post(() =>
                    {
                        source.Available = !initial;
                        Task.Run(source.Raise, token).GetAwaiter().GetResult();
                        Post(() =>
                        {
                            values.Should().Equal(!initial);
                            source.Available = initial;
                            source.Raise();
                            source.Available = !initial; // Re-read, do not replay stale event data.
                            Post(() =>
                            {
                                values.Should().Equal(!initial);
                                source.Available = initial;
                                source.Raise();
                            });
                        });
                    });
                };
                app.Run(token);
            }

            values.Should().Equal(!initial, initial);
            source.Adds.Should().Be(1);
            source.Removes.Should().Be(1);
            source.DisposeCount.Should().Be(0);
            Task.Run(source.RaiseLate, token).GetAwaiter().GetResult();
            values.Should().Equal(!initial, initial);
        });
    }

    [Theory]
    [InlineData("add")]
    [InlineData("initial-read")]
    [InlineData("delivery-read")]
    [InlineData("callback")]
    [InlineData("remove")]
    public void Network_SourceAndConsumerFailuresAreRetainedAndUnsubscribed(string failurePoint)
    {
        RunOnSta(token =>
        {
            InvalidOperationException failure = new(failurePoint);
            FakeNetwork source = new()
            {
                AddFailure = failurePoint == "add" ? failure : null,
                ReadFailure = failurePoint == "initial-read" ? failure : null,
                RemoveFailure = failurePoint == "remove" ? failure : null
            };
            QuietForm main = new();
            List<Exception> reported = [];
            WinFormsApplication? app = null;
            using (app = WinFormsApplication.CreateBuilder().UseStartupForm(main)
                .ConfigureApplicationFeatures(features =>
                {
                    features.NetworkSource = source;
                    features.NetworkAvailabilityChanged = available =>
                    {
                        if (failurePoint == "callback")
                        {
                            throw failure;
                        }

                        _ = app!.StopAsync();
                    };
                    features.ReportFailure = (exception, phase) =>
                    {
                        phase.Should().Be(WinFormsLifecyclePhase.ApplicationService);
                        reported.Add(exception);
                    };
                }).Build())
            {
                app.Lifetime.ApplicationStarted += (_, _) => Post(() =>
                {
                    if (failurePoint == "delivery-read")
                    {
                        source.ReadFailure = failure;
                    }

                    source.Available = true;
                    source.Raise();
                });

                Assert.Throws<InvalidOperationException>(() => app.Run(token)).Should().BeSameAs(failure);
            }

            source.Removes.Should().Be(1);
            source.DisposeCount.Should().Be(0);
            reported.Should().Equal(failurePoint == "remove" ? [] : [failure]);
            source.RaiseLate();
            main.IsDisposed.Should().BeTrue();
        });
    }

    [Fact]
    public void Network_ReentrantStopDuringMainLoadSkipsSubscription()
    {
        RunOnSta(token =>
        {
            FakeNetwork source = new();
            QuietForm main = new();
            using WinFormsApplication app = WinFormsApplication.CreateBuilder().UseStartupForm(main)
                .ConfigureApplicationFeatures(features =>
                {
                    features.NetworkSource = source;
                    features.NetworkAvailabilityChanged = _ => throw new InvalidOperationException("Stopped.");
                }).Build();
            main.Load += (_, _) => _ = app.StopAsync();

            app.Run(token);

            source.Adds.Should().Be(0);
        });
    }

    [Fact]
    public void Splash_ReentrantStopDuringReleaseSkipsMainAndNetwork()
    {
        RunOnSta(token =>
        {
            TrackingSplash splash = new();
            QuietForm main = new();
            FakeNetwork source = new();
            using WinFormsApplication app = WinFormsApplication.CreateBuilder().UseStartupForm(main)
                .ConfigureApplicationFeatures(features =>
                {
                    features.SplashScreenFactory = () => splash;
                    features.MinimumSplashDisplayTime = TimeSpan.Zero;
                    features.NetworkSource = source;
                    features.NetworkAvailabilityChanged = _ => throw new InvalidOperationException("Stopped.");
                    features.Shutdown = (_, _) => throw new InvalidOperationException("Startup was not ready.");
                }).Build();
            splash.Disposed += (_, _) => _ = app.StopAsync();
            main.Load += (_, _) => throw new InvalidOperationException("Stopped before main visibility.");

            app.Run(token);

            source.Adds.Should().Be(0);
            splash.DisposeCount.Should().Be(1);
            main.IsDisposed.Should().BeTrue();
        });
    }

    [Fact]
    public void Network_ReentrantStopDuringSubscriptionSkipsInitialRead()
    {
        RunOnSta(token =>
        {
            FakeNetwork source = new() { ReadFailure = new InvalidOperationException("Stopped before initial read.") };
            using WinFormsApplication app = WinFormsApplication.CreateBuilder().UseStartupForm(new QuietForm())
                .ConfigureApplicationFeatures(features =>
                {
                    features.NetworkSource = source;
                    features.NetworkAvailabilityChanged = _ => throw new InvalidOperationException("Stopped.");
                }).Build();
            source.OnSubscribe = () => _ = app.StopAsync();

            app.Run(token);

            source.Adds.Should().Be(1);
            source.Removes.Should().Be(1);
        });
    }

    [Fact]
    public void Network_UnsupportedSourceIsFailureNotAnOfflineNotification()
    {
        RunOnSta(token =>
        {
            PlatformNotSupportedException failure = new("Unavailable event source.");
            FakeNetwork source = new() { ReadFailure = failure };
            using WinFormsApplication app = WinFormsApplication.CreateBuilder().UseStartupForm(new QuietForm())
                .ConfigureApplicationFeatures(features =>
                {
                    features.NetworkSource = source;
                    features.NetworkAvailabilityChanged = _ => throw new InvalidOperationException("Not offline.");
                }).Build();

            Assert.Throws<PlatformNotSupportedException>(() => app.Run(token)).Should().BeSameAs(failure);

            source.Removes.Should().Be(1);
        });
    }

    [Fact]
    public void Information_SnapshotsExplicitAssemblyNotDriverOrWorkingDirectory()
    {
        Assembly selected = typeof(Form).Assembly;
        WinFormsApplicationFeatures? retained = null;
        using WinFormsApplication app = WinFormsApplication.CreateBuilder().UseStartupForm<Form>()
            .ConfigureApplicationFeatures(features =>
            {
                features.ApplicationAssembly = selected;
                retained = features;
            }).Build();
        retained!.ApplicationAssembly = typeof(WinFormsDesktopServicesTests).Assembly;

        WinFormsApplicationInformation info = app.Information!;
        info.AssemblyName.Should().Be(selected.GetName().Name);
        info.AssemblyLocation.Should().Be(selected.Location);
        info.DirectoryPath.Should().Be(Path.GetDirectoryName(selected.Location));
        info.Title.Should().Be(selected.GetCustomAttribute<AssemblyTitleAttribute>()!.Title);
        info.Version.Should().Be(selected.GetName().Version);
        info.FrameworkDescription.Should().Be(RuntimeInformation.FrameworkDescription);

        AssemblyBuilder dynamicAssembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("SelectedMetadata") { Version = new Version(3, 2, 1, 0) }, AssemblyBuilderAccess.Run);
        WinFormsApplicationInformation absent = new(dynamicAssembly);
        absent.AssemblyName.Should().Be("SelectedMetadata");
        absent.AssemblyLocation.Should().BeNull();
        absent.DirectoryPath.Should().BeNull();
        absent.CompanyName.Should().BeEmpty();
        absent.Title.Should().BeEmpty();
        absent.AssemblyCulture.Should().BeEmpty();
    }

    private static void Post(Action callback)
    {
        Application.ThreadContext context = Application.ThreadContext.FromCurrent();
        WinFormsApplication runner = context.ApplicationRunner!;
        context.MarshallingControl.BeginInvoke(() =>
        {
            try
            {
                callback();
            }
            catch (Exception exception)
            {
                runner.RecordException(exception);
                runner.RequestStop();
            }
        });
    }

    private static void RunOnSta(Action<CancellationToken> action)
    {
        using CancellationTokenSource watchdog = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        watchdog.CancelAfter(TimeSpan.FromSeconds(20));
        ExceptionDispatchInfo? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                action(watchdog.Token);
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(30)).Should().BeTrue("the owned STA must complete bounded cleanup");
        failure?.Throw();
    }

    /// <summary>
    ///  Avoids activating test main windows on a shared desktop.
    /// </summary>
    private sealed class QuietForm : Form
    {
        protected override bool ShowWithoutActivation => true;
    }

    /// <summary>
    ///  Counts actual splash disposal while leaving all behavior on its owner STA.
    /// </summary>
    private sealed class TrackingSplash : WinFormsSplashScreen
    {
        internal int DisposeCount { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCount++;
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    ///  Supplies an explicitly advanced monotonic clock and manually fired timer invalidations.
    /// </summary>
    private sealed class ControlledClock : TimeProvider
    {
        private long _timestamp;
        private TimerCallback? _callback;
        private object? _state;
        internal bool ThrowOnCreate { get; init; }
        internal bool TimerCreated { get; private set; }
        internal bool TimerDisposed { get; set; }
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        internal void Advance(TimeSpan amount) => _timestamp += amount.Ticks;
        internal void Fire() => _callback?.Invoke(_state);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (ThrowOnCreate)
            {
                throw new InvalidOperationException("Timer setup failed.");
            }

            TimerCreated = true;
            _callback = callback;
            _state = state;
            return new ControlledTimer(this);
        }

        /// <summary>
        ///  Records release without relying on elapsed wall-clock time.
        /// </summary>
        private sealed class ControlledTimer(ControlledClock clock) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() => clock.TimerDisposed = true;
            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    /// <summary>
    ///  Supplies availability invalidations without altering any machine network state.
    /// </summary>
    private sealed class FakeNetwork : IWinFormsNetworkSource, IDisposable
    {
        private NetworkAddressChangedEventHandler? _changed;
        private NetworkAddressChangedEventHandler? _late;
        internal bool Available { get; set; }
        internal int Adds { get; private set; }
        internal int Removes { get; private set; }
        internal int DisposeCount { get; private set; }
        internal Exception? AddFailure { get; init; }
        internal Exception? ReadFailure { get; set; }
        internal Exception? RemoveFailure { get; init; }
        internal Action? OnSubscribe { get; set; }
        public bool IsAvailable => ReadFailure is { } failure ? throw failure : Available;
        public event NetworkAddressChangedEventHandler Changed
        {
            add
            {
                Adds++;
                _changed += value;
                _late = value;
                OnSubscribe?.Invoke();
                if (AddFailure is { } failure)
                {
                    throw failure;
                }
            }
            remove
            {
                Removes++;
                _changed -= value;
                if (RemoveFailure is { } failure)
                {
                    throw failure;
                }
            }
        }

        internal void Raise() => _changed?.Invoke(this, EventArgs.Empty);
        internal void RaiseLate() => _late?.Invoke(this, EventArgs.Empty);
        public void Dispose() => DisposeCount++;
    }
}
