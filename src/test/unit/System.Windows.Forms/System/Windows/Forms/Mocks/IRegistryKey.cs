// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Windows.Forms.Tests.Mocks;

/// <summary>
///  Minimal abstraction over <see cref="Microsoft.Win32.RegistryKey"/> covering the subset of members
///  production code typically needs (such as the registry reads performed by
///  <see cref="InputLanguage"/>). Lets registry-dependent code be exercised against an in-memory fake
///  (<see cref="FakeRegistryKey"/>) instead of the real Windows registry.
/// </summary>
/// <remarks>
///  <para>
///   Tracks https://github.com/dotnet/winforms/issues/3239.
///  </para>
/// </remarks>
internal interface IRegistryKey : IDisposable
{
    /// <summary>
    ///  Gets the value associated with <paramref name="name"/>, or <see langword="null"/> if it does
    ///  not exist.
    /// </summary>
    object? GetValue(string? name);

    /// <summary>
    ///  Opens the subkey with the specified name, or returns <see langword="null"/> if it does not exist.
    /// </summary>
    IRegistryKey? OpenSubKey(string name);

    /// <summary>
    ///  Returns the names of the direct subkeys of this key.
    /// </summary>
    string[] GetSubKeyNames();
}
