// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WinFormsLifetimePerformance;

/// <summary>
///  Reads GUI resource counts and verifies native window destruction.
/// </summary>
internal static partial class NativeMethods
{
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint GetGuiResources(SafeProcessHandle process, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindow(nint window);

    internal static uint ReadGuiResources(Process process, bool userObjects)
    {
        uint count = GetGuiResources(process.SafeHandle, userObjects ? 1u : 0u);
        int error = Marshal.GetLastPInvokeError();
        if (count == 0 && error != 0)
        {
            throw new Win32Exception(error);
        }

        return count;
    }
}
