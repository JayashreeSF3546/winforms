// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ApplicationBuilderMigration.CSharp;

/// <summary>
///  Keeps argument processing and work state independent of controls and lifecycle drivers.
/// </summary>
internal sealed class MigrationSession(string[] arguments)
{
    internal string[] Arguments { get; private set; } = [.. arguments];
    internal List<string> Events { get; } = [];
    internal int Count { get; private set; }

    internal bool Start(IEnumerable<string> commandLine)
    {
        Arguments = [.. commandLine];
        Events.Add("startup");
        bool canceled = Arguments.Contains("--cancel-startup", StringComparer.Ordinal);
        Events.Add(canceled ? "canceled" : "accepted");

        return !canceled;
    }

    internal void ProcessItem()
    {
        Count++;
        Events.Add("processed");
    }
}
