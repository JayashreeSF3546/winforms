// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Windows.Forms;
using Microsoft.VisualBasic.ApplicationServices;
using Microsoft.Extensions.WinForms;

namespace WinFormsApplicationBuilder.Benchmarks;

internal static partial class Program
{
    private const int ActivationProcessTimeoutMilliseconds = 30_000;
    private const string ActivationReadyMarker = "READY";
    private const string ActivationReceivedMarker = "ACTIVATED";
    private const string ActivationArgumentMismatchMarker = "ACTIVATED_WITH_INVALID_ARGUMENTS";

    private static void RunActivationBenchmarks(int iterationCount)
    {
        Console.WriteLine("Single-instance activation and IPC proxy measurements:");
        Console.WriteLine(
            "  Timings include secondary process startup; they are not isolated pipe-protocol costs.");

        foreach (ActivationModel model in Enum.GetValues<ActivationModel>())
        {
            List<double> activationDispatchMilliseconds = [];
            List<double> secondaryLaunchToExitMilliseconds = [];

            for (int iteration = 0; iteration < iterationCount; iteration++)
            {
                string applicationId = $"WinFormsApplicationBuilder.Benchmarks.{Guid.NewGuid():N}";
                using Process primary = StartActivationProcess(
                    primary: true,
                    model,
                    applicationId,
                    commandLineArgument: $"benchmark-argument-{iteration}");
                Task<string> primaryErrorTask = primary.StandardError.ReadToEndAsync();
                Process? secondary = null;

                try
                {
                    string? readyMarker = ReadActivationOutput(primary, model, "primary readiness");
                    if (readyMarker != ActivationReadyMarker)
                    {
                        throw new InvalidOperationException(
                            $"{model} primary did not signal readiness. Output: {readyMarker ?? "<closed>"}");
                    }

                    Stopwatch activationTimer = Stopwatch.StartNew();
                    Stopwatch secondaryTimer = Stopwatch.StartNew();
                    secondary = StartActivationProcess(
                        primary: false,
                        model,
                        applicationId,
                        commandLineArgument: $"benchmark-argument-{iteration}");
                    Task<string> secondaryErrorTask = secondary.StandardError.ReadToEndAsync();
                    string? activatedMarker = ReadActivationOutput(primary, model, "activation callback");
                    activationTimer.Stop();

                    if (activatedMarker != ActivationReceivedMarker)
                    {
                        throw new InvalidOperationException(
                            $"{model} primary did not report activation. Output: {activatedMarker ?? "<closed>"}");
                    }

                    WaitForActivationProcess(secondary, model, "secondary");
                    secondaryTimer.Stop();
                    WaitForActivationProcess(primary, model, "primary");

                    string secondaryError = secondaryErrorTask.GetAwaiter().GetResult();
                    string primaryError = primaryErrorTask.GetAwaiter().GetResult();
                    if (secondary.ExitCode != 0 || primary.ExitCode != 0)
                    {
                        throw new InvalidOperationException(
                            $"{model} activation worker failed. "
                                + $"Primary exit code: {primary.ExitCode}; secondary exit code: {secondary.ExitCode}."
                                + Environment.NewLine
                                + primaryError
                                + secondaryError);
                    }

                    activationDispatchMilliseconds.Add(activationTimer.Elapsed.TotalMilliseconds);
                    secondaryLaunchToExitMilliseconds.Add(secondaryTimer.Elapsed.TotalMilliseconds);
                }
                finally
                {
                    if (secondary is not null)
                    {
                        StopActivationProcess(secondary);
                        secondary.Dispose();
                    }

                    StopActivationProcess(primary);
                }
            }

            Console.WriteLine($"  {model}:");
            Console.WriteLine(
                $"    Secondary launch-to-primary callback ms: "
                    + $"median {Percentile(activationDispatchMilliseconds, 0.50):F2}, "
                    + $"p95 {Percentile(activationDispatchMilliseconds, 0.95):F2}");
            Console.WriteLine(
                $"    Secondary launch-to-exit ms: "
                    + $"median {Percentile(secondaryLaunchToExitMilliseconds, 0.50):F2}, "
                    + $"p95 {Percentile(secondaryLaunchToExitMilliseconds, 0.95):F2}");
        }

        Console.WriteLine();
    }

    private static void RunActivationWorker(string[] args)
    {
        if (args.Length < 3
            || !Enum.TryParse(args[1], ignoreCase: true, out ActivationModel model))
        {
            throw new ArgumentException(
                "Activation worker usage: --activation-primary|--activation-secondary "
                    + "<builder|visualbasicframework> <application-id> [command-line-argument]");
        }

        bool isPrimary = args[0] == "--activation-primary";
        if (args[0] is not ("--activation-primary" or "--activation-secondary"))
        {
            throw new ArgumentException($"Unsupported activation worker mode: {args[0]}.");
        }

        if (isPrimary)
        {
            if (args.Length != 4)
            {
                throw new ArgumentException(
                    "Activation primary usage requires the expected command-line argument.");
            }

            RunActivationPrimary(model, args[2], args[3]);
        }
        else
        {
            if (args.Length != 4)
            {
                throw new ArgumentException(
                    "Activation secondary usage requires a command-line argument.");
            }

            RunActivationSecondary(model, args[2], args[3]);
        }
    }

