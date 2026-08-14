// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Forms.Tests.Mocks;

namespace System.Windows.Forms.Tests
{
    public class InputLanguageHelperTests
    {
        [WinFormsFact]
        public void GetLayout_ReturnsRegistryValue()
        {
            FakeRegistryReader fakeRegistry =
                new FakeRegistryReader();

            fakeRegistry.AddValue(
                @"Keyboard Layout",
                "Layout",
                "English");

            InputLanguageHelper helper =
                new InputLanguageHelper(fakeRegistry);

            string? result = helper.GetLayout();

            Assert.Equal("English", result);
        }

        [WinFormsFact]
        public void GetLayout_WhenMissing_ReturnsNull()
        {
            FakeRegistryReader fakeRegistry =
                new FakeRegistryReader();

            InputLanguageHelper helper =
                new InputLanguageHelper(fakeRegistry);

            Assert.Null(helper.GetLayout());
        }
    }
}
