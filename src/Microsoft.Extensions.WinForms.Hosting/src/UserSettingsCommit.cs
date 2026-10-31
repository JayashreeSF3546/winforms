// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Describes an immutable committed snapshot separately from delivery of its change notifications.
/// </summary>
public sealed class UserSettingsCommit
{
    internal UserSettingsCommit(int schemaVersion, long revision, JsonElement settings, Task notificationCompletion)
    {
        SchemaVersion = schemaVersion;
        Revision = revision;
        Settings = settings.Clone();
        NotificationCompletion = notificationCompletion;
    }

    /// <summary>
    ///  Gets the document's schema version.
    /// </summary>
    public int SchemaVersion { get; }

    /// <summary>
    ///  Gets the committed revision, increasing for writes observed by the same service instance.
    /// </summary>
    public long Revision { get; }

    /// <summary>
    ///  Gets the immutable JSON model, without a dependency on a caller-owned JsonDocument.
    /// </summary>
    public JsonElement Settings { get; }

    /// <summary>
    ///  Gets notification completion; subscriber failures fault this task after persistence succeeds.
    /// </summary>
    public Task NotificationCompletion { get; }
}

/// <summary>
///  Supplies an immutable committed user-settings snapshot to a change subscriber.
/// </summary>
public sealed class UserSettingsChangedEventArgs : EventArgs
{
    internal UserSettingsChangedEventArgs(UserSettingsCommit commit)
    {
        Commit = commit;
    }

    /// <summary>
    ///  Gets the committed snapshot.
    /// </summary>
    public UserSettingsCommit Commit { get; }
}
