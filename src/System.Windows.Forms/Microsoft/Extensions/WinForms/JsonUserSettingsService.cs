// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Configuration;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Persists per-user settings in a versioned JSON file.
/// </summary>
public sealed class JsonUserSettingsService : IUserSettingsService
{
    private static readonly TimeSpan s_lockRetryDelay = TimeSpan.FromMilliseconds(50);
    private readonly string _filePath;
    private readonly string _backupPath;
    private readonly TimeSpan _lockTimeout;
    private readonly int _schemaVersion;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly Dictionary<int, IUserSettingsMigration> _migrations;

    /// <summary>
    ///  Initializes a new instance of the <see cref="JsonUserSettingsService"/>
    ///  class.
    /// </summary>
    /// <param name="options">The settings storage and schema options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">
    ///  An application identifier or file path is invalid.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///  The schema version or lock timeout is invalid.
    /// </exception>
    public JsonUserSettingsService(UserSettingsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.SchemaVersion < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The settings schema version must be greater than zero.");
        }

        if (options.LockTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The settings lock timeout must be greater than zero.");
        }

        ArgumentNullException.ThrowIfNull(options.SerializerOptions);

        _schemaVersion = options.SchemaVersion;
        _lockTimeout = options.LockTimeout;
        _serializerOptions = new(options.SerializerOptions);
        _filePath = ResolveFilePath(options);
        _backupPath = $"{_filePath}.bak";
        _migrations = CreateMigrationMap(options);
    }

    /// <inheritdoc/>
    public event EventHandler<UserSettingsChangedEventArgs>? Changed;

    /// <inheritdoc/>
    public int SchemaVersion => _schemaVersion;

    /// <inheritdoc/>
    public Task<int?> GetStoredSchemaVersionAsync(CancellationToken cancellationToken = default)
        => ExecuteLockedAsync(
            async token =>
            {
                (SettingsDocument? document, bool recovered) =
                    await ReadDocumentWithRecoveryAsync(token).ConfigureAwait(false);

                return (document?.SchemaVersion, recovered ? document : null);
            },
            cancellationToken);

    /// <inheritdoc/>
    public Task<TSettings> LoadAsync<TSettings>(
        TSettings defaults,
        CancellationToken cancellationToken = default)
        where TSettings : class
    {
        ArgumentNullException.ThrowIfNull(defaults);

        return ExecuteLockedAsync(
            async token =>
            {
                (SettingsDocument? document, bool recovered) =
                    await ReadDocumentWithRecoveryAsync(token).ConfigureAwait(false);

                if (document is null)
                {
                    return (defaults, null);
                }

                bool upgraded = document.SchemaVersion < _schemaVersion;

                if (upgraded)
                {
                    document = Upgrade(document);
                    await WriteDocumentAsync(document, preservePrevious: true, token).ConfigureAwait(false);
                }
                else if (document.SchemaVersion > _schemaVersion)
                {
                    throw CreateUnsupportedVersionException(document.SchemaVersion);
                }

                TSettings settings = document.Settings.Deserialize<TSettings>(_serializerOptions)
                    ?? throw new InvalidDataException(
                        $"The user settings file '{_filePath}' does not contain a valid settings object.");

                return (settings, recovered || upgraded ? document : null);
            },
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task SaveAsync<TSettings>(
        TSettings settings,
        CancellationToken cancellationToken = default)
        where TSettings : class
    {
        ArgumentNullException.ThrowIfNull(settings);

        JsonObject settingsObject = SerializeSettings(settings);

        await ExecuteLockedAsync(
            async token =>
            {
                SettingsDocument? currentDocument =
                    (await ReadDocumentWithRecoveryAsync(token).ConfigureAwait(false)).Document;

                if (currentDocument is not null && currentDocument.SchemaVersion > _schemaVersion)
                {
                    throw CreateUnsupportedVersionException(currentDocument.SchemaVersion);
                }

                await WriteDocumentAsync(
                    new SettingsDocument(_schemaVersion, settingsObject),
                    preservePrevious: true,
                    token).ConfigureAwait(false);

                return (true, new SettingsDocument(_schemaVersion, settingsObject));
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task ResetAsync<TSettings>(
        TSettings defaults,
        CancellationToken cancellationToken = default)
        where TSettings : class
        => SaveAsync(defaults, cancellationToken);

    /// <inheritdoc/>
    public async Task UpgradeAsync(CancellationToken cancellationToken = default)
    {
        await ExecuteLockedAsync(
            async token =>
            {
                (SettingsDocument? document, bool recovered) =
                    await ReadDocumentWithRecoveryAsync(token).ConfigureAwait(false);

                if (document is null)
                {
                    return (true, null);
                }

                if (document.SchemaVersion > _schemaVersion)
                {
                    throw CreateUnsupportedVersionException(document.SchemaVersion);
                }

                bool upgraded = document.SchemaVersion < _schemaVersion;

                if (upgraded)
                {
                    document = Upgrade(document);
                    await WriteDocumentAsync(document, preservePrevious: true, token).ConfigureAwait(false);
                }

                return (true, recovered || upgraded ? document : null);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<bool> MigrateFromLegacySettingsAsync(
        ApplicationSettingsBase legacySettings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(legacySettings);

        return ExecuteLockedAsync(
            async token =>
            {
                SettingsDocument? existingDocument =
                    (await ReadDocumentWithRecoveryAsync(token).ConfigureAwait(false)).Document;

                if (existingDocument is not null)
                {
                    return (false, null);
                }

                JsonObject settings = new();

                foreach (SettingsProperty property in legacySettings.Properties)
                {
                    if (property.Attributes[typeof(UserScopedSettingAttribute)] is null)
                    {
                        continue;
                    }

                    JsonNode? value = JsonSerializer.SerializeToNode(
                        legacySettings[property.Name],
                        property.PropertyType,
                        _serializerOptions);
                    settings[property.Name] = value;
                }

                await WriteDocumentAsync(
                    new SettingsDocument(_schemaVersion, settings),
                    preservePrevious: false,
                    token).ConfigureAwait(false);

                return (true, new SettingsDocument(_schemaVersion, settings));
            },
            cancellationToken);
    }

    private async Task<TResult> ExecuteLockedAsync<TResult>(
        Func<CancellationToken, Task<(TResult Result, SettingsDocument? ChangedDocument)>> operation,
        CancellationToken cancellationToken)
    {
        FileStream fileLock = await AcquireFileLockAsync(cancellationToken).ConfigureAwait(false);
        TResult result;
        SettingsDocument? changedDocument;

        using (fileLock)
        {
            (result, changedDocument) = await operation(cancellationToken).ConfigureAwait(false);
        }

        if (changedDocument is not null)
        {
            Changed?.Invoke(
                this,
                new UserSettingsChangedEventArgs(changedDocument.SchemaVersion, changedDocument.Settings));
        }

        return result;
    }

    private async Task<FileStream> AcquireFileLockAsync(CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException($"The settings path '{_filePath}' has no parent directory.");

        Directory.CreateDirectory(directory);

        string lockPath = $"{_filePath}.lock";
        DateTime deadline = DateTime.UtcNow + _lockTimeout;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.Asynchronous);
            }
            catch (IOException exception) when (IsLockContention(exception))
            {
                TimeSpan remaining = deadline - DateTime.UtcNow;

                if (remaining <= TimeSpan.Zero)
                {
                    throw new TimeoutException(
                        $"Timed out waiting for the user settings file '{_filePath}' to become available.",
                        exception);
                }

                await Task.Delay(
                    remaining < s_lockRetryDelay ? remaining : s_lockRetryDelay,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static bool IsLockContention(IOException exception)
    {
        int nativeErrorCode = exception.HResult & 0xFFFF;
        return nativeErrorCode is 32 or 33;
    }

    private async Task<(SettingsDocument? Document, bool Recovered)> ReadDocumentWithRecoveryAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            if (File.Exists(_backupPath))
            {
                SettingsDocument backupDocument =
                    await ReadDocumentAsync(_backupPath, cancellationToken).ConfigureAwait(false);

                if (backupDocument.SchemaVersion > _schemaVersion)
                {
                    throw CreateUnsupportedVersionException(backupDocument.SchemaVersion);
                }

                await WriteDocumentAsync(
                    backupDocument,
                    preservePrevious: false,
                    cancellationToken).ConfigureAwait(false);

                return (backupDocument, true);
            }

            return (null, false);
        }

        InvalidDataException primaryFailure;

        try
        {
            return (await ReadDocumentAsync(_filePath, cancellationToken).ConfigureAwait(false), false);
        }
        catch (InvalidDataException exception)
        {
            primaryFailure = exception;
        }

        if (!File.Exists(_backupPath))
        {
            throw new InvalidDataException(
                $"The user settings file '{_filePath}' is invalid. The original file was preserved; " +
                "repair it or restore a valid backup before trying again.",
                primaryFailure);
        }

        SettingsDocument backup;

        try
        {
            backup = await ReadDocumentAsync(_backupPath, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException backupFailure)
        {
            throw new InvalidDataException(
                $"Both the user settings file '{_filePath}' and its backup are invalid. " +
                "Neither file was replaced.",
                new AggregateException(primaryFailure, backupFailure));
        }

        if (backup.SchemaVersion > _schemaVersion)
        {
            throw CreateUnsupportedVersionException(backup.SchemaVersion);
        }

        string preservedPath = $"{_filePath}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfffffff}";
        File.Copy(_filePath, preservedPath);
        await WriteDocumentAsync(backup, preservePrevious: false, cancellationToken).ConfigureAwait(false);

        return (backup, true);
    }

    private static async Task<SettingsDocument> ReadDocumentAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            JsonNode? node;

            await using (stream.ConfigureAwait(false))
            {
                node = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            if (node is not JsonObject root
                || root["schemaVersion"] is not JsonValue versionNode
                || !versionNode.TryGetValue(out int version)
                || version < 1
                || root["settings"] is not JsonObject settings)
            {
                throw new InvalidDataException(
                    $"The user settings file '{path}' does not contain a valid versioned settings object.");
            }

            return new SettingsDocument(version, settings);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The user settings file '{path}' contains invalid JSON.", exception);
        }
    }

    private SettingsDocument Upgrade(SettingsDocument document)
    {
        JsonObject settings = document.Settings;
        int version = document.SchemaVersion;

        while (version < _schemaVersion)
        {
            if (!_migrations.TryGetValue(version, out IUserSettingsMigration? migration))
            {
                throw new InvalidOperationException(
                    $"No user settings migration is registered for schema version {version}.");
            }

            settings = migration.Migrate(settings.DeepClone().AsObject())
                ?? throw new InvalidOperationException(
                    $"The user settings migration from schema version {version} returned null.");
            version++;
        }

        return new SettingsDocument(version, settings);
    }

    private async Task WriteDocumentAsync(
        SettingsDocument document,
        bool preservePrevious,
        CancellationToken cancellationToken)
    {
        string temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            await using (stream.ConfigureAwait(false))
            {
                using Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true });

                writer.WriteStartObject();
                writer.WriteNumber("schemaVersion", document.SchemaVersion);
                writer.WritePropertyName("settings");
                document.Settings.WriteTo(writer, _serializerOptions);
                writer.WriteEndObject();
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (preservePrevious && File.Exists(_filePath))
            {
                File.Copy(_filePath, _backupPath, overwrite: true);
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private JsonObject SerializeSettings<TSettings>(TSettings settings)
        where TSettings : class
    {
        JsonNode? node = JsonSerializer.SerializeToNode(settings, _serializerOptions);

        return node as JsonObject
            ?? throw new ArgumentException(
                "User settings must serialize to a JSON object.",
                nameof(settings));
    }

    private InvalidOperationException CreateUnsupportedVersionException(int storedVersion)
        => new(
            $"The user settings file '{_filePath}' has schema version {storedVersion}, " +
            $"which is newer than the supported version {_schemaVersion}.");

    private static string ResolveFilePath(UserSettingsOptions options)
    {
        if (options.FilePath is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(options.FilePath);
            return Path.GetFullPath(options.FilePath);
        }

        string applicationId = options.ApplicationId
            ?? Assembly.GetEntryAssembly()?.GetName().Name
            ?? "WinFormsApplication";

        if (string.IsNullOrWhiteSpace(applicationId)
            || applicationId is "." or ".."
            || applicationId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || applicationId.Contains(Path.DirectorySeparatorChar)
            || applicationId.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException(
                "The application identifier must be a valid single directory name.",
                nameof(options));
        }

        string localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            throw new InvalidOperationException(
                "The current user's local application data directory is unavailable.");
        }

        return Path.Combine(localApplicationData, "WinForms", applicationId, "user-settings.json");
    }

    private static Dictionary<int, IUserSettingsMigration> CreateMigrationMap(UserSettingsOptions options)
    {
        Dictionary<int, IUserSettingsMigration> migrations = new();

        foreach (IUserSettingsMigration migration in options.Migrations)
        {
            ArgumentNullException.ThrowIfNull(migration);

            if (migration.FromVersion < 1 || migration.FromVersion >= options.SchemaVersion)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    $"Migration source version {migration.FromVersion} is outside the supported schema range.");
            }

            if (!migrations.TryAdd(migration.FromVersion, migration))
            {
                throw new ArgumentException(
                    $"More than one user settings migration starts at schema version {migration.FromVersion}.",
                    nameof(options));
            }
        }

        return migrations;
    }

    /// <summary>
    ///  Represents the versioned settings document persisted by the service.
    /// </summary>
    private sealed record SettingsDocument(int SchemaVersion, JsonObject Settings);
}
