// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.WinForms;

namespace System.Windows.Forms.Tests;

/// <summary>
///  Exercises opt-in synchronous UI scopes without changing service-free activation.
/// </summary>
/// <remarks>
///  <para>
///   Scoped forms use base-last disposal. Modal and application tests own their STA and message loop.
///  </para>
/// </remarks>
[Collection("Sequential")]
public class WinFormsUiScopeTests
{
    [WinFormsFact]
    public void DirectDispose_ReleasesFormScopeAfterBorrowingControlsAndComponents()
    {
        List<string> log = [];
        using ServiceProvider provider = CreateProvider(log);
        using WinFormsUiScope application = new(provider);
        WinFormsFormScope first = CreateForm(application, "first");
        WinFormsFormScope second = CreateForm(application, "second");
        var form = (ScopedForm)first.Form;
        var other = (ScopedForm)second.Form;
        first.Assign(form.Child, static (target, services) => target.Dependency = services.GetRequiredService<Dependency>());
        first.Assign(form.Worker, static (target, services) => target.Dependency = services.GetRequiredService<Dependency>());
        Assert.Same(form.Dependency, form.Child.Dependency);
        Assert.Same(form.Dependency, form.Worker.Dependency);
        Assert.NotSame(form.Dependency, other.Dependency);
        other.Controls.Add(form.Child);
        Assert.Throws<InvalidOperationException>(() =>
            second.Assign(form.Child, static (target, services) => target.Dependency = services.GetRequiredService<Dependency>()));
        form.Controls.Add(form.Child);

        form.Dispose();

        Assert.True(form.IsDisposed);
        Assert.True(form.Child.AliveDuringDispose);
        Assert.True(form.Worker.AliveDuringDispose);
        Assert.Equal(1, form.Dependency.DisposeCount);
        Assert.Equal(Environment.CurrentManagedThreadId, form.Dependency.DisposeThread);
        Assert.Equal(0, other.Dependency.DisposeCount);
        Assert.Null(form.RuntimeDispose);
        first.Dispose();
        form.Dispose();
        Assert.Equal(1, form.Dependency.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => first.Assign(form.Child, (_, _) => { }));
    }

    [WinFormsFact]
    public void OwnerClose_DisposesDescendantsBeforeOwnerAndRejectsCrossOwnerUse()
    {
        List<string> log = [];
        using ServiceProvider provider = CreateProvider(log);
        using WinFormsUiScope application = new(provider);
        using WinFormsUiScope otherApplication = new(provider);
        WinFormsFormScope parent = CreateForm(application, "parent");
        WinFormsFormScope child = CreateForm(application, "child", parent);
        _ = CreateForm(application, "grandchild", child);
        Assert.Throws<InvalidOperationException>(() => otherApplication.CreateForm(_ => new Form(), parent));
        Assert.Throws<InvalidOperationException>(() =>
            parent.Assign(((ScopedForm)child.Form).Child, (_, _) => { }));
        Assert.Throws<InvalidOperationException>(() => application.CreateForm(_ => parent.Form));
        Assert.False(parent.Form.IsDisposed);

        parent.Form.Close();

        Assert.Equal(
            ["parent UI", "child UI", "grandchild UI", "grandchild service", "child service", "parent service"],
            log);
        Assert.Throws<ObjectDisposedException>(() => application.CreateForm(_ => new Form(), parent));
    }

    [WinFormsFact]
    public void CloseVetoHideAndHandleRecreation_DoNotEndScope()
    {
        List<string> log = [];
        using ServiceProvider provider = CreateProvider(log);
        using WinFormsUiScope application = new(provider);
        WinFormsFormScope lifetime = CreateForm(application, "form");
        var form = (ScopedForm)lifetime.Form;
        bool cancel = true;
        form.FormClosing += (_, e) => e.Cancel = cancel;
        lifetime.Show();
        form.Close();
        Assert.False(form.IsDisposed);
        Assert.Equal(0, form.Dependency.DisposeCount);
        form.Hide();
        form.Recreate();
        Assert.Equal(0, form.Dependency.DisposeCount);
        lifetime.Show();
        cancel = false;
        form.Close();
        Assert.True(form.IsDisposed);
        Assert.Equal(1, form.Dependency.DisposeCount);
    }

