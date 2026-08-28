// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Windows.Forms.Tests.Mocks;

/// <summary>
///  In-memory fake for <see cref="IRegistryKey"/>. Lets tests build a small tree of subkeys and values
///  so registry-dependent code can be exercised without touching the real Windows registry.
/// </summary>
internal sealed class FakeRegistryKey : IRegistryKey
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FakeRegistryKey> _subKeys = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///  Configures the value that <see cref="GetValue(string?)"/> will return for the given name.
    /// </summary>
    public FakeRegistryKey SetValue(string name, object? value)
    {
        _values[name] = value;
        return this;
    }

    /// <summary>
    ///  Adds (or returns the existing) fake subkey with the given name, so further values or subkeys
    ///  can be configured on it.
    /// </summary>
    public FakeRegistryKey AddSubKey(string name)
    {
        if (!_subKeys.TryGetValue(name, out FakeRegistryKey? subKey))
        {
            subKey = new FakeRegistryKey();
            _subKeys[name] = subKey;
        }

        return subKey;
    }

    public object? GetValue(string? name) =>
        name is not null && _values.TryGetValue(name, out object? value) ? value : null;

    public IRegistryKey? OpenSubKey(string name) =>
        _subKeys.TryGetValue(name, out FakeRegistryKey? subKey) ? subKey : null;

    public string[] GetSubKeyNames() => [.. _subKeys.Keys];

    public void Dispose()
    {
        // The fake owns no unmanaged resources; nothing to release.
    }
}
