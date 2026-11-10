// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Configures the internal lifecycle prototype pending API and compatibility review.
/// </summary>
/// <remarks>
///  <para>
///   Callbacks are synchronous owner-thread operations, not async-void handlers. Settings preparation
///   returns a UI-free persistence operation borrowing existing services; it transfers no ownership.
///   UI dispatch policy remains with the existing exception logging adapter.
///  </para>
/// </remarks>
internal sealed class WinFormsApplicationFeatures
{
    internal IEnumerable<string>? CommandLine { get; set; }

    internal EventHandler<WinFormsStartingEventArgs>? Starting { get; set; }

    internal EventHandler<WinFormsShutdownEventArgs>? Shutdown { get; set; }

    internal Action<Exception, WinFormsLifecyclePhase>? ReportFailure { get; set; }

    internal Func<WinFormsApplication, Func<Task>>? PrepareSettingsSave { get; set; }

    internal Func<WinFormsSplashScreen>? SplashScreenFactory { get; set; }

    internal TimeSpan MinimumSplashDisplayTime { get; set; } = TimeSpan.FromSeconds(2);

    internal TimeProvider SplashTimeProvider { get; set; } = TimeProvider.System;

    internal Assembly? ApplicationAssembly { get; set; }

    internal IWinFormsNetworkSource? NetworkSource { get; set; }

    internal Action<bool>? NetworkAvailabilityChanged { get; set; }

    internal WinFormsApplicationFeatures Snapshot()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MinimumSplashDisplayTime, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MinimumSplashDisplayTime.TotalMilliseconds, uint.MaxValue - 1);
        ArgumentNullException.ThrowIfNull(SplashTimeProvider);
        string[] arguments = [.. CommandLine ?? Environment.GetCommandLineArgs().Skip(1)];
        foreach (string argument in arguments)
        {
            ArgumentNullException.ThrowIfNull(argument, nameof(CommandLine));
        }

        return new WinFormsApplicationFeatures
        {
            CommandLine = Array.AsReadOnly(arguments),
            Starting = Starting,
            Shutdown = Shutdown,
            ReportFailure = ReportFailure,
            PrepareSettingsSave = PrepareSettingsSave,
            SplashScreenFactory = SplashScreenFactory,
            MinimumSplashDisplayTime = MinimumSplashDisplayTime,
            SplashTimeProvider = SplashTimeProvider,
            ApplicationAssembly = ApplicationAssembly,
            NetworkSource = NetworkSource,
            NetworkAvailabilityChanged = NetworkAvailabilityChanged
        };
    }
}

/// <summary>
///  Provides original argument tokens and the final mutable startup cancellation decision.
/// </summary>
internal sealed class WinFormsStartingEventArgs(ReadOnlyCollection<string> commandLine) : CancelEventArgs
{
    internal ReadOnlyCollection<string> CommandLine { get; } = commandLine;
}

/// <summary>
///  Describes an eligible, noncancellable shutdown.
/// </summary>
internal sealed class WinFormsShutdownEventArgs(WinFormsShutdownReason reason) : EventArgs
{
    internal WinFormsShutdownReason Reason { get; } = reason;
}

/// <summary>
///  Identifies the first committed stop request, or an ordinary message-loop exit.
/// </summary>
internal enum WinFormsShutdownReason
{
    UserExit,
    RequestedStop,
    RunTokenCancellation
}

/// <summary>
///  Identifies terminal owner-thread failures; reporting never suppresses the original failure.
/// </summary>
internal enum WinFormsLifecyclePhase
{
    Startup,
    MessageLoop,
    Shutdown,
    ApplicationService
}