    [WinFormsFact]
    public void FactoryAndOwnerFailures_CleanAcceptedUiAndNewScope()
    {
        List<string> log = [];
        using ServiceProvider provider = CreateProvider(log);
        using WinFormsUiScope application = new(provider);
        Dependency? dependency = null;
        InvalidOperationException expected = new("Construction failed.");
        Assert.Same(expected, Assert.Throws<InvalidOperationException>(() => application.CreateForm(services =>
        {
            dependency = services.GetRequiredService<Dependency>();
            throw expected;
        })));
        Assert.Equal(1, dependency!.DisposeCount);

        InvalidOperationException cleanupFailure = new("Activation scope cleanup failed.");
        AggregateException combined = Assert.Throws<AggregateException>(() => application.CreateForm(services =>
        {
            services.GetRequiredService<Dependency>().DisposeFailure = cleanupFailure;
            throw expected;
        }));
        Assert.Equal<Exception>([expected, cleanupFailure], combined.InnerExceptions);

        WinFormsFormScope parent = CreateForm(application, "parent");
        ScopedForm? abandoned = null;
        Assert.Throws<ObjectDisposedException>(() => application.CreateForm(services =>
        {
            parent.Dispose();
            return abandoned = new ScopedForm(services.GetRequiredService<Dependency>(), "abandoned");
        }, parent));
        Assert.True(abandoned!.IsDisposed);
        Assert.Equal(1, abandoned.Dependency.DisposeCount);
    }

    [WinFormsFact]
    public void ScopeCreationFailure_DoesNotInvokeFactoryOrLoseApplicationScope()
    {
        FailingScopeFactory provider = new();
        using WinFormsUiScope application = new(provider);
        bool invoked = false;
        Assert.Same(provider.Failure, Assert.Throws<InvalidOperationException>(() =>
            application.CreateForm(_ =>
            {
                invoked = true;
                return new Form();
            })));
        Assert.False(invoked);
        Assert.False(provider.ApplicationScope.Disposed);
        application.Dispose();
        Assert.True(provider.ApplicationScope.Disposed);
    }

    [WinFormsFact]
    public void ContextFactory_CannotTransferAnotherApplicationsFormOwnership()
    {
        List<string> log = [];
        using ServiceProvider provider = CreateProvider(log);
        using WinFormsUiScope application = new(provider);
        using WinFormsUiScope otherApplication = new(provider);
        WinFormsFormScope foreign = CreateForm(otherApplication, "foreign");
        using ApplicationContext context = new(foreign.Form);

        Assert.Throws<InvalidOperationException>(() => application.CreateContext((_, _) => context));

        Assert.False(foreign.Form.IsDisposed);
        Assert.Equal(0, ((ScopedForm)foreign.Form).Dependency.DisposeCount);
    }

    [WinFormsFact]
    public void ShowDialogFailure_DisposesPreviouslyAcceptedUiAndItsScope()
    {
        List<string> log = [];
        using ServiceProvider provider = CreateProvider(log);
        using WinFormsUiScope application = new(provider);
        WinFormsFormScope lifetime = CreateForm(application, "form");
        lifetime.Show();

        Assert.Throws<InvalidOperationException>(() => lifetime.ShowDialog());

        Assert.True(lifetime.Form.IsDisposed);
        Assert.Equal(1, ((ScopedForm)lifetime.Form).Dependency.DisposeCount);
    }

    [WinFormsFact]
    public void DisposalFailures_PreserveUiAndServiceErrorsAndContinueIndependentCleanup()
    {
        List<string> log = [];
        using ServiceProvider provider = CreateProvider(log);
        using WinFormsUiScope application = new(provider);
        WinFormsFormScope first = CreateForm(application, "first");
        WinFormsFormScope second = CreateForm(application, "second");
        var firstForm = (ScopedForm)first.Form;
        InvalidOperationException uiFailure = new("UI cleanup failed.");
        InvalidOperationException serviceFailure = new("Service cleanup failed.");
        firstForm.Disposed += (_, _) => throw uiFailure;
        firstForm.Dependency.DisposeFailure = serviceFailure;

        AggregateException failure = Assert.Throws<AggregateException>(application.Dispose);

        Assert.Contains(uiFailure, failure.Flatten().InnerExceptions);
        Assert.Contains(serviceFailure, failure.Flatten().InnerExceptions);
        Assert.True(second.Form.IsDisposed);
        Assert.Equal(1, ((ScopedForm)second.Form).Dependency.DisposeCount);
        Assert.Equal(1, firstForm.Dependency.DisposeCount);
        Assert.Null(firstForm.RuntimeDispose);
        application.Dispose();
        Assert.Equal(1, firstForm.Dependency.DisposeCount);
    }

