// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.WinForms;

namespace System.Windows.Forms.Tests;

/// <summary>
///  Tests the builder prototype without starting an application message loop.
/// </summary>
public class WinFormsApplicationBuilderTests
{
    [WinFormsFact]
    public void Build_GenericStartup_IsDeferredAndActivatedOnce()
    {
        int initialCount = StartupForm.CreationCount;
        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder();
        builder.UseStartupForm<StartupForm>().Should().BeSameAs(builder);
        using WinFormsApplication application = builder.Build();

        StartupForm.CreationCount.Should().Be(initialCount);
        ApplicationContext context = application.ActivateStartup();
        context.MainForm.Should().BeOfType<StartupForm>();
        context.MainForm!.IsHandleCreated.Should().BeFalse();
        context.MainForm.Visible.Should().BeFalse();
        StartupForm.CreationCount.Should().Be(initialCount + 1);
        Assert.Throws<InvalidOperationException>(() => application.ActivateStartup());
        StartupForm.CreationCount.Should().Be(initialCount + 1);

        application.Dispose();
        context.MainForm.Should().BeNull();
    }

    [WinFormsFact]
    public void Build_ExistingForm_TransfersOwnershipWithoutCreatingHandle()
    {
        using Form form = new();
        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder();
        builder.UseStartupForm(form).Should().BeSameAs(builder);
        using WinFormsApplication application = builder.Build();

        form.IsHandleCreated.Should().BeFalse();
        form.IsDisposed.Should().BeFalse();
        ApplicationContext context = application.ActivateStartup();
        context.MainForm.Should().BeSameAs(form);
        form.IsHandleCreated.Should().BeFalse();

        application.Dispose();
        form.IsDisposed.Should().BeTrue();
    }

