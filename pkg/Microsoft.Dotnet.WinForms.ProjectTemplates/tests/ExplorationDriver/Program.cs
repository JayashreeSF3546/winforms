// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Automation;

namespace TemplateExplorationDriver;

/// <summary>
///  Inspects only a test-owned application's actual cross-process UI Automation surface.
/// </summary>
internal static partial class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            ArgumentOutOfRangeException.ThrowIfNotEqual(args.Length, 2);
            string executable = Path.GetFullPath(args[0]);
            string scenario = args[1];
            if (scenario is not ("window" or "startup-error"))
            {
                throw new ArgumentException("Expected window or startup-error.", nameof(args));
            }

            Inspect(executable, scenario);

            return 0;
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            Console.Error.WriteLine(exception);

            return 1;
        }
    }

    private static void Inspect(string executable, string scenario)
    {
        string title = $"Template exploration {Guid.NewGuid():N}";
        ProcessStartInfo start = new(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetTempPath(),
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string key in start.Environment.Keys.Where(key =>
            key.StartsWith("Application__", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("Application:", StringComparison.OrdinalIgnoreCase)
            || key.Equals("DOTNET_CONTENTROOT", StringComparison.OrdinalIgnoreCase)
            || key.Equals("DOTNET_ENVIRONMENT", StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            start.Environment.Remove(key);
        }

        start.ArgumentList.Add($"--Application:Title={(scenario == "window" ? title : "")}");
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("The application did not start.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        try
        {
            AutomationElement window = WaitForWindow(process, scenario == "window" ? title : "Application error");
            AutomationElement.AutomationElementInformation information = window.Current;
            string accessibleWindowName = information.Name;
            Require(information.ProcessId == process.Id && information.ControlType == ControlType.Window,
                "The accessible window does not belong to the application.");
            Require(information.IsEnabled && !information.IsOffscreen, "The accessible window is disabled or off-screen.");
            uint dpi = GetDpiForWindow(information.NativeWindowHandle);
            Require(dpi != 0, "The application's actual window DPI was unavailable.");
            string? buttonName = null;
            if (scenario == "startup-error")
            {
                AutomationElementCollection texts = window.FindAll(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
                Require(texts.Cast<AutomationElement>().Any(element =>
                    element.Current.Name.Contains("Application:Title must not be empty.", StringComparison.Ordinal)),
                    "The startup error was not exposed as accessible text.");
                AutomationElementCollection buttons = window.FindAll(TreeScope.Descendants,
                    new AndCondition(
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                        new PropertyCondition(AutomationElement.IsKeyboardFocusableProperty, true)));
                Require(buttons.Count == 1, "The startup error should expose one acknowledgment button. Observed: "
                    + string.Join("; ", buttons.Cast<AutomationElement>().Select(button =>
                        $"{button.Current.Name} id={button.Current.AutomationId} focusable={button.Current.IsKeyboardFocusable}")));
                AutomationElement button = buttons[0];
                buttonName = button.Current.Name;
                Require(!string.IsNullOrWhiteSpace(buttonName) && button.Current.IsEnabled
                    && button.Current.IsKeyboardFocusable && !button.Current.IsOffscreen,
                    "The error acknowledgment button is not named, enabled and keyboard-focusable.");
                var invoke = (InvokePattern)button.GetCurrentPattern(InvokePattern.Pattern);
                invoke.Invoke();
            }
            else
            {
                var pattern = (WindowPattern)window.GetCurrentPattern(WindowPattern.Pattern);
                pattern.Close();
            }

            Require(process.WaitForExit(30000), "The application did not exit after its accessible close command.");
            int expectedExit = scenario == "window" ? 0 : 1;
            Require(process.ExitCode == expectedExit, $"Expected exit {expectedExit}, received {process.ExitCode}.");
            Require(string.IsNullOrWhiteSpace(stderr.GetAwaiter().GetResult()),
                "The application wrote unexpected standard error.");
            _ = stdout.GetAwaiter().GetResult();
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Scenario = scenario,
                WindowName = accessibleWindowName,
                ButtonName = buttonName,
                Dpi = dpi,
                RemoteSession = System.Windows.Forms.SystemInformation.TerminalServerSession,
                HighContrast = System.Windows.Forms.SystemInformation.HighContrast,
                OSArchitecture = RuntimeInformation.OSArchitecture.ToString(),
                ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
                ExitCode = process.ExitCode,
                Outcome = "Passed"
            }));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
        }
    }

    private static AutomationElement WaitForWindow(Process process, string name)
    {
        Stopwatch timeout = Stopwatch.StartNew();
        AndCondition condition = new(
            new PropertyCondition(AutomationElement.ProcessIdProperty, process.Id),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window),
            new PropertyCondition(AutomationElement.NameProperty, name));
        while (timeout.Elapsed < TimeSpan.FromSeconds(30))
        {
            Require(!process.HasExited, $"The application exited before showing '{name}'.");
            AutomationElement? window = AutomationElement.RootElement.FindFirst(TreeScope.Children, condition);
            if (window is not null)
            {
                return window;
            }

            Thread.Sleep(50);
        }

        throw new TimeoutException($"The application did not expose '{name}' through UI Automation.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint window);
}