    [WinFormsFact]
    public void DerivedDisposeFailureBeforeBase_StillReleasesScopeThroughLifetimeOwner()
    {
        List<string> log = [];
        using ServiceProvider provider = CreateProvider(log);
        using WinFormsUiScope application = new(provider);
        Dependency? dependency = null;
        ThrowingForm? form = null;
        WinFormsFormScope lifetime = application.CreateForm(services =>
        {
            dependency = services.GetRequiredService<Dependency>();
            return form = new ThrowingForm();
        });

        Assert.Same(form!.Failure, Assert.Throws<InvalidOperationException>(lifetime.Dispose));
        Assert.Equal(1, dependency!.DisposeCount);
        Assert.Null(form.RuntimeDispose);
        form.ThrowOnDispose = false;
        form.Dispose();
    }

    [WinFormsFact]
    public void AsyncOnlyService_IsExplicitlyUnsupportedBySynchronousScopeDisposal()
    {
        using ServiceProvider provider = new ServiceCollection().AddScoped<AsyncDependency>().BuildServiceProvider();
        using WinFormsUiScope application = new(provider);
        AsyncDependency? dependency = null;
        WinFormsFormScope lifetime = application.CreateForm(services =>
        {
            dependency = services.GetRequiredService<AsyncDependency>();
            return new Form();
        });

        Assert.Throws<InvalidOperationException>(lifetime.Dispose);
        Assert.True(lifetime.Form.IsDisposed);
        Assert.Equal(0, dependency!.DisposeCount);
    }

