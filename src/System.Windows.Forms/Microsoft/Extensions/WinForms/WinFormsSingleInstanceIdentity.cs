// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Creates a per-user and per-session identity for a single-instance application.
/// </summary>
internal static class WinFormsSingleInstanceIdentity
{
    /// <summary>
    ///  Creates the named-pipe name for the current application, user, and session.
    /// </summary>
    internal static string CreatePipeName(string? applicationId)
    {
        Assembly entryAssembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        AssemblyName assemblyName = entryAssembly.GetName();
        string assemblyId = entryAssembly.GetCustomAttribute<GuidAttribute>()?.Value
            ?? assemblyName.Name
            ?? entryAssembly.ManifestModule.ModuleVersionId.ToString();
        string assemblyPath = AppContext.BaseDirectory;
        Version version = assemblyName.Version ?? new(0, 0);

        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        string userSid = identity.User?.Value
            ?? throw new InvalidOperationException("The current Windows user SID is unavailable.");

        using Process process = Process.GetCurrentProcess();

        return CreatePipeName(
            applicationId,
            assemblyId,
            version,
            assemblyPath,
            userSid,
            process.SessionId);
    }

    /// <summary>
    ///  Creates a deterministic pipe name from the application and security scope.
    /// </summary>
    internal static string CreatePipeName(
        string? applicationId,
        string assemblyId,
        Version version,
        string assemblyPath,
        string userSid,
        int sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyId);
        if (applicationId is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);
        }

        ArgumentNullException.ThrowIfNull(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(userSid);
        ArgumentOutOfRangeException.ThrowIfNegative(sessionId);

        string versionScope = $"{version.Major}.{version.Minor}";
        string applicationScope = applicationId is null
            ? $"default\0{assemblyId}\0{versionScope}\0{Path.GetFullPath(assemblyPath).ToUpperInvariant()}"
            : $"explicit\0{applicationId}\0{versionScope}";
        string scope = $"{applicationScope}\0{userSid}\0{sessionId}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(scope));

        return $"WinFormsApplication-{Convert.ToHexString(hash)}";
    }
}
