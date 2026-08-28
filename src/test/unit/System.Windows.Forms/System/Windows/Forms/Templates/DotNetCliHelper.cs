// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;

namespace System.Windows.Forms.Tests;

internal static class DotNetCliHelper
{
    private static readonly TimeSpan s_defaultTimeout = TimeSpan.FromMinutes(5);

    // Roots the folder under the OS temp directory so generated projects are fully isolated from this
    // repository's own .editorconfig/Directory.Build.props (which would otherwise leak into, and fail,
    // the generated project's build).
    public static string CreateTempFolder()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static void Dispose(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    // Runs a CLI command with a timeout and captured output so CI failures are diagnosable and a hung
    // child process can never hang the test run indefinitely.
    public static CommandResult RunCommand(string fileName, string arguments, string workingDirectory, TimeSpan? timeout = null)
    {
        using Process process = new()
        {
            StartInfo = new()
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        StringBuilder standardOutput = new();
        StringBuilder standardError = new();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                standardOutput.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                standardError.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        TimeSpan effectiveTimeout = timeout ?? s_defaultTimeout;
        if (!process.WaitForExit((int)effectiveTimeout.TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"'{fileName} {arguments}' did not complete within {effectiveTimeout}. " +
                $"Output so far:{Environment.NewLine}{standardOutput}{Environment.NewLine}Error:{Environment.NewLine}{standardError}");
        }

        // Ensures the asynchronous output/error stream handlers have finished flushing before we read the buffers.
        process.WaitForExit();

        return new CommandResult(process.ExitCode, standardOutput.ToString(), standardError.ToString());
    }

    internal readonly record struct CommandResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public override string ToString() =>
            $"ExitCode: {ExitCode}{Environment.NewLine}StandardOutput:{Environment.NewLine}{StandardOutput}{Environment.NewLine}StandardError:{Environment.NewLine}{StandardError}";
    }
}
