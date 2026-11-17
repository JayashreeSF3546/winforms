// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Captures the narrow prototype boundary: a normal desktop token, session and executable path.
/// </summary>
/// <remarks>
///  <para>
///   Elevated/custom-owner tokens and cross-path sharing require review and are not supported.
///   The caller chooses the logical ID and version cohort explicitly; no MVID default is inferred.
///  </para>
/// </remarks>
internal sealed class WinFormsSingleInstanceIdentity
{
    private WinFormsSingleInstanceIdentity(string key, string path, string user, int session)
    {
        PipeName = $"WinForms.Builder.SingleInstance.{key}";
        ExecutablePath = path;
        User = user;
        Session = session;
    }

    internal string PipeName { get; }
    internal string ExecutablePath { get; }
    internal string User { get; }
    internal int Session { get; }

    internal static WinFormsSingleInstanceIdentity Create(string logicalId, string versionCohort)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(versionCohort);
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        if (identity.User is null || identity.Owner != identity.User || identity.ImpersonationLevel != TokenImpersonationLevel.None)
        {
            throw new UnauthorizedAccessException();
        }

        string path = Path.GetFullPath(Environment.ProcessPath!);
        using Process process = Process.GetCurrentProcess();
        string user = identity.User.Value;
        int session = process.SessionId;

        return new(GetKey(logicalId, versionCohort, path, user, session), path, user, session);
    }

    // Pure key calculation is exposed to tests, not an injectable security identity.
    internal static string GetKey(string logicalId, string versionCohort, string path, string user, int session)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream, Encoding.UTF8);
        writer.Write(logicalId);
        writer.Write(versionCohort);
        writer.Write(Path.GetFullPath(path).ToUpperInvariant());
        writer.Write(user);
        writer.Write(session);
        writer.Flush();

        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    internal bool MatchesProcess(uint processId)
    {
        try
        {
            using Process process = Process.GetProcessById(checked((int)processId));
            return process.SessionId == Session
                && string.Equals(process.MainModule?.FileName, ExecutablePath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
