// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Win32;

namespace System.Windows.Forms.Tests.Mocks
{
    internal sealed class RegistryReader : IRegistryReader
    {
        public object? GetValue(string subKey, string valueName)
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(subKey);

            return key?.GetValue(valueName);
        }
    }
}
