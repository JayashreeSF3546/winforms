// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.ExceptionServices;
using Microsoft.Extensions.WinForms;

namespace System.Windows.Forms.UITests;

/// <summary>
///  Exercises the public runner with native windows and dispatched UI continuations.
/// </summary>
[Collection(nameof(WinFormsApplicationTests))]
[CollectionDefinition(nameof(WinFormsApplicationTests), DisableParallelization = true)]
public class WinFormsApplicationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Run_PostedAsyncContinuation_UsesOwningStaAndRestoresEntryContext(bool useContext, bool componentManager)
    {
        using AppContextSwitchScope scope = new("Switch.System.Windows.Forms.EnableMsoComponentManager", componentManager);
        RunOnSta(() =>
        {
            int ownerThread = Environment.CurrentManagedThreadId;
            int continuationThread = 0;
            ApartmentState continuationApartment = ApartmentState.Unknown;
            SynchronizationContext? continuationContext = null;
            bool messageLoop = false;
            bool factoryUsedWinFormsContext = false;
            Form? form = null;
            Task? continuation = null;
            var builder = WinFormsApplication.CreateBuilder();
            if (useContext)
            {
                builder.UseApplicationContext(() =>
                {
                    factoryUsedWinFormsContext = SynchronizationContext.Current is WindowsFormsSynchronizationContext;
                    form = new Form();
                    return new ApplicationContext(form);
                });
            }
            else
            {
                form = new Form();
                builder.UseStartupForm(form);
            }

            SynchronizationContext? previousContext = SynchronizationContext.Current;
            using WinFormsApplication application = builder.Build();
            List<(string Event, int Thread)> events = [];
            application.Lifetime.ApplicationStarted += (_, _) =>
            {
                events.Add(("started", Environment.CurrentManagedThreadId));
                form!.Shown += (_, _) => continuation = ObserveContinuationAsync();
            };
            async Task ObserveContinuationAsync()
            {
                try
                {
                    await Task.Yield();
                    continuationThread = Environment.CurrentManagedThreadId;
                    continuationApartment = Thread.CurrentThread.GetApartmentState();
                    continuationContext = SynchronizationContext.Current;
                    messageLoop = Application.MessageLoop;
                }
                finally
                {
                    form!.Close();
                }
            }

            application.Lifetime.ApplicationStopping += (_, _) =>
                events.Add(("stopping", Environment.CurrentManagedThreadId));
            application.Lifetime.ApplicationStopped += (_, _) =>
                events.Add(("stopped", Environment.CurrentManagedThreadId));

            application.Run();

            Assert.NotNull(continuation);
            Assert.True(continuation.IsCompletedSuccessfully, continuation.Exception?.ToString());
            Assert.Equal(ownerThread, continuationThread);
            Assert.Equal(ApartmentState.STA, continuationApartment);
            Assert.IsType<WindowsFormsSynchronizationContext>(continuationContext);
            Assert.True(messageLoop);
            Assert.Equal(useContext, factoryUsedWinFormsContext);
            Assert.Equal(
                new[] { ("started", ownerThread), ("stopping", ownerThread), ("stopped", ownerThread) },
                events);
            Assert.True(form!.IsDisposed);
            Assert.False(form.IsHandleCreated);
            Assert.False(Application.MessageLoop);
            Assert.Same(previousContext, SynchronizationContext.Current);
            (previousContext as IDisposable)?.Dispose();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_ContextChangesMainForm_OldFormCloseDoesNotStopRootLoop(bool componentManager)
    {
        using AppContextSwitchScope scope = new("Switch.System.Windows.Forms.EnableMsoComponentManager", componentManager);
        RunOnSta(() =>
        {
            Form? first = null;
            Form? second = null;
            bool dispatchedAfterOldFormClosed = false;
            List<string> events = [];
            using WinFormsApplication application = WinFormsApplication.CreateBuilder()
                .UseApplicationContext(() =>
                {
                    first = new Form();
                    ApplicationContext context = new(first);
                    first.Shown += (_, _) => first.BeginInvoke(() =>
                    {
                        second = new Form();
                        context.MainForm = second;
                        second.Shown += (_, _) => second.BeginInvoke(() =>
                        {
                            first.Close();
                            events.Add("old form closed");
                            second.BeginInvoke(() =>
                            {
                                dispatchedAfterOldFormClosed = Application.MessageLoop;
                                second.Close();
                            });
                        });
                        second.Show();
                    });
                    return context;
                })
                .Build();
            application.Lifetime.ApplicationStopping += (_, _) => events.Add("stopping");
            application.Lifetime.ApplicationStopped += (_, _) => events.Add("stopped");

            application.Run();

            Assert.True(dispatchedAfterOldFormClosed);
            Assert.Equal(new[] { "old form closed", "stopping", "stopped" }, events);
            Assert.True(first!.IsDisposed);
            Assert.True(second!.IsDisposed);
            Assert.False(Application.MessageLoop);
            Assert.Null(SynchronizationContext.Current);
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "The caller-owned STA did not finish shutdown.");
        failure?.Throw();
    }
}
