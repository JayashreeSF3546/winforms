// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Serialization;

namespace ApplicationBuilderStartupConfiguration.CSharp;

/// <summary>
///  Stores a simple user preference for the WinForms settings service example.
/// </summary>
internal sealed class StartupPreferences
{
    public bool UseCompactLayout { get; set; } = true;
}

/// <summary>
///  Supplies source-generated JSON metadata for the user settings service.
/// </summary>
[JsonSerializable(typeof(StartupPreferences))]
internal sealed partial class StartupPreferencesJsonContext : JsonSerializerContext
{
}
