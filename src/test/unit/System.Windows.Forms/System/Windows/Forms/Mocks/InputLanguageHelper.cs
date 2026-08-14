// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Windows.Forms.Tests.Mocks
{
    internal class InputLanguageHelper
    {
        private readonly IRegistryReader _registry;

        public InputLanguageHelper(IRegistryReader registry)
        {
            _registry = registry;
        }

        public string? GetLayout()
        {
            return _registry.GetValue(
                @"Keyboard Layout",
                "Layout") as string;
        }
    }
}