    [WinFormsFact]
    public void WrongThreadAndStopping_RejectBeforeFactoryAndLeaveOwnerUsableForCleanup()
    {
        List<string> log = [];
        using ServiceProvider provider = CreateProvider(log);
        using WinFormsUiScope application = new(provider);
        WinFormsFormScope lifetime = CreateForm(application, "form");
        Exception? failure = null;
        Thread thread = new(() => failure = Record.Exception(lifetime.Dispose)) { IsBackground = true };
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)));
        Assert.IsType<InvalidOperationException>(failure);
        Assert.False(lifetime.Form.IsDisposed);
        application.BeginStopping();
        bool called = false;
        Assert.Throws<InvalidOperationException>(() => application.CreateForm(_ =>
        {
            called = true;
            return new Form();
        }));
        Assert.False(called);
        application.Dispose();
        Assert.True(lifetime.Form.IsDisposed);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Run_ModalAndModelessScopesFinishBeforeApplicationScopeAndHost(
        bool componentManager, bool disposeOwnerDuringModal)
    {
        using AppContextSwitchScope switchScope = new("Switch.System.Windows.Forms.EnableMsoComponentManager", componentManager);
        RunOnSta(() =>
        {
            List<string> log = [];
            Dependency? applicationDependency = null;
            ScopedForm? main = null;
            ScopedForm? hidden = null;
            ScopedForm? dialog = null;
            RootDependency? root = null;
            Exception? callbackFailure = null;
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseScopedApplicationContext((services, scopes) =>
                {
                    applicationDependency = services.GetRequiredService<Dependency>();
                    applicationDependency.Name = "application";
                    root = services.GetRequiredService<RootDependency>();
                    WinFormsFormScope mainLifetime = CreateForm(scopes, "main");
                    main = (ScopedForm)mainLifetime.Form;
                    hidden = (ScopedForm)CreateForm(scopes, "hidden").Form;
                    main.Shown += (_, _) => main.BeginInvoke(() =>
                    {
                        try
                        {
                            WinFormsFormScope modal = CreateForm(scopes, "dialog", mainLifetime);
                            dialog = (ScopedForm)modal.Form;
                            dialog.Shown += (_, _) => dialog.BeginInvoke(() =>
                            {
                                try
                                {
                                    Assert.Throws<InvalidOperationException>(() => modal.ShowDialog());
                                    if (disposeOwnerDuringModal)
                                    {
                                        WinFormsFormScope nested = CreateForm(scopes, "nested", modal);
                                        var nestedForm = (ScopedForm)nested.Form;
                                        nestedForm.Shown += (_, _) => nestedForm.BeginInvoke(() =>
                                        {
                                            try
                                            {
                                                main.Dispose();
                                                Assert.Equal(0, main.Dependency.DisposeCount);
                                                Assert.Equal(0, dialog.Dependency.DisposeCount);
                                                Assert.Equal(0, nestedForm.Dependency.DisposeCount);
                                            }
                                            catch (Exception exception)
                                            {
                                                callbackFailure = exception;
                                                nestedForm.Close();
                                            }
                                        });
                                        nested.ShowDialog();
                                        Assert.Equal(1, nestedForm.Dependency.DisposeCount);
                                        Assert.Equal(0, main.Dependency.DisposeCount);
                                        Assert.Equal(0, dialog.Dependency.DisposeCount);
                                    }
                                    else
                                    {
                                        dialog.DialogResult = DialogResult.OK;
                                    }
                                }
                                catch (Exception exception)
                                {
                                    callbackFailure = exception;
                                    dialog.Close();
                                }
                            });
                            DialogResult result = modal.ShowDialog();
                            if (!disposeOwnerDuringModal)
                            {
                                Assert.Equal(DialogResult.OK, result);
                                Assert.Equal(0, main.Dependency.DisposeCount);
                            }

                            Assert.True(dialog.IsDisposed);
                            Assert.Equal(1, dialog.Dependency.DisposeCount);
                            Assert.Equal(0, applicationDependency.DisposeCount);
                        }
                        catch (Exception exception)
                        {
                            callbackFailure ??= exception;
                        }
                        finally
                        {
                            main.Close();
                        }
                    });
                    return new ApplicationContext(main);
                })
                .UseHost(new HostBuilder().ConfigureServices((_, services) =>
                {
                    services.AddSingleton(new DisposalLog(log));
                    services.AddSingleton<RootDependency>();
                    services.AddScoped<Dependency>();
                }))
                .Build();
            application.Run(TestContext.Current.CancellationToken);
            if (callbackFailure is not null)
            {
                ExceptionDispatchInfo.Throw(callbackFailure);
            }

            Assert.NotNull(dialog);
            Assert.True(main!.IsDisposed);
            Assert.True(hidden!.IsDisposed);
            Assert.Equal(1, main.Dependency.DisposeCount);
            Assert.Equal(1, hidden.Dependency.DisposeCount);
            Assert.Equal(1, applicationDependency!.DisposeCount);
            Assert.True(application.UiCompletion.IsCompletedSuccessfully);
            Assert.True(root!.Disposed);
            Assert.True(log.IndexOf("dialog service") < log.IndexOf("main service"));
            Assert.True(log.IndexOf("main service") < log.IndexOf("application service"));
            Assert.True(log.IndexOf("hidden service") < log.IndexOf("application service"));
            Assert.Equal("host", log[^1]);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_ContextNavigationAndStopping_KeepIndependentScopesTracked(bool componentManager)
    {
        using AppContextSwitchScope switchScope = new("Switch.System.Windows.Forms.EnableMsoComponentManager", componentManager);
        RunOnSta(() =>
        {
            List<string> log = [];
            Exception? callbackFailure = null;
            ScopedForm? first = null;
            ScopedForm? second = null;
            WinFormsUiScope? scopes = null;
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseScopedApplicationContext((_, lifetimes) =>
                {
                    scopes = lifetimes;
                    WinFormsFormScope initial = CreateForm(lifetimes, "first");
                    first = (ScopedForm)initial.Form;
                    ApplicationContext context = new(first);
                    first.Shown += (_, _) => first.BeginInvoke(() =>
                    {
                        try
                        {
                            WinFormsFormScope next = CreateForm(lifetimes, "second");
                            second = (ScopedForm)next.Form;
                            context.MainForm = second;
                            next.Show();
                            first.Close();
                            Assert.Equal(1, first.Dependency.DisposeCount);
                            Assert.Equal(0, second.Dependency.DisposeCount);
                        }
                        catch (Exception exception)
                        {
                            callbackFailure = exception;
                        }
                        finally
                        {
                            context.ExitThread();
                        }
                    });
                    return context;
                })
                .UseHost(new HostBuilder().ConfigureServices((_, services) =>
                {
                    services.AddSingleton(new DisposalLog(log));
                    services.AddSingleton<RootDependency>();
                    services.AddScoped<Dependency>();
                }))
                .Build();
            application.Lifetime.ApplicationStopping += (_, _) =>
                Assert.Throws<InvalidOperationException>(() => scopes!.CreateForm(_ => new Form()));
            application.Run(TestContext.Current.CancellationToken);
            if (callbackFailure is not null)
            {
                ExceptionDispatchInfo.Throw(callbackFailure);
            }

            Assert.True(first!.IsDisposed);
            Assert.True(second!.IsDisposed);
            Assert.Equal(1, second.Dependency.DisposeCount);
        });
    }

    [WinFormsFact]
    public void ScopedBuilder_RequiresHostAndDefersActivationThroughUnrunDisposal()
    {
        int calls = 0;
        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder();
        Assert.Throws<ArgumentNullException>(() => builder.UseScopedStartupForm(null!));
        Assert.Throws<ArgumentNullException>(() => builder.UseScopedApplicationContext(null!));
        builder.UseScopedStartupForm(_ =>
        {
            calls++;
            return new Form();
        });
        Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Throws<InvalidOperationException>(() => builder.UseStartupForm<Form>());
        Assert.Throws<InvalidOperationException>(() => builder.UseStartupFormFactory(_ => new Form()));
        using WinFormsApplication application = builder.UseHost(new HostBuilder()).Build();
        application.Dispose();
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_ScopedStartupCancellation_CleansScopesDespiteCloseVeto(bool componentManager)
    {
        using AppContextSwitchScope switchScope = new("Switch.System.Windows.Forms.EnableMsoComponentManager", componentManager);
        RunOnSta(() =>
        {
            List<string> log = [];
            ScopedForm? form = null;
            using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseScopedStartupForm(services =>
                {
                    form = new ScopedForm(services.GetRequiredService<Dependency>(), "startup");
                    form.FormClosing += (_, e) => e.Cancel = true;
                    form.Shown += (_, _) => form.BeginInvoke(cancellation.Cancel);
                    return form;
                })
                .UseHost(new HostBuilder().ConfigureServices((_, services) =>
                {
                    services.AddSingleton(new DisposalLog(log));
                    services.AddSingleton<RootDependency>();
                    services.AddScoped<Dependency>();
                }))
                .Build();

            Assert.Throws<OperationCanceledException>(() => application.Run(cancellation.Token));
            Assert.True(form!.IsDisposed);
            Assert.Equal(1, form.Dependency.DisposeCount);
            Assert.True(application.UiCompletion.IsCompletedSuccessfully);
            Assert.Equal("host", log[^1]);
        });
    }

    [Fact]
    public void FailedContextActivation_CleansUnshownTrackedFormsAndApplicationScope()
    {
        RunOnSta(() =>
        {
            List<string> log = [];
            ScopedForm? form = null;
            Dependency? applicationDependency = null;
            InvalidOperationException expected = new("Context activation failed.");
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseScopedApplicationContext((services, scopes) =>
                {
                    applicationDependency = services.GetRequiredService<Dependency>();
                    applicationDependency.Name = "application";
                    form = (ScopedForm)CreateForm(scopes, "hidden").Form;
                    throw expected;
                })
                .UseHost(new HostBuilder().ConfigureServices((_, services) =>
                {
                    services.AddSingleton(new DisposalLog(log));
                    services.AddSingleton<RootDependency>();
                    services.AddScoped<Dependency>();
                }))
                .Build();

            Assert.Same(expected, Assert.Throws<InvalidOperationException>(() => application.Run(TestContext.Current.CancellationToken)));
            Assert.True(form!.IsDisposed);
            Assert.Equal(1, form.Dependency.DisposeCount);
            Assert.Equal(1, applicationDependency!.DisposeCount);
            Assert.Equal("host", log[^1]);
        });
    }

    private static ServiceProvider CreateProvider(List<string> log)
        => new ServiceCollection()
            .AddSingleton(new DisposalLog(log))
            .AddSingleton<RootDependency>()
            .AddScoped<Dependency>()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

    private static WinFormsFormScope CreateForm(WinFormsUiScope application, string name, WinFormsFormScope? owner = null)
        => application.CreateForm(services => new ScopedForm(services.GetRequiredService<Dependency>(), name), owner);

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
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "The scoped application did not finish.");
        failure?.Throw();
    }

    /// <summary>
    ///  Shares a deterministic, single-thread-at-a-time disposal trace.
    /// </summary>
    private sealed class DisposalLog(List<string> entries)
    {
        internal List<string> Entries { get; } = entries;
    }

    /// <summary>
    ///  Records disposal of the host-owned singleton after all UI scope cleanup.
    /// </summary>
    private sealed class RootDependency(DisposalLog log) : IDisposable
    {
        internal bool Disposed { get; private set; }

        public void Dispose()
        {
            Disposed = true;
            log.Entries.Add("host");
        }
    }

    /// <summary>
    ///  Provides identity, ordering, thread-affinity, and failure observations for a real scoped service.
    /// </summary>
    private sealed class Dependency(DisposalLog log, RootDependency root) : IDisposable
    {
        internal string Name { get; set; } = "unnamed";

        internal int DisposeCount { get; private set; }

        internal int DisposeThread { get; private set; }

        internal Exception? DisposeFailure { get; set; }

        internal void LogUiDisposal()
        {
            Assert.Equal(0, DisposeCount);
            Assert.False(root.Disposed);
            log.Entries.Add($"{Name} UI");
        }

        public void Dispose()
        {
            DisposeCount++;
            DisposeThread = Environment.CurrentManagedThreadId;
            Assert.False(root.Disposed);
            log.Entries.Add($"{Name} service");
            if (DisposeFailure is not null)
            {
                throw DisposeFailure;
            }
        }
    }

    /// <summary>
    ///  Performs derived cleanup before base disposal and owns its service-free child graph.
    /// </summary>
    private sealed class ScopedForm : Form
    {
        private readonly Container _components = new();

        internal ScopedForm(Dependency dependency, string name)
        {
            Dependency = dependency;
            dependency.Name = name;
            Controls.Add(Child);
            _components.Add(Worker);
        }

        internal Dependency Dependency { get; }

        internal BorrowingControl Child { get; } = new();

        internal BorrowingComponent Worker { get; } = new();

        internal void Recreate() => RecreateHandle();

        protected override void Dispose(bool disposing)
        {
            if (disposing && !IsDisposed)
            {
                Dependency.LogUiDisposal();
                _components.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    ///  Uses a borrowed dependency while releasing control resources.
    /// </summary>
    private sealed class BorrowingControl : UserControl
    {
        internal Dependency? Dependency { get; set; }

        internal bool AliveDuringDispose { get; private set; }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                AliveDuringDispose = Dependency?.DisposeCount == 0;
            }
        }
    }

    /// <summary>
    ///  Uses its form's borrowed dependency without owning or disposing it.
    /// </summary>
    private sealed class BorrowingComponent : Component
    {
        internal Dependency? Dependency { get; set; }

        internal bool AliveDuringDispose { get; private set; }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                AliveDuringDispose = Dependency?.DisposeCount == 0;
            }
        }
    }

    /// <summary>
    ///  Throws before base disposal to exercise the lifetime owner's failure fallback.
    /// </summary>
    private sealed class ThrowingForm : Form
    {
        internal InvalidOperationException Failure { get; } = new("Derived disposal failed.");

        internal bool ThrowOnDispose { get; set; } = true;

        protected override void Dispose(bool disposing)
        {
            if (disposing && ThrowOnDispose)
            {
                throw Failure;
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    ///  Demonstrates an unsupported async-only dependency without holding external resources.
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

    /// <summary>
    ///  Fails form-scope creation after successfully providing an application scope.
    /// </summary>
    private sealed class FailingScopeFactory : IServiceProvider, IServiceScopeFactory
    {
        private int _calls;

        internal InvalidOperationException Failure { get; } = new("Scope creation failed.");

        internal StubScope ApplicationScope { get; } = new();

        public object? GetService(Type serviceType)
            => serviceType == typeof(IServiceScopeFactory) ? this : null;

        public IServiceScope CreateScope()
            => ++_calls == 1 ? ApplicationScope : throw Failure;
    }

    /// <summary>
    ///  Records application-scope disposal when subsequent form-scope creation fails.
    /// </summary>
    private sealed class StubScope : IServiceScope
    {
        internal bool Disposed { get; private set; }

        public IServiceProvider ServiceProvider
            => throw new InvalidOperationException("No services should be resolved in this test.");

        public void Dispose() => Disposed = true;
    }
}
