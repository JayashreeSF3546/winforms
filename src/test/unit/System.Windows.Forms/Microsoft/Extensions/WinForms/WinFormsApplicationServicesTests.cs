// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.WinForms;

namespace System.Windows.Forms.Tests;

/// <summary>
///  Tests the shared application services and splash-screen coordination.
/// </summary>
public class WinFormsApplicationServicesTests
{
    /// <summary>
    ///  Verifies that the builder registers default application and runtime information services.
    /// </summary>
    [Fact]
    public void AddApplicationServices_RegistersApplicationAndRuntimeInformation()
    {
        using WinFormsApplication application = WinFormsApplication.CreateBuilder()
            .AddApplicationServices()
            .UseApplicationContext()
            .Build();

        IServiceProvider services = application.Options.Host!.Services;
        IWinFormsApplicationInfo applicationInfo = Assert.IsType<WinFormsApplicationInfo>(
            services.GetService(typeof(IWinFormsApplicationInfo)));
        IWinFormsRuntimeInfo runtimeInfo = Assert.IsType<WinFormsRuntimeInfo>(
            services.GetService(typeof(IWinFormsRuntimeInfo)));

        Assert.False(string.IsNullOrEmpty(applicationInfo.Name));
        Assert.Equal(AppContext.BaseDirectory, applicationInfo.BaseDirectory);
        Assert.Contains(typeof(WinFormsApplicationServicesTests).Assembly, applicationInfo.LoadedAssemblies);
        Assert.Equal(Environment.ProcessId, runtimeInfo.ProcessId);
        Assert.False(string.IsNullOrEmpty(runtimeInfo.FrameworkDescription));
        Assert.False(string.IsNullOrEmpty(runtimeInfo.OperatingSystemDescription));
    }

    /// <summary>
    ///  Verifies that network changes are marshaled and duplicate availability states are suppressed.
    /// </summary>
    [Fact]
    public void NetworkAvailabilityChanged_MarshalsAndRaisesOnlyForAvailabilityChanges()
    {
        FakeNetworkAvailabilityMonitor monitor = new(isAvailable: false);
        using WinFormsNetworkAvailabilityService service = new(monitor);
        RecordingSynchronizationContext synchronizationContext = new();
        List<bool> observedStates = [];
        EventHandler<WinFormsNetworkAvailabilityChangedEventArgs> handler =
            (_, eventArgs) => observedStates.Add(eventArgs.IsAvailable);

        service.NetworkAvailabilityChanged += handler;

        Assert.Equal(0, monitor.SubscribeCount);

        service.Initialize(synchronizationContext);
        Assert.Equal(1, monitor.SubscribeCount);
        Assert.False(service.IsAvailable);

        monitor.SetAvailability(isAvailable: true);

        Assert.Empty(observedStates);

        synchronizationContext.DispatchPending();

        Assert.Equal([true], observedStates);

        monitor.SetAvailability(isAvailable: true);
        synchronizationContext.DispatchPending();

        Assert.Equal([true], observedStates);

        service.NetworkAvailabilityChanged -= handler;
        Assert.Equal(1, monitor.UnsubscribeCount);

        service.NetworkAvailabilityChanged += handler;
        Assert.Equal(2, monitor.SubscribeCount);

        service.Stop();
        Assert.Equal(2, monitor.UnsubscribeCount);

        monitor.SetAvailability(isAvailable: false);
        synchronizationContext.DispatchPending();

        Assert.Equal([true], observedStates);
    }

    /// <summary>
    ///  Verifies that the configured splash remains visible until startup completes and is disposed on shutdown.
    /// </summary>
    [Fact]
    public void Run_WithSplashScreen_CoordinatesHostlessStartupAndShutdown()
    {
        RunOnStaThread(() =>
        {
            using Form splashForm = new();
            using Form startupForm = new();
            List<string> events = [];
            startupForm.Shown += (_, _) =>
            {
                Assert.True(splashForm.IsDisposed);
                events.Add("MainFormShown");
                startupForm.Close();
            };

            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseStartupForm(startupForm)
                .UseSplashScreen(() => splashForm, TimeSpan.Zero)
                .Build();
            application.Lifetime.ApplicationStarted += (_, _) => events.Add("ApplicationStarted");

            application.Run();

            Assert.Equal(["ApplicationStarted", "MainFormShown"], events);
            Assert.True(splashForm.IsDisposed);
        });
    }

    /// <summary>
    ///  Verifies that host startup completes while the splash is visible and before the main form is shown.
    /// </summary>
    [Fact]
    public void Run_WithSplashScreen_CoordinatesHostBackedStartup()
    {
        RunOnStaThread(() =>
        {
            using Form splashForm = new();
            using Form startupForm = new();
            List<string> events = [];
            startupForm.Shown += (_, _) =>
            {
                Assert.True(splashForm.IsDisposed);
                events.Add("MainFormShown");
                startupForm.Close();
            };

            WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder();
            builder.Services.AddSingleton<IHostedService>(
                new SplashCheckingHostedService(splashForm, isVisible =>
                {
                    Assert.True(isVisible);
                    events.Add("HostStarted");
                }));
            using WinFormsApplication application = builder
                .UseStartupForm(startupForm)
                .UseSplashScreen(() => splashForm, TimeSpan.Zero)
                .Build();
            application.Lifetime.ApplicationStarted += (_, _) =>
            {
                Assert.True(splashForm.IsDisposed);
                events.Add("ApplicationStarted");
            };

            application.Run();

            Assert.Equal(["HostStarted", "ApplicationStarted", "MainFormShown"], events);
        });
    }

