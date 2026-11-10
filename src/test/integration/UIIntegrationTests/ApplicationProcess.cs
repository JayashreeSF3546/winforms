// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Windows.Forms.UITests;

/// <summary>
///  Starts and drains only test-owned applications, with a bounded exit wait and PID-scoped cleanup.
/// </summary>
internal static class ApplicationProcess
{
    internal static Process Start(string executable, IEnumerable<string> arguments)
    {
        Assert.True(File.Exists(executable), $"Missing built application: {executable}");
        ProcessStartInfo info = new(executable)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!
        };
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return Process.Start(info)!;
    }

    internal static async Task<(string Output, string Errors)> CompleteAsync(
        Process process, Task<string>? errors = null)
    {
        Task<string> transcript = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        errors ??= process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        try
        {
            using CancellationTokenSource watchdog =
                CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            watchdog.CancelAfter(TimeSpan.FromSeconds(45));
            await process.WaitForExitAsync(watchdog.Token);
        }
        finally
        {
            await KillOwnedAsync(process);
        }

        return (await transcript, await errors);
    }

    internal static async Task KillOwnedAsync(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
        }
    }
}
