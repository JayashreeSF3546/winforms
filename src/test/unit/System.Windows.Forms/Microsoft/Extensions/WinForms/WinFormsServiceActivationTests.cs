// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.WinForms;

namespace System.Windows.Forms.Tests;

/// <summary>
///  Exercises the internal factory/assignment prototype without introducing UI scope ownership.
/// </summary>
[Collection("Sequential")]
public class WinFormsServiceActivationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_FactoryUsesStartedHostOnStaAndAssignsExistingChildren(bool componentManager)
    {
        using AppContextSwitchScope switchScope = new("Switch.System.Windows.Forms.EnableMsoComponentManager", componentManager);
        RunOnSta(() =>
        {
            RuntimeDependency? dependency = null;
            RuntimeForm? form = null;
            int calls = 0;
            int ownerThread = Environment.CurrentManagedThreadId;
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseStartupFormFactory(provider =>
                {
                    calls++;
                    Assert.Equal(ownerThread, Environment.CurrentManagedThreadId);
                    Assert.IsType<WindowsFormsSynchronizationContext>(SynchronizationContext.Current);
                    dependency = provider.GetRequiredService<RuntimeDependency>();
                    Assert.True(dependency.Started);
                    form = WinFormsServiceActivation.Create(
                        provider,
                        services => new RuntimeForm(services.GetRequiredService<RuntimeDependency>()),
                        (created, services) =>
                        {
                            Assert.False(created.IsHandleCreated);
                            WinFormsServiceActivation.Assign(created.Child, services, static (child, source) =>
                                child.InitializeServices(source.GetRequiredService<RuntimeDependency>()));
                            WinFormsServiceActivation.Assign(created.Worker, services, static (worker, source) =>
                                worker.InitializeServices(source.GetRequiredService<RuntimeDependency>()));
                            created.Shown += (_, _) => created.BeginInvoke(created.Close);
                        });

                    return form;
                })
                .UseHost(new HostBuilder().ConfigureServices((_, services) =>
                {
                    services.AddSingleton<RuntimeDependency>();
                    services.AddSingleton<IHostedService>(provider => new StartupService(provider.GetRequiredService<RuntimeDependency>()));
                }))
                .Build();

            Assert.Equal(0, calls);
            Assert.Null(dependency);
            application.Run(TestContext.Current.CancellationToken);

            Assert.Equal(1, calls);
            Assert.NotNull(form);
            Assert.NotNull(dependency);
            Assert.Same(dependency, form.Dependency);
            Assert.Same(dependency, form.Child.Dependency);
            Assert.Same(dependency, form.Worker.Dependency);
            Assert.True(form.IsDisposed);
            Assert.True(form.Child.IsDisposed);
            Assert.True(form.Worker.IsDisposed);
            Assert.True(form.DependencyAliveAfterBaseDispose);
            Assert.Equal(1, dependency.DisposeCount);
        });
    }

    [WinFormsFact]
    public void Build_ServiceFactoryRequiresHostWithoutConsumingBuilderOrActivatingUi()
    {
        int calls = 0;
        var builder = WinFormsApplication.CreateBuilder().UseStartupFormFactory(_ =>
        {
            calls++;
            return new Form();
        });
        Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Throws<InvalidOperationException>(() => builder.UseStartupForm<Form>());
        using Form supplied = new();
        Assert.Throws<InvalidOperationException>(() => builder.UseStartupForm(supplied));
        Assert.Throws<InvalidOperationException>(() => builder.UseApplicationContext(() => new ApplicationContext()));
        builder.UseHost(new HostBuilder());
        using WinFormsApplication application = builder.Build();
        Assert.Throws<InvalidOperationException>(() => builder.UseStartupFormFactory(_ => new Form()));
        application.Dispose();

        Assert.Equal(0, calls);
        Assert.False(supplied.IsDisposed);
    }

    [WinFormsFact]
    public void UseStartupFormFactory_NullDoesNotConsumeSelectionAndExistingSelectionRejectsFactory()
    {
        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder();
        Assert.Throws<ArgumentNullException>("formFactory", () => builder.UseStartupFormFactory(null!));
        builder.UseStartupForm<Form>();
        Assert.Throws<InvalidOperationException>(() => builder.UseStartupFormFactory(_ => new Form()));
        using WinFormsApplication application = builder.Build();
    }

    [WinFormsFact]
    public void Create_ExplicitConstructorDoesNotFallbackWhenRequiredServiceIsMissing()
    {
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        RuntimeForm? form = null;
        Assert.Throws<InvalidOperationException>(() => WinFormsServiceActivation.Create(provider, services =>
            form = new RuntimeForm(services.GetRequiredService<RuntimeDependency>())));
        Assert.Null(form);
    }

    [WinFormsTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Assign_OptionalServiceIsExplicitAndCallerOwnedComponentRejectsReassignment(bool registered)
    {
        ServiceCollection services = new();
        if (registered)
        {
            services.AddSingleton<RuntimeDependency>();
        }

        using ServiceProvider provider = services.BuildServiceProvider();
        using RuntimeComponent component = new();
        WinFormsServiceActivation.Assign(component, provider, static (target, source) =>
            target.InitializeServices(source.GetService<RuntimeDependency>()));

        Assert.Equal(registered, component.Dependency is not null);
        Assert.Throws<InvalidOperationException>(() => WinFormsServiceActivation.Assign(component, provider,
            static (target, _) => target.InitializeServices(null)));
        Assert.False(component.IsDisposed);
        component.Dispose();
        Assert.Throws<ObjectDisposedException>(() => WinFormsServiceActivation.Assign(component, provider,
            static (target, _) => target.InitializeServices(null)));
        Assert.Equal(0, component.Dependency?.DisposeCount ?? 0);
    }

    [WinFormsTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Create_AssignmentFailureDisposesOnlyReturnedComponent(int kind)
    {
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        using RuntimeDependency dependency = new();
        IComponent? created = null;
        int disposed = 0;
        InvalidOperationException expected = new("Assignment failed.");
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            WinFormsServiceActivation.Create(provider, _ =>
            {
                created = kind switch
                {
                    0 => new RuntimeForm(dependency),
                    1 => new RuntimeControl(),
                    _ => new RuntimeComponent()
                };
                created.Disposed += (_, _) => disposed++;

                return created;
            }, (_, _) => throw expected));

        Assert.Same(expected, failure);
        Assert.Equal(1, disposed);
        Assert.Equal(0, dependency.DisposeCount);
    }

    [WinFormsFact]
    public void Assign_FailureLeavesCallerOwnedComponentUndisposed()
    {
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        using RuntimeComponent component = new();
        InvalidOperationException expected = new("Assignment failed.");

        Assert.Same(expected, Assert.Throws<InvalidOperationException>(() =>
            WinFormsServiceActivation.Assign(component, provider, (_, _) => throw expected)));
        Assert.False(component.IsDisposed);
    }

    [WinFormsFact]
    public void Create_AssignmentAndCleanupFailuresAreBothReported()
    {
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        InvalidOperationException activationFailure = new("Assignment failed.");
        InvalidOperationException cleanupFailure = new("Cleanup failed.");

        AggregateException failure = Assert.Throws<AggregateException>(() =>
            WinFormsServiceActivation.Create(provider, _ => new ThrowingComponent(cleanupFailure),
                (_, _) => throw activationFailure));

        Assert.Equal<Exception>([activationFailure, cleanupFailure], failure.InnerExceptions);
    }

    [WinFormsFact]
    public async Task Create_BorrowsAsyncOnlyDependencyUntilProviderDisposal()
    {
        await using ServiceProvider provider = new ServiceCollection()
            .AddSingleton<AsyncDependency>()
            .BuildServiceProvider();
        using AsyncBorrowingComponent component = WinFormsServiceActivation.Create(provider,
            services => new AsyncBorrowingComponent(services.GetRequiredService<AsyncDependency>()));

        component.Dispose();
        Assert.Equal(0, component.Dependency.DisposeCount);
        await provider.DisposeAsync();
        Assert.Equal(1, component.Dependency.DisposeCount);
    }

    [WinFormsFact]
    public void Create_NullResultIsRejectedAndDisposedControlIsNotAssigned()
    {
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() =>
            WinFormsServiceActivation.Create<Form>(provider, _ => null!));
        Form form = new();
        form.Dispose();
        bool assigned = false;
        Assert.Throws<ObjectDisposedException>(() =>
            WinFormsServiceActivation.Assign(form, provider, (_, _) => assigned = true));
        Assert.False(assigned);
    }

    [WinFormsFact]
    public void Create_ForeignThreadControlIsNotAssignedOrDisposed()
    {
        using Form form = new();
        _ = form.Handle;
        bool assigned = false;
        RunOnSta(() =>
        {
            using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
            Assert.Throws<InvalidOperationException>(() => WinFormsServiceActivation.Create(provider, _ => form,
                (_, _) => assigned = true));
            Assert.Throws<InvalidOperationException>(() => WinFormsServiceActivation.Assign(form, provider,
                (_, _) => assigned = true));
        });

        Assert.False(assigned);
        Assert.False(form.IsDisposed);
    }

    [Fact]
    public void Create_NonStaDoesNotInvokeFactory()
    {
        bool called = false;
        Exception? failure = null;
        Thread thread = new(() =>
        {
            using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
            failure = Record.Exception(() => WinFormsServiceActivation.Create(provider, _ =>
            {
                called = true;
                return new Component();
            }));
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)));
        Assert.IsType<InvalidOperationException>(failure);
        Assert.False(called);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_AssignmentFailureCleansUiBeforeHostDisposesDependencies(bool componentManager)
    {
        using AppContextSwitchScope switchScope = new("Switch.System.Windows.Forms.EnableMsoComponentManager", componentManager);
        RunOnSta(() =>
        {
            RuntimeForm? form = null;
            RuntimeDependency? dependency = null;
            InvalidOperationException expected = new("Assignment failed.");
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseStartupFormFactory(provider => WinFormsServiceActivation.Create(provider, services =>
                {
                    dependency = services.GetRequiredService<RuntimeDependency>();
                    form = new RuntimeForm(dependency);

                    return form;
                }, (_, _) => throw expected))
                .UseHost(new HostBuilder().ConfigureServices((_, services) => services.AddSingleton<RuntimeDependency>()))
                .Build();

            Assert.Same(expected, Assert.Throws<InvalidOperationException>(() => application.Run(TestContext.Current.CancellationToken)));
            Assert.NotNull(form);
            Assert.True(form.IsDisposed);
            Assert.True(form.Worker.IsDisposed);
            Assert.True(form.DependencyAliveAfterBaseDispose);
            Assert.NotNull(dependency);
            Assert.Equal(1, dependency.DisposeCount);
        });
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "The caller-owned STA did not finish.");
        failure?.Throw();
    }

    /// <summary>
    ///  Records host startup and provider-owned service disposal.
    /// </summary>
    private sealed class RuntimeDependency : IDisposable
    {
        public RuntimeDependency()
        {
        }

        internal bool Started { get; set; }

        internal int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;
    }

    /// <summary>
    ///  Establishes host startup ordering without registering a disposable dependency twice.
    /// </summary>
    private sealed class StartupService(RuntimeDependency dependency) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            dependency.Started = true;

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    /// <summary>
    ///  Represents a service-free layout constructor and an explicit typed runtime constructor.
    /// </summary>
    private sealed class RuntimeForm : Form
    {
        private readonly Container _components = new();

        public RuntimeForm()
        {
            Controls.Add(Child);
            _components.Add(Worker);
        }

        internal RuntimeForm(RuntimeDependency dependency) : this()
        {
            Dependency = dependency;
        }

        internal RuntimeControl Child { get; } = new();

        internal RuntimeComponent Worker { get; } = new();

        internal RuntimeDependency? Dependency { get; }

        internal bool DependencyAliveAfterBaseDispose { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _components.Dispose();
            }

            base.Dispose(disposing);
            DependencyAliveAfterBaseDispose = Dependency?.DisposeCount == 0;
        }
    }

    /// <summary>
    ///  Receives its typed dependency after the parent has constructed the control graph.
    /// </summary>
    private sealed class RuntimeControl : UserControl
    {
        internal RuntimeDependency? Dependency { get; private set; }

        internal void InitializeServices(RuntimeDependency dependency)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            ArgumentNullException.ThrowIfNull(dependency);
            if (Dependency is not null)
            {
                throw new InvalidOperationException("Services have already been assigned.");
            }

            Dependency = dependency;
        }
    }

    /// <summary>
    ///  Demonstrates an optional typed assignment without a provider field or owned service scope.
    /// </summary>
    private sealed class RuntimeComponent : Component
    {
        private bool _assigned;

        internal RuntimeDependency? Dependency { get; private set; }

        internal bool IsDisposed { get; private set; }

        internal void InitializeServices(RuntimeDependency? dependency)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (_assigned)
            {
                throw new InvalidOperationException("Services have already been assigned.");
            }

            _assigned = true;
            Dependency = dependency;
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    /// <summary>
    ///  Makes independent cleanup failure observable without hiding the assignment exception.
    /// </summary>
    private sealed class ThrowingComponent(Exception failure) : Component
    {
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                throw failure;
            }
        }
    }

    /// <summary>
    ///  Leaves service cleanup to its supplying provider rather than component disposal.
    /// </summary>
    private sealed class AsyncBorrowingComponent(AsyncDependency dependency) : Component
    {
        internal AsyncDependency Dependency { get; } = dependency;
    }

    /// <summary>
    ///  Requires asynchronous cleanup, which the activation helper does not attempt to own.
    /// </summary>
    private sealed class AsyncDependency : IAsyncDisposable
    {
        public AsyncDependency()
        {
        }

        internal int DisposeCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;

            return ValueTask.CompletedTask;
        }
    }
}