    /// <summary>
    ///  Verifies that startup cancellation prevents splash-screen creation.
    /// </summary>
    [Fact]
    public void Run_StartupCancellationDoesNotCreateSplashScreen()
    {
        RunOnStaThread(() =>
        {
            bool splashFactoryInvoked = false;
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseApplicationContext()
                .UseSplashScreen(
                    () =>
                    {
                        splashFactoryInvoked = true;
                        return new Form();
                    },
                    TimeSpan.Zero)
                .Build();
            application.Lifetime.ApplicationStarting += (_, eventArgs) => eventArgs.Cancel = true;

            application.Run();

            Assert.False(splashFactoryInvoked);
        });
    }

    /// <summary>
    ///  Verifies that a splash factory failure is surfaced and startup does not complete.
    /// </summary>
    [Fact]
    public void Run_SplashFactoryFailureIsSurfaced()
    {
        RunOnStaThread(() =>
        {
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseApplicationContext()
                .UseSplashScreen(
                    static () => throw new InvalidOperationException("Splash creation failed."),
                    TimeSpan.Zero)
                .Build();
            bool applicationStarted = false;
            application.Lifetime.ApplicationStarted += (_, _) => applicationStarted = true;

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(application.Run);

            Assert.Equal("Splash creation failed.", exception.Message);
            Assert.False(applicationStarted);
        });
    }

    /// <summary>
    ///  Verifies that the splash minimum-duration wait uses an asynchronous delay.
    /// </summary>
    [Fact]
    public void WaitForMinimumDisplayDurationAsync_UsesAsynchronousDelay()
    {
        RunOnStaThread(() =>
        {
            bool originalAutoInstall = WindowsFormsSynchronizationContext.AutoInstall;
            WindowsFormsSynchronizationContext.AutoInstall = false;

            try
            {
                using Form form = new();
                TaskCompletionSource finishDelay = new(TaskCreationOptions.RunContinuationsAsynchronously);
                TimeSpan requestedDelay = TimeSpan.Zero;
                using WinFormsSplashScreen splashScreen = new(
                    form,
                    TimeSpan.FromHours(1),
                    (duration, _) =>
                    {
                        requestedDelay = duration;
                        return finishDelay.Task;
                    });

                splashScreen.Show();
                Task wait = splashScreen.WaitForMinimumDisplayDurationAsync();

                Assert.False(wait.IsCompleted);
                Assert.True(requestedDelay > TimeSpan.Zero);

                finishDelay.SetResult();
                wait.GetAwaiter().GetResult();
            }
            finally
            {
                WindowsFormsSynchronizationContext.AutoInstall = originalAutoInstall;
            }
        });
    }

    /// <summary>
    ///  Verifies that stopping the splash cancels its pending minimum-duration delay and disposes its form.
    /// </summary>
    [Fact]
    public void Stop_CancelsMinimumDisplayDelayAndDisposesForm()
    {
        RunOnStaThread(() =>
        {
            bool originalAutoInstall = WindowsFormsSynchronizationContext.AutoInstall;
            WindowsFormsSynchronizationContext.AutoInstall = false;

            try
            {
                using Form form = new();
                using WinFormsSplashScreen splashScreen = new(
                    form,
                    TimeSpan.FromHours(1),
                    static (_, cancellationToken) => Task.Delay(Timeout.Infinite, cancellationToken));

                splashScreen.Show();
                Task wait = splashScreen.WaitForMinimumDisplayDurationAsync();

                splashScreen.Stop();

                Assert.ThrowsAny<OperationCanceledException>(() => wait.GetAwaiter().GetResult());
                Assert.True(form.IsDisposed);
            }
            finally
            {
                WindowsFormsSynchronizationContext.AutoInstall = originalAutoInstall;
            }
        });
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    /// <summary>
    ///  Provides a controllable network availability source for service tests.
    /// </summary>
    private sealed class FakeNetworkAvailabilityMonitor(bool isAvailable) : INetworkAvailabilityMonitor
    {
        private event EventHandler? _networkAddressChanged;

        /// <summary>
        ///  Gets the number of listener registrations.
        /// </summary>
        public int SubscribeCount { get; private set; }

        /// <summary>
        ///  Gets the number of listener removals.
        /// </summary>
        public int UnsubscribeCount { get; private set; }

        /// <inheritdoc/>
        public bool IsAvailable { get; private set; } = isAvailable;

        /// <inheritdoc/>
        public event EventHandler? NetworkAddressChanged
        {
            add
            {
                SubscribeCount++;
                _networkAddressChanged += value;
            }
            remove
            {
                UnsubscribeCount++;
                _networkAddressChanged -= value;
            }
        }

        /// <summary>
        ///  Changes the current state and raises a network-address notification.
        /// </summary>
        /// <param name="isAvailable">The new availability state.</param>
        public void SetAvailability(bool isAvailable)
        {
            IsAvailable = isAvailable;
            _networkAddressChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    ///  Queues synchronization-context callbacks until a test explicitly dispatches them.
    /// </summary>
    private sealed class RecordingSynchronizationContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _callbacks = new();

        /// <inheritdoc/>
        public override void Post(SendOrPostCallback callback, object? state)
            => _callbacks.Enqueue((callback, state));

        /// <summary>
        ///  Runs all callbacks currently queued by <see cref="Post"/>.
        /// </summary>
        public void DispatchPending()
        {
            while (_callbacks.TryDequeue(out (SendOrPostCallback Callback, object? State) workItem))
            {
                workItem.Callback(workItem.State);
            }
        }
    }

    /// <summary>
    ///  Observes whether a configured splash form remains visible during host startup.
    /// </summary>
    private sealed class SplashCheckingHostedService(Form splashForm, Action<bool> onStart) : IHostedService
    {
        /// <inheritdoc/>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            onStart(splashForm.Visible);

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task StopAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
