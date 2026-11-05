// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ApplicationBuilderAdoption;

/// <summary>
///  Exercises real UI commands only when an integration test explicitly selects a scenario.
/// </summary>
internal sealed class SampleProbe(IConfiguration configuration)
{
    internal string Scenario { get; } = configuration["Sample:Scenario"] ?? "";
    internal Exception? Failure { get; set; }

    internal void OnShown(Form form, Func<Task> exercise)
    {
        form.Shown += (_, _) => Console.WriteLine("UI ACTIVATED");
        form.Disposed += (_, _) => Console.WriteLine("UI DISPOSED");
        if (Scenario.Length == 0)
        {
            return;
        }

        form.Shown += (_, _) => form.BeginInvoke((Action)(async () =>
        {
            try
            {
                await exercise();
            }
            catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
            {
                Failure = exception;
                form.Close();
            }
        }));
    }

    internal void Complete(string result)
    {
        if (Failure is not null)
        {
            ExceptionDispatchInfo.Capture(Failure).Throw();
        }

        Console.WriteLine($"{(Scenario.Length == 0 ? "COMPLETE" : "PASS")} {result}");
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

/// <summary>
///  Makes host startup and normal host shutdown ordering observable without retaining UI objects.
/// </summary>
internal sealed class HostTrace : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("HOST STARTED");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("HOST STOPPED");

        return Task.CompletedTask;
    }
}
