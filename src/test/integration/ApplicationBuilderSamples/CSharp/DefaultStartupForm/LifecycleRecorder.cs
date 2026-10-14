// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;

namespace ApplicationBuilderSamples;

internal sealed class LifecycleRecorder
{
    private readonly Lock _fileLock = new();
    private readonly ConcurrentQueue<string> _entries = new();

    public string LogPath { get; } =
        Path.Combine(AppContext.BaseDirectory, "lifecycle.log");

    public event EventHandler<string>? EntryRecorded;

    public void Record(string eventName)
    {
        string entry = string.Join(
            "|",
            DateTimeOffset.UtcNow.ToString("O"),
            Environment.ProcessId,
            Environment.CurrentManagedThreadId,
            Thread.CurrentThread.GetApartmentState(),
            eventName);

        _entries.Enqueue(entry);

        lock (_fileLock)
        {
            File.AppendAllLines(LogPath, [entry]);
        }

        EntryRecorded?.Invoke(this, entry);
    }

    public string[] GetEntries() => [.. _entries];
}
