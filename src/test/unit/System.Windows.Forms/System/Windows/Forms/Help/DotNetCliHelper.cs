// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Windows.Forms.Tests;

internal static class DotNetCliHelper
{
    public static string CreateTempFolder()
    {
        string path = Path.Combine(Guid.NewGuid().ToString());
        Directory.CreateDirectory(path);
        return path;
    }

    public static int RunCommand(string fileName, string arguments, string workingDirectory)
    {
        Process process = new();

        process.StartInfo.FileName = fileName;
        process.StartInfo.Arguments = arguments;
        process.StartInfo.WorkingDirectory = workingDirectory;
        process.Start();
        process.WaitForExit();
        return process.ExitCode;
    }

    public static void Dispose(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