    [WinFormsFact]
    public void Build_ContextFactory_IsDeferredAndPreservesContext()
    {
        int calls = 0;
        using Form form = new();
        TrackingContext context = new(form);
        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder();
        builder.UseApplicationContext(() =>
        {
            calls++;
            return context;
        }).Should().BeSameAs(builder);

        using WinFormsApplication application = builder.Build();
        calls.Should().Be(0);
        application.ActivateStartup().Should().BeSameAs(context);
        calls.Should().Be(1);
        context.MainForm.Should().BeSameAs(form);

        application.Dispose();
        application.Dispose();
        context.DisposeCount.Should().Be(1);
        form.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public void Build_MissingStartup_DoesNotConsumeBuilder()
    {
        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder();
        Assert.Throws<InvalidOperationException>(() => builder.Build());

        builder.UseApplicationContext(() => new ApplicationContext());
        using WinFormsApplication application = builder.Build();
    }

    [WinFormsTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void StartupSelection_Conflicts_DoNotReplaceSelection(int selection)
    {
        using Form form = new();
        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder();
        SelectStartup(builder, form, selection);

        Assert.Throws<InvalidOperationException>(() => builder.UseStartupForm<Form>());
        Assert.Throws<InvalidOperationException>(() => builder.UseStartupForm(form));
        Assert.Throws<InvalidOperationException>(() => builder.UseApplicationContext(() => new ApplicationContext()));
        form.IsDisposed.Should().BeFalse();

        using WinFormsApplication application = builder.Build();
        ApplicationContext context = application.ActivateStartup();
        if (selection == 1)
        {
            context.MainForm.Should().BeSameAs(form);
        }
        else
        {
            context.MainForm.Should().NotBeSameAs(form);
        }
    }

    [WinFormsTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Build_Success_FreezesBuilder(int selection)
    {
        using Form form = new();
        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder();
        SelectStartup(builder, form, selection);
        using WinFormsApplication application = builder.Build();

        Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Throws<InvalidOperationException>(() => builder.UseStartupForm<Form>());
        Assert.Throws<InvalidOperationException>(() => builder.UseStartupForm(form));
        Assert.Throws<InvalidOperationException>(() => builder.UseApplicationContext(() => new ApplicationContext()));
    }

    [Fact]
    public void StartupSelection_Null_DoesNotConsumeSelection()
    {
        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder();
        Assert.Throws<ArgumentNullException>("startupForm", () => builder.UseStartupForm(null!));
        Assert.Throws<ArgumentNullException>("contextFactory", () => builder.UseApplicationContext(null!));

        builder.UseApplicationContext(() => new ApplicationContext());
        using WinFormsApplication application = builder.Build();
    }

    [WinFormsFact]
    public void UseStartupForm_Disposed_RejectsSelection()
    {
        Form form = new();
        form.Dispose();
        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder();

        Assert.Throws<ObjectDisposedException>(() => builder.UseStartupForm(form));
        builder.UseStartupForm<Form>();
        using WinFormsApplication application = builder.Build();
    }

    [WinFormsFact]
    public void Build_FormDisposedAfterSelection_RejectsBuild()
    {
        Form form = new();
        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder().UseStartupForm(form);
        form.Dispose();

        Assert.Throws<ObjectDisposedException>(() => builder.Build());
    }

    [WinFormsFact]
    public void ActivateStartup_FormDisposedAfterBuild_RejectsActivation()
    {
        Form form = new();
        using WinFormsApplication application = WinFormsApplication.CreateBuilder().UseStartupForm(form).Build();
        form.Dispose();

        Assert.Throws<ObjectDisposedException>(() => application.ActivateStartup());
    }

    [WinFormsFact]
    public void Dispose_UnactivatedInstance_DisposesTransferredFormWithoutLifetimeEvents()
    {
        using Form form = new();
        using WinFormsApplication application = WinFormsApplication.CreateBuilder().UseStartupForm(form).Build();
        List<string> events = [];
        application.Lifetime.ApplicationStarted += (_, _) => events.Add("started");
        application.Lifetime.ApplicationStopping += (_, _) => events.Add("stopping");
        application.Lifetime.ApplicationStopped += (_, _) => events.Add("stopped");

        application.Dispose();
        application.Dispose();

        form.IsDisposed.Should().BeTrue();
        events.Should().BeEmpty();
        Assert.Throws<ObjectDisposedException>(() => application.ActivateStartup());
        Assert.Throws<ObjectDisposedException>(() => application.Run());
    }

    [Fact]
    public void Dispose_UnactivatedFactory_DoesNotInvokeFactory()
    {
        int calls = 0;
        using WinFormsApplication application = WinFormsApplication.CreateBuilder().UseApplicationContext(() =>
        {
            calls++;
            return new ApplicationContext();
        }).Build();

        application.Dispose();
        calls.Should().Be(0);
        Assert.Throws<ObjectDisposedException>(() => application.ActivateStartup());
    }

    [Fact]
    public void ActivateStartup_FactoryFailure_PreservesExceptionAndPreventsRetry()
    {
        InvalidOperationException expected = new("Factory failed.");
        int calls = 0;
        using WinFormsApplication application = WinFormsApplication.CreateBuilder().UseApplicationContext(() =>
        {
            calls++;
            throw expected;
        }).Build();

        Assert.Throws<InvalidOperationException>(() => application.ActivateStartup()).Should().BeSameAs(expected);
        Assert.Throws<InvalidOperationException>(() => application.ActivateStartup()).Should().NotBeSameAs(expected);
        calls.Should().Be(1);
    }

    [WinFormsFact]
    public void ActivateStartup_FormConstructorFailure_IsNotWrapped()
    {
        using WinFormsApplication application = WinFormsApplication.CreateBuilder()
            .UseStartupForm<ThrowingForm>()
            .Build();

        Assert.Throws<InvalidOperationException>(() => application.ActivateStartup())
            .Should().BeSameAs(ThrowingForm.ConstructorException);
    }

    [Fact]
    public void ActivateStartup_NullFactoryResult_RejectsActivation()
    {
        using WinFormsApplication application = WinFormsApplication.CreateBuilder()
            .UseApplicationContext(() => null!)
            .Build();

        Assert.Throws<InvalidOperationException>(() => application.ActivateStartup());
        Assert.Throws<InvalidOperationException>(() => application.ActivateStartup());
    }

    [Fact]
    public void ActivateStartup_ReentrantDisposal_IsRejectedWithoutLosingContextOwnership()
    {
        TrackingContext context = new();
        WinFormsApplication? application = null;
        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder().UseApplicationContext(() =>
        {
            Assert.Throws<InvalidOperationException>(() => application!.Dispose());
            return context;
        });
        using (application = builder.Build())
        {
            application.ActivateStartup().Should().BeSameAs(context);
        }

        context.DisposeCount.Should().Be(1);
    }

    [Fact]
    public void Dispose_ContextFailure_PropagatesWithoutDisposingTwice()
    {
        InvalidOperationException expected = new("Dispose failed.");
        TrackingContext context = new(disposeException: expected);
        using WinFormsApplication application = WinFormsApplication.CreateBuilder()
            .UseApplicationContext(() => context)
            .Build();
        application.ActivateStartup();

        Assert.Throws<InvalidOperationException>(() => application.Dispose()).Should().BeSameAs(expected);
        application.Dispose();
        context.DisposeCount.Should().Be(1);
    }

    [Fact]
    public void Run_AfterDirectActivation_RejectsRepeatedActivation()
    {
        int calls = 0;
        using WinFormsApplication application = WinFormsApplication.CreateBuilder().UseApplicationContext(() =>
        {
            calls++;
            return new ApplicationContext();
        }).Build();

        application.ActivateStartup();
        Assert.Throws<InvalidOperationException>(() => application.Run(TestContext.Current.CancellationToken));
        calls.Should().Be(1);
    }

    [Fact]
    public void Operations_OnAnotherThread_RejectWithoutConsumingOwnership()
    {
        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder()
            .UseApplicationContext(() => new ApplicationContext());
        using WinFormsApplication application = builder.Build();
        WinFormsApplicationBuilder unbuilt = WinFormsApplication.CreateBuilder()
            .UseApplicationContext(() => new ApplicationContext());
        List<Exception?> exceptions = [];
        Thread thread = new(() =>
        {
            exceptions.Add(Record.Exception(() => unbuilt.Build()));
            exceptions.Add(Record.Exception(() => unbuilt.UseStartupForm<Form>()));
            exceptions.Add(Record.Exception(() => application.ActivateStartup()));
            exceptions.Add(Record.Exception(() => application.Dispose()));
            exceptions.Add(Record.Exception(() => application.Run(TestContext.Current.CancellationToken)));
        });

        thread.Start();
        thread.Join();

        exceptions.Should().HaveCount(5);
        exceptions.Should().AllBeOfType<InvalidOperationException>();
        using WinFormsApplication secondApplication = unbuilt.Build();
        application.ActivateStartup();
    }

    [WinFormsFact]
    public void UseStartupForm_FormHandleOnAnotherThread_RejectsWithoutDisposingForm()
    {
        using Form form = new();
        _ = form.Handle;
        Exception? exception = null;
        Thread thread = new(() =>
        {
            WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder();
            exception = Record.Exception(() => builder.UseStartupForm(form));
        });

        thread.Start();
        thread.Join();

        exception.Should().BeOfType<InvalidOperationException>();
        form.IsDisposed.Should().BeFalse();
    }

    private static void SelectStartup(WinFormsApplicationBuilder builder, Form form, int selection)
    {
        switch (selection)
        {
            case 0:
                builder.UseStartupForm<Form>();
                break;
            case 1:
                builder.UseStartupForm(form);
                break;
            default:
                builder.UseApplicationContext(() => new ApplicationContext());
                break;
        }
    }

    /// <summary>
    ///  Counts generic startup construction without creating a native handle.
    /// </summary>
    private sealed class StartupForm : Form
    {
        internal static int CreationCount { get; private set; }

        public StartupForm() => CreationCount++;
    }

    /// <summary>
    ///  Supplies a recognizable startup-constructor failure.
    /// </summary>
    private sealed class ThrowingForm : Form
    {
        internal static InvalidOperationException ConstructorException { get; } = new("Constructor failed.");

        public ThrowingForm() => throw ConstructorException;
    }

    /// <summary>
    ///  Tracks context disposal and optionally reports a cleanup failure.
    /// </summary>
    private sealed class TrackingContext(Form? form = null, Exception? disposeException = null) : ApplicationContext(form)
    {
        internal int DisposeCount { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCount++;
            }

            base.Dispose(disposing);
            if (disposing && disposeException is not null)
            {
                throw disposeException;
            }
        }
    }
}
