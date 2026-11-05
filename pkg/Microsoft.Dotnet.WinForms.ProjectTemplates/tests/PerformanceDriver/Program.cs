// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace TemplatePerformanceDriver;

/// <summary>
///  Observes unmodified published entry points without loading product assemblies into the driver.
/// </summary>
internal static partial class Program
{
    private const int StartupTimeoutMilliseconds = 30000;
    private const int ExitTimeoutMilliseconds = 30000;
    private const uint CloseMessage = 0x0010;

    private static int Main(string[] args)
    {
        try
        {
            if (args is ["inventory", string directory])
            {
                Inventory(Path.GetFullPath(directory));
            }
            else if (args is ["measure", string executable, string title, string idle, string profile])
            {
                int idleMilliseconds = int.Parse(idle, System.Globalization.CultureInfo.InvariantCulture);
                ArgumentOutOfRangeException.ThrowIfNegative(idleMilliseconds);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(idleMilliseconds, 30000);
                if (profile is not ("default" or "watchers"))
                {
                    throw new ArgumentException("Expected default or watchers profile.", nameof(args));
                }

                Measure(Path.GetFullPath(executable), title, idleMilliseconds, profile == "watchers");
            }
            else
            {
                throw new ArgumentException("Expected inventory <directory> or measure <exe> <title> <idle-ms> <default|watchers>.", nameof(args));
            }

            return 0;
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            Console.Error.WriteLine(exception);

            return 1;
        }
    }

    private static void Measure(string executable, string title, int idleMilliseconds, bool watchers)
    {
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
            || key.StartsWith("Logging__", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("Logging:", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("hostBuilder__", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("hostBuilder:", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("DOTNET_HOSTBUILDER", StringComparison.OrdinalIgnoreCase)
            || key.Equals("DOTNET_CONTENTROOT", StringComparison.OrdinalIgnoreCase)
            || key.Equals("DOTNET_APPLICATIONNAME", StringComparison.OrdinalIgnoreCase)
            || key.Equals("DOTNET_ENVIRONMENT", StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            start.Environment.Remove(key);
        }

        start.ArgumentList.Add($"--Application:Title={title}");
        if (watchers)
        {
            start.ArgumentList.Add("--hostBuilder:reloadConfigOnChange=true");
        }

        long launched = Stopwatch.GetTimestamp();
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("The application did not start.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        try
        {
            nint window;
            while (true)
            {
                Require(!process.HasExited, "The application exited before its responsive main window appeared.");
                process.Refresh();
                window = process.MainWindowHandle;
                if (window != 0 && process.MainWindowTitle == title && IsWindowVisible(window)
                    && SendMessageTimeoutW(window, 0, 0, 0, 3, 1000, out _) != 0)
                {
                    break;
                }

                Require(Stopwatch.GetElapsedTime(launched).TotalMilliseconds < StartupTimeoutMilliseconds,
                    $"The expected responsive main window '{title}' did not appear.");
                Thread.Sleep(10);
            }

            double startupMilliseconds = Stopwatch.GetElapsedTime(launched).TotalMilliseconds;
            uint dpi = GetDpiForWindow(window);
            Require(dpi > 0, "The visible window DPI was unavailable.");
            ResourceSnapshot ready = Snapshot(process);
            Thread.Sleep(idleMilliseconds);
            Require(!process.HasExited && IsWindowVisible(window), "The application exited or hid its window during observation.");
            ResourceSnapshot settled = Snapshot(process);
            long closing = Stopwatch.GetTimestamp();
            Require(PostMessageW(window, CloseMessage, 0, 0), "The application's normal close request failed.");
            Require(process.WaitForExit(ExitTimeoutMilliseconds), "The application did not exit after normal window close.");
            double shutdownMilliseconds = Stopwatch.GetElapsedTime(closing).TotalMilliseconds;
            string output = stdout.GetAwaiter().GetResult();
            string errors = stderr.GetAwaiter().GetResult();
            Require(process.ExitCode == 0, $"The application exited with {process.ExitCode}.\n{output}\n{errors}");
            Require(string.IsNullOrWhiteSpace(errors), $"Unexpected application standard error:\n{errors}");
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Outcome = "Passed",
                Executable = executable,
                WindowTitle = title,
                Dpi = dpi,
                StartupMilliseconds = startupMilliseconds,
                ShutdownMilliseconds = shutdownMilliseconds,
                IdleMilliseconds = idleMilliseconds,
                WatchersOverride = watchers,
                Ready = ready,
                Settled = settled,
                ExitCode = process.ExitCode,
                ApplicationOutput = output,
                RemoteSession = GetSystemMetrics(0x1000) != 0,
                OSArchitecture = RuntimeInformation.OSArchitecture.ToString(),
                ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
                ObserverRuntime = RuntimeInformation.FrameworkDescription
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

    private static ResourceSnapshot Snapshot(Process process)
    {
        process.Refresh();
        uint gdi = GetGuiResources(process.Handle, 0);
        uint user = GetGuiResources(process.Handle, 1);
        Require(user > 0, "No USER objects were observed for the visible application.");

        return new(process.PrivateMemorySize64, process.WorkingSet64, process.HandleCount,
            process.Threads.Count, process.Modules.Count, gdi, user);
    }

    private static void Inventory(string directory)
    {
        List<object> files = [];
        long totalBytes = 0;
        int assemblies = 0;
        foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            FileInfo information = new(file);
            bool managedAssembly = false;
            string? assemblyName = null;
            string? assemblyVersion = null;
            string? machine = null;
            if (information.Extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
                || information.Extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
            {
                using FileStream stream = File.OpenRead(file);
                using PEReader reader = new(stream);
                machine = reader.PEHeaders.CoffHeader.Machine.ToString();
                if (reader.HasMetadata)
                {
                    MetadataReader metadata = reader.GetMetadataReader();
                    if (metadata.IsAssembly)
                    {
                        AssemblyDefinition definition = metadata.GetAssemblyDefinition();
                        managedAssembly = true;
                        assemblyName = metadata.GetString(definition.Name);
                        assemblyVersion = definition.Version.ToString();
                        assemblies++;
                    }
                }
            }

            using FileStream hashStream = File.OpenRead(file);
            files.Add(new
            {
                Path = Path.GetRelativePath(directory, file),
                Bytes = information.Length,
                SHA256 = Convert.ToHexString(SHA256.HashData(hashStream)),
                ManagedAssembly = managedAssembly,
                AssemblyName = assemblyName,
                AssemblyVersion = assemblyVersion,
                Machine = machine
            });
            totalBytes += information.Length;
        }

        Require(files.Count > 0 && assemblies > 0, "The publish inventory was empty or contained no managed assemblies.");
        Console.WriteLine(JsonSerializer.Serialize(new { FileCount = files.Count, AssemblyCount = assemblies, TotalBytes = totalBytes, Files = files }));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    /// <summary>
    ///  Captures process-wide resources without forcing collections or changing the measured application.
    /// </summary>
    private sealed record ResourceSnapshot(long PrivateBytes, long WorkingSetBytes, int KernelHandles,
        int Threads, int LoadedModules, uint GdiObjects, uint UserObjects);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint window);

    [LibraryImport("user32.dll")]
    private static partial nint SendMessageTimeoutW(nint window, uint message, nuint wParam,
        nint lParam, uint flags, uint timeout, out nuint result);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(nint window, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial uint GetGuiResources(nint process, uint flags);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint window);
}
