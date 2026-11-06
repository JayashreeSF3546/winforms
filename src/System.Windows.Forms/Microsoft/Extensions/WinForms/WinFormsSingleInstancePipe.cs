// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using Windows.Win32.Security;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.System.Pipes;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Creates an exclusive local pipe with an explicit user ACL and validates the connected process.
/// </summary>
internal static partial class WinFormsSingleInstancePipe
{
    // The checkout's CsWin32 metadata omits SetLastError for CreateNamedPipeW.
    // Election must distinguish namespace access denial from other acquisition failures.
    [LibraryImport("kernel32.dll", EntryPoint = "CreateNamedPipeW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial nint CreateNamedPipe(
        char* name,
        FILE_FLAGS_AND_ATTRIBUTES openMode,
        NAMED_PIPE_MODE pipeMode,
        uint maximumInstances,
        uint outputBufferSize,
        uint inputBufferSize,
        uint defaultTimeout,
        SECURITY_ATTRIBUTES* attributes);

    internal static unsafe NamedPipeServerStream? Create(WinFormsSingleInstanceIdentity identity)
    {
        SecurityIdentifier user = new(identity.User);
        PipeSecurity security = new();
        security.SetOwner(user);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        // Do not grant CreateNewInstance to clients. The server keeps the elected handle until stop.
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize, AccessControlType.Allow));
        byte[] descriptor = security.GetSecurityDescriptorBinaryForm();
        string pipePath = $@"\\.\pipe\{identity.PipeName}";
        fixed (byte* pDescriptor = descriptor)
        fixed (char* pPath = pipePath)
        {
            SECURITY_ATTRIBUTES attributes = new()
            {
                nLength = (uint)sizeof(SECURITY_ATTRIBUTES),
                lpSecurityDescriptor = pDescriptor
            };
            nint handle = CreateNamedPipe(
                pPath,
                FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_OVERLAPPED | FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_FIRST_PIPE_INSTANCE
                    | FILE_FLAGS_AND_ATTRIBUTES.PIPE_ACCESS_DUPLEX,
                NAMED_PIPE_MODE.PIPE_TYPE_BYTE | NAMED_PIPE_MODE.PIPE_READMODE_BYTE | NAMED_PIPE_MODE.PIPE_REJECT_REMOTE_CLIENTS,
                1, WinFormsSingleInstanceProtocol.MaximumPayload, WinFormsSingleInstanceProtocol.MaximumPayload, 0, &attributes);
            int error = Marshal.GetLastPInvokeError();
            SafePipeHandle owned = new(handle, ownsHandle: true);
            if (owned.IsInvalid)
            {
                owned.Dispose();
                if (error is (int)WIN32_ERROR.ERROR_ACCESS_DENIED or (int)WIN32_ERROR.ERROR_PIPE_BUSY)
                {
                    // Both outcomes mean only "not acquired", never successful secondary delivery.
                    return null;
                }

                throw new Win32Exception(error);
            }

            try
            {
                return new NamedPipeServerStream(PipeDirection.InOut, isAsync: true, isConnected: false, owned);
            }
            catch
            {
                owned.Dispose();
                throw;
            }
        }
    }

    internal static unsafe bool VerifyProcess(PipeStream pipe, WinFormsSingleInstanceIdentity identity, bool server)
    {
        uint processId;
        HANDLE handle = (HANDLE)pipe.SafePipeHandle.DangerousGetHandle();
        bool obtained = server
            ? PInvoke.GetNamedPipeClientProcessId(handle, &processId)
            : PInvoke.GetNamedPipeServerProcessId(handle, &processId);

        return obtained && identity.MatchesProcess(processId);
    }

    internal static bool VerifyClient(NamedPipeServerStream pipe, WinFormsSingleInstanceIdentity identity)
    {
        if (!VerifyProcess(pipe, identity, server: true))
        {
            return false;
        }

        bool matches = false;
        // Identification-only token; no user code or I/O runs under impersonation.
        pipe.RunAsClient(() =>
        {
            using WindowsIdentity client = WindowsIdentity.GetCurrent();
            matches = client.User?.Value == identity.User && client.Owner == client.User;
        });

        return matches;
    }
}
