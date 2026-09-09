// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Blocks every activation entry point while the component model reports design-time usage.
/// </summary>
/// <remarks>
///  <para>
///   The Windows Forms Designer sets <see cref="LicenseManager.UsageMode"/> to
///   <see cref="LicenseUsageMode.Designtime"/> while it creates components. Refusing activation in
///   that state guarantees that a design surface can never start application services, read user
///   data, or capture a runtime service provider, even if a control mistakenly holds an activator.
///  </para>
/// </remarks>
internal static class DesignTimeGuard
{
    internal static bool IsDesignTime => LicenseManager.UsageMode == LicenseUsageMode.Designtime;

    internal static void ThrowIfDesignTime()
    {
        if (IsDesignTime)
        {
            throw new InvalidOperationException(
                "Windows Forms service activation is not available at design time. Types created by "
                + "the Designer must have a public parameterless constructor and must work without "
                + "application services.");
        }
    }
}