    private static void RunActivationPrimary(
        ActivationModel model,
        string applicationId,
        string expectedCommandLineArgument)
    {
        switch (model)
        {
            case ActivationModel.Builder:
                ConfigureWinForms();
                Form builderForm = CreateBenchmarkForm();
                builderForm.Shown += static (_, _) => Console.WriteLine(ActivationReadyMarker);

                using (WinFormsApplication application = WinFormsApplication.CreateBuilder()
                    .UseStartupForm(builderForm)
                    .UseSingleInstance(applicationId)
                    .Build())
                {
                    application.ActivationRequested += (_, eventArgs) =>
                    {
                        bool argumentForwarded = eventArgs.Arguments.Count == 1
                            && eventArgs.Arguments[0] == expectedCommandLineArgument;
                        eventArgs.BringToForeground = false;
                        Console.WriteLine(
                            argumentForwarded
                                ? ActivationReceivedMarker
                                : ActivationArgumentMismatchMarker);
                        builderForm.Close();
                    };

                    application.Run();
                }

                break;
            case ActivationModel.VisualBasicFramework:
                BenchmarkVisualBasicApplication visualBasicApplication = new(
                    () =>
                    {
                        Form visualBasicForm = CreateBenchmarkForm();
                        visualBasicForm.Shown += static (_, _) => Console.WriteLine(ActivationReadyMarker);
                        return visualBasicForm;
                    },
                    splashScreenFactory: null,
                    isSingleInstance: true);
                visualBasicApplication.StartupNextInstance += (_, eventArgs) =>
                {
                    bool argumentForwarded = eventArgs.CommandLine.Count == 1
                        && eventArgs.CommandLine[0] == expectedCommandLineArgument;
                    eventArgs.BringToForeground = false;
                    Console.WriteLine(
                        argumentForwarded
                            ? ActivationReceivedMarker
                            : ActivationArgumentMismatchMarker);
                    visualBasicApplication.CloseStartupForm();
                };
                visualBasicApplication.Run([]);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(model));
        }
    }

    private static void RunActivationSecondary(
        ActivationModel model,
        string applicationId,
        string commandLineArgument)
    {
        switch (model)
        {
            case ActivationModel.Builder:
                ConfigureWinForms();
                using (WinFormsApplication application = WinFormsApplication.CreateBuilder([commandLineArgument])
                    .UseStartupForm(CreateBenchmarkForm())
                    .UseSingleInstance(applicationId)
                    .Build())
                {
                    application.Run();
                }

                break;
            case ActivationModel.VisualBasicFramework:
                BenchmarkVisualBasicApplication visualBasicApplication = new(
                    CreateBenchmarkForm,
                    splashScreenFactory: null,
                    isSingleInstance: true);
                visualBasicApplication.Run([commandLineArgument]);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(model));
        }
    }

    private static Process StartActivationProcess(
        bool primary,
        ActivationModel model,
        string applicationId,
        string? commandLineArgument)
    {
        string executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("The benchmark executable path is unavailable.");
        ProcessStartInfo startInfo = new(executablePath)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        startInfo.ArgumentList.Add(primary ? "--activation-primary" : "--activation-secondary");
        startInfo.ArgumentList.Add(model.ToString());
        startInfo.ArgumentList.Add(applicationId);

        if (commandLineArgument is not null)
        {
            startInfo.ArgumentList.Add(commandLineArgument);
        }

        Process process = new() { StartInfo = startInfo };
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException($"Could not start the {model} activation worker.");
        }

        return process;
    }

    private static string? ReadActivationOutput(Process process, ActivationModel model, string action)
    {
        try
        {
            return process.StandardOutput
                .ReadLineAsync()
                .WaitAsync(TimeSpan.FromMilliseconds(ActivationProcessTimeoutMilliseconds))
                .GetAwaiter()
                .GetResult();
        }
        catch (TimeoutException exception)
        {
            throw new TimeoutException($"{model} activation worker timed out during {action}.", exception);
        }
    }

    private static void WaitForActivationProcess(
        Process process,
        ActivationModel model,
        string role)
    {
        if (!process.WaitForExit(ActivationProcessTimeoutMilliseconds))
        {
            StopActivationProcess(process);
            throw new TimeoutException($"{model} {role} activation worker did not exit.");
        }
    }

    private static void StopActivationProcess(Process process)
    {
        if (process.HasExited)
        {
            return;
        }

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
        }

        if (!process.HasExited)
        {
            process.WaitForExit();
        }
    }

    /// <summary>
    ///  Identifies the application model used for single-instance activation measurements.
    /// </summary>
    private enum ActivationModel
    {
        Builder,
        VisualBasicFramework
    }

    /// <summary>
    ///  Adapts the legacy Visual Basic Application Framework for benchmark scenarios.
    /// </summary>
    private sealed class BenchmarkVisualBasicApplication : WindowsFormsApplicationBase
    {
        private readonly Func<Form> _startupFormFactory;
        private readonly Func<Form>? _splashScreenFactory;

        public BenchmarkVisualBasicApplication(
            Func<Form> startupFormFactory,
            Func<Form>? splashScreenFactory,
            bool isSingleInstance = false)
        {
            ArgumentNullException.ThrowIfNull(startupFormFactory);

            _startupFormFactory = startupFormFactory;
            _splashScreenFactory = splashScreenFactory;
            EnableVisualStyles = true;
            IsSingleInstance = isSingleInstance;

            if (splashScreenFactory is not null)
            {
                MinimumSplashScreenDisplayTime = MinimumSplashDisplayMilliseconds;
            }
        }

        public void ListenForNetworkAvailability()
            => NetworkAvailabilityChanged += static (_, _) => { };

        public void CloseStartupForm()
            => MainForm?.Close();

        /// <inheritdoc/>
        protected override void OnCreateMainForm()
            => MainForm = _startupFormFactory();

        /// <inheritdoc/>
        protected override void OnCreateSplashScreen()
        {
            if (_splashScreenFactory is not null)
            {
                SplashScreen = _splashScreenFactory();
            }
        }
    }
}
