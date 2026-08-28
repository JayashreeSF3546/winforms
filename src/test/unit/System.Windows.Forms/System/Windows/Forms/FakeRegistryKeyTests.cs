// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Forms.Tests.Mocks;

namespace System.Windows.Forms.Tests;

/// <summary>
///  Verifies that <see cref="FakeRegistryKey"/> can stand in for a real
///  <see cref="Microsoft.Win32.RegistryKey"/>, per https://github.com/dotnet/winforms/issues/3239
///  ("Need a way to mock RegistryKey").
/// </summary>
/// <remarks>
///  <para>
///   <see cref="InputLanguage"/>'s registry reads (<c>LayoutName</c>, <c>LayoutId</c>,
///   <c>LanguageTag</c>) call <see cref="Microsoft.Win32.Registry"/> directly and are not yet
///   injectable, so they are not covered by these tests. These tests instead confirm the fake
///   correctly supports the exact traversal shapes those members rely on&#8212;subkey enumeration
///   and nested value lookup&#8212;so a future change that makes <see cref="InputLanguage"/>'s
///   registry access injectable can reuse this fake instead of depending on the real registry.
///  </para>
/// </remarks>
public class FakeRegistryKeyTests
{
    [Fact]
    public void GetValue_ReturnsConfiguredValue()
    {
        FakeRegistryKey key = new();
        key.SetValue("Layout Text", "English (United States)");

        Assert.Equal("English (United States)", key.GetValue("Layout Text"));
    }

    [Fact]
    public void GetValue_WhenMissing_ReturnsNull()
    {
        FakeRegistryKey key = new();

        Assert.Null(key.GetValue("DoesNotExist"));
    }

    [Fact]
    public void OpenSubKey_WhenMissing_ReturnsNull()
    {
        FakeRegistryKey key = new();

        Assert.Null(key.OpenSubKey("DoesNotExist"));
    }

    [Fact]
    public void FindsMatchingSubKey_ByEnumeratingSubKeyNames()
    {
        // Mirrors InputLanguage.LayoutId, which walks the subkeys of the "Keyboard Layouts" key
        // looking for the one whose "Layout Id" value matches a target id.
        FakeRegistryKey keyboardLayouts = new();
        keyboardLayouts.AddSubKey("00000409").SetValue("Layout Id", "0409");
        keyboardLayouts.AddSubKey("00010409").SetValue("Layout Id", "0001");

        string? match = null;
        foreach (string subKeyName in keyboardLayouts.GetSubKeyNames())
        {
            using IRegistryKey? subKey = keyboardLayouts.OpenSubKey(subKeyName);
            if (subKey?.GetValue("Layout Id") is "0001")
            {
                match = subKeyName;
                break;
            }
        }

        Assert.Equal("00010409", match);
    }

    [Fact]
    public void FindsMatchingLanguage_ByReadingMultiStringValueAndNestedSubKeys()
    {
        // Mirrors InputLanguage.LanguageTag, which reads a "Languages" REG_MULTI_SZ value and then
        // opens a subkey per language to compare its "TransientLangId" value.
        FakeRegistryKey userProfile = new();
        userProfile.SetValue("Languages", new[] { "nqo-GN", "jv-Java" });
        userProfile.AddSubKey("nqo-GN").SetValue("TransientLangId", 0x7C92);
        userProfile.AddSubKey("jv-Java").SetValue("TransientLangId", 0x7C93);

        string? match = null;
        if (userProfile.GetValue("Languages") is string[] languages)
        {
            foreach (string language in languages)
            {
                using IRegistryKey? subKey = userProfile.OpenSubKey(language);
                if (subKey?.GetValue("TransientLangId") is int transientLangId && transientLangId == 0x7C93)
                {
                    match = language;
                    break;
                }
            }
        }

        Assert.Equal("jv-Java", match);
    }
}
