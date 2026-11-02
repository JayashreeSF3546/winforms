// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Serializes typed settings operations with a retained sharing-mode lock file and atomic replacement.
/// </summary>
/// <remarks>
///  <para>
///   File handles are operation-owned. There is no permanent watcher, process-global provider, or notification
///   thread. Callers must join pending writes and notification tasks before releasing application lifetime.
///  </para>
/// </remarks>
internal sealed class JsonUserSettingsService<T>(JsonUserSettingsService<T>.Registration registration) : IUserSettingsService<T>
    where T : class
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _notificationGate = new();
    private readonly Queue<Notification> _notifications = new();
    private bool _dispatching;
    private long _revision;
    private Notification? _pendingNotification;

    public event EventHandler<UserSettingsChangedEventArgs>? Changed;

    public Task<T> LoadAsync(CancellationToken cancellationToken = default)
        => ExecuteAsync(() =>
        {
            Document? document = ReadDocument(registration.PrimaryPath);
            if (document is not null)
            {
                return ReadCompatible(document);
            }

            if (ReadDocument(registration.BackupPath) is not null)
            {
                throw new InvalidDataException(SettingsSR.RecoveryRequired);
            }

            return ValidateModel(Serialize(registration.CreateDefaults()));
        }, cancellationToken);

    public Task<int?> GetSchemaVersionAsync(CancellationToken cancellationToken = default)
        => ExecuteAsync(() => (int?)ReadDocument(registration.PrimaryPath)?.SchemaVersion, cancellationToken);

    public Task<UserSettingsCommit> SaveAsync(T settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return ExecuteAsync(() => Save(Serialize(settings), cancellationToken), cancellationToken);
    }

    public Task<UserSettingsCommit> ResetAsync(CancellationToken cancellationToken = default)
        => ExecuteAsync(() => Save(Serialize(registration.CreateDefaults()), cancellationToken), cancellationToken);

    public Task<UserSettingsCommit> UpgradeAsync(CancellationToken cancellationToken = default)
        => ExecuteAsync(() =>
        {
            Document document = ReadDocument(registration.PrimaryPath)
                ?? throw new FileNotFoundException(SettingsSR.MissingPrimary, registration.PrimaryPath);
            if (document.SchemaVersion == registration.SchemaVersion)
            {
                ReadCompatible(document);
                return new UserSettingsCommit(document.SchemaVersion, document.Revision, document.Settings, Task.CompletedTask);
            }

            if (document.SchemaVersion > registration.SchemaVersion)
            {
                throw new InvalidDataException(SettingsSR.NewerSchema);
            }

            JsonElement settings = document.Settings;
            for (int version = document.SchemaVersion; version < registration.SchemaVersion; version++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!registration.Migrations.TryGetValue(version, out Func<JsonElement, JsonElement>? migrate))
                {
                    throw new InvalidDataException(string.Format(SettingsSR.MissingMigration, version));
                }

                settings = migrate(settings.Clone()).Clone();
                RequireObject(settings);
            }

            ValidateModel(settings);
            return Commit(settings, document, preserveBackup: true, cancellationToken);
        }, cancellationToken);

    public Task<UserSettingsCommit> RecoverAsync(CancellationToken cancellationToken = default)
        => ExecuteAsync(() =>
        {
            Document backup = ReadDocument(registration.BackupPath)
                ?? throw new FileNotFoundException(SettingsSR.MissingBackup, registration.BackupPath);
            ReadCompatible(backup);
            // Never replace a compatible or newer primary through recovery.
            try
            {
                Document? primary = ReadDocument(registration.PrimaryPath);
                if (primary is not null)
                {
                    if (primary.SchemaVersion > registration.SchemaVersion)
                    {
                        throw new InvalidOperationException(SettingsSR.NewerSchema);
                    }

                    ReadCompatible(primary);
                    throw new InvalidOperationException(SettingsSR.ValidPrimary);
                }
            }
            catch (InvalidDataException)
            {
                // Explicit recovery alone may replace a damaged/incompatible primary with a validated backup.
            }
            catch (JsonException)
            {
                // Typed deserialization may report malformed model data after the envelope has been parsed.
            }

            cancellationToken.ThrowIfCancellationRequested();
            bool primaryPresent;
            try
            {
                File.Copy(registration.PrimaryPath, registration.PrimaryPath + ".corrupt." + Guid.NewGuid().ToString("N"));
                primaryPresent = true;
            }
            catch (FileNotFoundException)
            {
                primaryPresent = false;
            }

            return Commit(backup.Settings, backup, preserveBackup: false, cancellationToken, primaryPresent);
        }, cancellationToken);

    public Task<UserSettingsCommit?> ImportAsync(Func<T> readLegacySettings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(readLegacySettings);
        return ExecuteAsync<UserSettingsCommit?>(() =>
        {
            // Parsing errors also protect existing modern data; absence is not inferred from invalid input.
            if (ReadDocument(registration.PrimaryPath) is not null || ReadDocument(registration.BackupPath) is not null)
            {
                return null;
            }

            return Save(Serialize(readLegacySettings()), cancellationToken);
        }, cancellationToken);
    }

    private UserSettingsCommit Save(JsonElement settings, CancellationToken cancellationToken)
    {
        ValidateModel(settings);
        Document? previous = ReadDocument(registration.PrimaryPath);
        if (previous is not null)
        {
            ReadCompatible(previous);
        }
        else if (ReadDocument(registration.BackupPath) is not null)
        {
            throw new InvalidDataException(SettingsSR.RecoveryRequired);
        }

        return Commit(settings, previous, preserveBackup: true, cancellationToken);
    }

    private T ReadCompatible(Document document)
    {
        if (document.SchemaVersion != registration.SchemaVersion)
        {
            throw new InvalidDataException(document.SchemaVersion > registration.SchemaVersion
                ? SettingsSR.NewerSchema
                : SettingsSR.UpgradeRequired);
        }

        return ValidateModel(document.Settings);
    }

    private T ValidateModel(JsonElement settings)
    {
        RequireObject(settings);
        T value = settings.Deserialize(registration.TypeInfo) ?? throw new InvalidDataException(SettingsSR.InvalidModel);
        registration.Validate?.Invoke(value);

        return value;
    }

    private JsonElement Serialize(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return JsonSerializer.SerializeToElement(value, registration.TypeInfo);
    }

    private static void RequireObject(JsonElement settings)
    {
        if (settings.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(SettingsSR.InvalidModel);
        }
    }

    private static Document? ReadDocument(string path)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }

        try
        {
            using JsonDocument json = JsonDocument.Parse(bytes);
            JsonElement root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || root.EnumerateObject().Count() != 3
                || !root.TryGetProperty("schemaVersion", out JsonElement schema)
                || schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out int version) || version <= 0
                || !root.TryGetProperty("revision", out JsonElement revision)
                || revision.ValueKind != JsonValueKind.Number || !revision.TryGetInt64(out long number) || number <= 0
                || !root.TryGetProperty("settings", out JsonElement settings))
            {
                throw new InvalidDataException(SettingsSR.InvalidDocument);
            }

            RequireObject(settings);
            return new Document(version, number, settings.Clone());
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(string.Format(SettingsSR.InvalidFile, path), exception);
        }
    }

    private UserSettingsCommit Commit(
        JsonElement settings,
        Document? previous,
        bool preserveBackup,
        CancellationToken cancellationToken,
        bool? primaryPresent = null)
    {
        long revision = checked(Math.Max(_revision, previous?.Revision ?? 0) + 1);
        string temporary = registration.PrimaryPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                using Utf8JsonWriter writer = new(stream);
                writer.WriteStartObject();
                writer.WriteNumber("schemaVersion", registration.SchemaVersion);
                writer.WriteNumber("revision", revision);
                writer.WritePropertyName("settings");
                settings.WriteTo(writer);
                writer.WriteEndObject();
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (primaryPresent ?? (previous is not null))
            {
                File.Replace(temporary, registration.PrimaryPath, preserveBackup ? registration.BackupPath : null);
            }
            else
            {
                File.Move(temporary, registration.PrimaryPath);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception cleanupFailure) when (cleanupFailure is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
            {
                throw new AggregateException(exception, cleanupFailure);
            }

            throw;
        }

        _revision = revision;
        Notification notification = new(registration.SchemaVersion, revision, settings, Changed);
        _pendingNotification = notification;
        lock (_notificationGate)
        {
            _notifications.Enqueue(notification);
        }

        return notification.Commit;
    }

    private Task<TResult> ExecuteAsync<TResult>(Func<TResult> operation, CancellationToken cancellationToken)
        => Task.Run(async () =>
        {
            using CancellationTokenSource waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            waiting.CancelAfter(registration.LockTimeout);
            bool gateAcquired = false;
            try
            {
                await _gate.WaitAsync(waiting.Token).ConfigureAwait(false);
                gateAcquired = true;
                Directory.CreateDirectory(registration.Directory);
                using FileStream fileLock = await AcquireFileLockAsync(waiting.Token).ConfigureAwait(false);
                waiting.CancelAfter(Timeout.InfiniteTimeSpan);
                cancellationToken.ThrowIfCancellationRequested();
                return operation();
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested && waiting.IsCancellationRequested)
            {
                throw new TimeoutException(string.Format(SettingsSR.LockTimeout, registration.PrimaryPath), exception);
            }
            finally
            {
                if (gateAcquired)
                {
                    Notification? notification = _pendingNotification;
                    _pendingNotification = null;
                    _gate.Release();
                    notification?.Ready.SetResult();
                    StartNotifications();
                }
            }
        }, cancellationToken);

    private async Task<FileStream> AcquireFileLockAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(registration.PrimaryPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException exception) when ((exception.HResult & 0xffff) is 32 or 33)
            {
                // Retry sharing/lock violations only; permissions and unrelated I/O failures propagate.
                await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void StartNotifications()
    {
        lock (_notificationGate)
        {
            if (_dispatching || _notifications.Count == 0)
            {
                return;
            }

            _dispatching = true;
        }

        _ = Task.Run(DispatchNotificationsAsync);
    }

    private async Task DispatchNotificationsAsync()
    {
        while (true)
        {
            Notification notification;
            lock (_notificationGate)
            {
                if (_notifications.Count == 0)
                {
                    _dispatching = false;
                    return;
                }

                notification = _notifications.Dequeue();
            }

            await notification.Ready.Task.ConfigureAwait(false);
            List<Exception> failures = [];
            foreach (Delegate subscriber in notification.Subscribers?.GetInvocationList() ?? [])
            {
                try
                {
                    ((EventHandler<UserSettingsChangedEventArgs>)subscriber)(this, new(notification.Commit));
                }
                catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
                {
                    failures.Add(exception);
                }
            }

            if (failures.Count == 0)
            {
                notification.Completion.SetResult();
            }
            else
            {
                notification.Completion.SetException(failures);
            }
        }
    }

    /// <summary>
    ///  Holds a registration-time copy of mutable caller options without acquiring storage resources.
    /// </summary>
    internal sealed class Registration
    {
        internal Registration(string applicationId, int version, JsonTypeInfo<T> typeInfo, Func<T> defaults, JsonUserSettingsOptions<T> options)
        {
            string device = applicationId.Split('.')[0].ToUpperInvariant();
            bool reserved = device is "CON" or "PRN" or "AUX" or "NUL"
                || (device.Length == 4 && device[3] is >= '1' and <= '9'
                    && (device.StartsWith("COM", StringComparison.Ordinal) || device.StartsWith("LPT", StringComparison.Ordinal)));
            if (applicationId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || applicationId is "." or ".." || applicationId.EndsWith(' ') || applicationId.EndsWith('.')
                || reserved)
            {
                throw new ArgumentException(SettingsSR.InvalidIdentity, nameof(applicationId));
            }

            if (options.LockTimeout <= TimeSpan.Zero || options.LockTimeout.TotalMilliseconds > uint.MaxValue - 1)
            {
                throw new ArgumentOutOfRangeException(nameof(options), SettingsSR.InvalidTimeout);
            }

            string directory = options.StorageDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinForms", applicationId);
            if (!Path.IsPathFullyQualified(directory))
            {
                throw new ArgumentException(SettingsSR.AbsoluteDirectory, nameof(options));
            }

            Directory = Path.GetFullPath(directory);
            SchemaVersion = version;
            TypeInfo = typeInfo;
            CreateDefaults = defaults;
            Validate = options.Validate;
            LockTimeout = options.LockTimeout;
            Migrations = new Dictionary<int, Func<JsonElement, JsonElement>>(options.Migrations);
            if (Migrations.Any(pair => pair.Key <= 0 || pair.Key >= version || pair.Value is null))
            {
                throw new ArgumentException(SettingsSR.InvalidMigration, nameof(options));
            }
        }

        internal string Directory { get; }
        internal string PrimaryPath => Path.Combine(Directory, "settings.json");
        internal string BackupPath => PrimaryPath + ".bak";
        internal int SchemaVersion { get; }
        internal JsonTypeInfo<T> TypeInfo { get; }
        internal Func<T> CreateDefaults { get; }
        internal Action<T>? Validate { get; }
        internal TimeSpan LockTimeout { get; }
        internal Dictionary<int, Func<JsonElement, JsonElement>> Migrations { get; }
    }

    /// <summary>
    ///  Owns a parsed envelope and JSON model detached from its parsing document.
    /// </summary>
    private sealed record Document(int SchemaVersion, long Revision, JsonElement Settings);

    /// <summary>
    ///  Captures subscribers at commit time and reports their failures on a separate observable task.
    /// </summary>
    private sealed class Notification
    {
        internal Notification(int version, long revision, JsonElement settings, EventHandler<UserSettingsChangedEventArgs>? subscribers)
        {
            Commit = new(version, revision, settings, Completion.Task);
            Subscribers = subscribers;
        }

        internal TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal UserSettingsCommit Commit { get; }
        internal EventHandler<UserSettingsChangedEventArgs>? Subscribers { get; }
    }
}
