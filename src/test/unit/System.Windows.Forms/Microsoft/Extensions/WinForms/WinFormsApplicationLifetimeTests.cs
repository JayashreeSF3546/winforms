// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.WinForms;

namespace System.Windows.Forms.Tests;

/// <summary>
///  Tests lifetime transitions independently of message-loop execution.
/// </summary>
public class WinFormsApplicationLifetimeTests
{
    [Fact]
    public void Notifications_RepeatedAndOutOfOrder_RaiseEachMilestoneOnce()
    {
        WinFormsApplicationLifetime lifetime = new();
        List<string> events = Subscribe(lifetime);

        lifetime.NotifyStarted();
        lifetime.NotifyStarted();
        lifetime.NotifyStopping();
        lifetime.NotifyStarted();
        lifetime.NotifyStopping();
        lifetime.NotifyStopped();
        lifetime.NotifyStopped();
        lifetime.NotifyStopping();
        lifetime.NotifyStarted();

        events.Should().Equal("started", "stopping", "stopped");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Notifications_WithoutSuccessfulStartup_OmitStarted(bool requestStoppingFirst)
    {
        WinFormsApplicationLifetime lifetime = new();
        List<string> events = Subscribe(lifetime);

        if (requestStoppingFirst)
        {
            lifetime.NotifyStopping();
        }

        lifetime.NotifyStopped();
        lifetime.NotifyStarted();

        events.Should().Equal("stopping", "stopped");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Notifications_ReentrantStop_CompletesCurrentNotificationBeforeAdvancing(bool duringStopping)
    {
        WinFormsApplicationLifetime lifetime = new();
        List<string> events = [];
        EventHandler requestStop = (_, _) =>
        {
            events.Add("request");
            lifetime.NotifyStopped();
            lifetime.NotifyStarted();
        };

        if (duringStopping)
        {
            lifetime.ApplicationStopping += requestStop;
        }
        else
        {
            lifetime.ApplicationStarted += requestStop;
        }

        lifetime.ApplicationStarted += (_, _) => events.Add("started");
        lifetime.ApplicationStopping += (_, _) => events.Add("stopping");
        lifetime.ApplicationStopped += (_, _) => events.Add("stopped");

        lifetime.NotifyStarted();
        lifetime.NotifyStopping();
        lifetime.NotifyStopped();

        events.Should().Equal(duringStopping
            ? ["started", "request", "stopping", "stopped"]
            : ["request", "started", "stopping", "stopped"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Notifications_SubscriberFailure_PreservesFailureAndNotifiesRemainingSubscribers(int milestone)
    {
        WinFormsApplicationLifetime lifetime = new();
        InvalidOperationException expected = new("Subscriber failed.");
        EventHandler fail = (_, _) => throw expected;
        switch (milestone)
        {
            case 0:
                lifetime.ApplicationStarted += fail;
                break;
            case 1:
                lifetime.ApplicationStopping += fail;
                break;
            default:
                lifetime.ApplicationStopped += fail;
                break;
        }

        List<string> events = Subscribe(lifetime);
        Action notify = milestone switch
        {
            0 => lifetime.NotifyStarted,
            1 => lifetime.NotifyStopping,
            _ => lifetime.NotifyStopped
        };

        Assert.Throws<InvalidOperationException>(notify).Should().BeSameAs(expected);
        notify();
        lifetime.NotifyStopped();

        events.Should().Equal(milestone == 0
            ? ["started", "stopping", "stopped"]
            : ["stopping", "stopped"]);
    }

    [Fact]
    public void Notifications_MultipleFailuresAndReentrantStop_AggregateAfterAllTransitions()
    {
        WinFormsApplicationLifetime lifetime = new();
        InvalidOperationException first = new("First failure.");
        ArgumentException second = new("Second failure.");
        lifetime.ApplicationStarted += (_, _) =>
        {
            lifetime.NotifyStopped();
            throw first;
        };
        lifetime.ApplicationStopping += (_, _) => throw second;
        List<string> events = Subscribe(lifetime);

        AggregateException exception = Assert.Throws<AggregateException>(() => lifetime.NotifyStarted());

        exception.InnerExceptions.Should().Equal(first, second);
        events.Should().Equal("started", "stopping", "stopped");
        lifetime.NotifyStopped();
        events.Should().HaveCount(3);
    }

    [Fact]
    public void Notifications_OnAnotherThread_RejectWithoutChangingState()
    {
        WinFormsApplicationLifetime lifetime = new();
        List<string> events = Subscribe(lifetime);
        Exception? exception = null;
        Thread thread = new(() => exception = Record.Exception(() => lifetime.NotifyStopped()));

        thread.Start();
        thread.Join();

        exception.Should().BeOfType<InvalidOperationException>();
        events.Should().BeEmpty();
        lifetime.NotifyStarted();
        lifetime.NotifyStopped();
        events.Should().Equal("started", "stopping", "stopped");
    }

    private static List<string> Subscribe(WinFormsApplicationLifetime lifetime)
    {
        List<string> events = [];
        lifetime.ApplicationStarted += (sender, e) =>
        {
            sender.Should().BeSameAs(lifetime);
            e.Should().BeSameAs(EventArgs.Empty);
            events.Add("started");
        };
        lifetime.ApplicationStopping += (_, _) => events.Add("stopping");
        lifetime.ApplicationStopped += (_, _) => events.Add("stopped");

        return events;
    }
}
