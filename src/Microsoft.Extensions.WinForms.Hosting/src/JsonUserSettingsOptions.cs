// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Configures opt-in storage, validation, and consecutive JSON schema migrations.
/// </summary>
/// <typeparam name="T">The settings object model.</typeparam>
public sealed class JsonUserSettingsOptions<T> where T : class
{
    /// <summary>
    ///  Gets or sets the absolute application-specific storage directory.
    ///  Null selects Local Application Data, WinForms, and the registered stable application ID.
    /// </summary>
    public string? StorageDirectory { get; set; }

    /// <summary>
    ///  Gets or sets the maximum combined in-instance and cross-process contention wait.
    /// </summary>
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    ///  Gets or sets validation which throws for an unacceptable deserialized model.
    /// </summary>
    public Action<T>? Validate { get; set; }

    /// <summary>
    ///  Gets migrations keyed by source schema version. Each migration advances exactly one version.
    /// </summary>
    public IDictionary<int, Func<JsonElement, JsonElement>> Migrations { get; } = new Dictionary<int, Func<JsonElement, JsonElement>>();
}
