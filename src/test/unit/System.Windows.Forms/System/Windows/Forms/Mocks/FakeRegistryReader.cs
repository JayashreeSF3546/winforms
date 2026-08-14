// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Windows.Forms.Tests.Mocks
{
    internal sealed class FakeRegistryReader : IRegistryReader
    {
        private readonly Dictionary<string, object?> _values =
            new Dictionary<string, object?>();

        public void AddValue(
            string subKey,
            string valueName,
            object? value)
        {
            _values[$"{subKey}\\{valueName}"] = value;
        }

        public object? GetValue(
            string subKey,
            string valueName)
        {
            _values.TryGetValue(
                $"{subKey}\\{valueName}",
                out object? value);

            return value;
        }
    }
}
