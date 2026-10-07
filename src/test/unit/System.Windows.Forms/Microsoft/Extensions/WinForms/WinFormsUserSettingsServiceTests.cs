// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Configuration;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.WinForms;

namespace System.Windows.Forms.Tests;

/// <summary>
///  Tests the application builder user-settings service.
/// </summary>
public class WinFormsUserSettingsServiceTests
{
    [Fact]
    public async Task LoadAsync_MissingFileReturnsDefaultsWithoutCreatingFile()
    {
        string directory = CreateTemporaryDirectory();
        string filePath = Path.Combine(directory, "settings.json");
        JsonUserSettingsService service = new(new UserSettingsOptions { FilePath = filePath });
        TestSettings defaults = new() { Name = "default" };

        try
        {
            TestSettings result = await service.LoadAsync(defaults, TestContext.Current.CancellationToken);

            Assert.Same(defaults, result);
            Assert.Null(await service.GetStoredSchemaVersionAsync(TestContext.Current.CancellationToken));
            Assert.False(File.Exists(filePath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveLoadAndReset_PersistVersionedDataAndNotifyAfterEachCommit()
    {
        string directory = CreateTemporaryDirectory();
        string filePath = Path.Combine(directory, "settings.json");
        JsonUserSettingsService service = new(new UserSettingsOptions { FilePath = filePath });
        List<UserSettingsChangedEventArgs> changes = [];
        service.Changed += (_, e) => changes.Add(e);

        try
        {
            await service.SaveAsync(
                new TestSettings { Name = "saved" },
                TestContext.Current.CancellationToken);

            TestSettings loaded = await service.LoadAsync(new TestSettings(), TestContext.Current.CancellationToken);

            Assert.Equal("saved", loaded.Name);
            Assert.Equal(1, await service.GetStoredSchemaVersionAsync(TestContext.Current.CancellationToken));
            Assert.Single(changes);
            Assert.Equal(1, changes[0].SchemaVersion);
            Assert.Equal("saved", changes[0].Settings.GetProperty("name").GetString());

            await service.ResetAsync(
                new TestSettings { Name = "default" },
                TestContext.Current.CancellationToken);

            Assert.Equal(
                "default",
                (await service.LoadAsync(new TestSettings(), TestContext.Current.CancellationToken)).Name);
            Assert.Equal(2, changes.Count);
            Assert.Equal("default", changes[1].Settings.GetProperty("name").GetString());
            Assert.True(File.Exists($"{filePath}.bak"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_RunsOrderedMigrationsAndAtomicallyStoresCurrentVersion()
    {
        string directory = CreateTemporaryDirectory();
        string filePath = Path.Combine(directory, "settings.json");
        File.WriteAllText(filePath, """{"schemaVersion":1,"settings":{"Name":"original"}}""");
        UserSettingsOptions options = new()
        {
            FilePath = filePath,
            SchemaVersion = 3
        };
        options.Migrations.Add(new AddThemeMigration());
        options.Migrations.Add(new RenameNameMigration());
        JsonUserSettingsService service = new(options);
        List<UserSettingsChangedEventArgs> changes = [];
        service.Changed += (_, e) => changes.Add(e);

        try
        {
            TestSettings settings = await service.LoadAsync(new TestSettings(), TestContext.Current.CancellationToken);

            Assert.Equal("migrated", settings.Name);
            Assert.Equal("light", settings.Theme);
            Assert.Equal(3, await service.GetStoredSchemaVersionAsync(TestContext.Current.CancellationToken));
            Assert.Single(changes);
            Assert.True(File.Exists($"{filePath}.bak"));
            using JsonDocument backup = JsonDocument.Parse(File.ReadAllText($"{filePath}.bak"));
            Assert.Equal(1, backup.RootElement.GetProperty("schemaVersion").GetInt32());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task UpgradeAsync_MissingMigrationLeavesOriginalFileUnchanged()
    {
        string directory = CreateTemporaryDirectory();
        string filePath = Path.Combine(directory, "settings.json");
        string original = """{"schemaVersion":1,"settings":{"Name":"original"}}""";
        File.WriteAllText(filePath, original);
        JsonUserSettingsService service = new(
            new UserSettingsOptions { FilePath = filePath, SchemaVersion = 2 });

        try
        {
            InvalidOperationException exception =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => service.UpgradeAsync(TestContext.Current.CancellationToken));

            Assert.Contains("No user settings migration", exception.Message);
            Assert.Equal(original, File.ReadAllText(filePath));
            Assert.False(File.Exists($"{filePath}.bak"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_FutureSchemaIsReportedWithoutDowngrading()
    {
        string directory = CreateTemporaryDirectory();
        string filePath = Path.Combine(directory, "settings.json");
        string original = """{"schemaVersion":4,"settings":{"Name":"newer"}}""";
        File.WriteAllText(filePath, original);
        JsonUserSettingsService service = new(new UserSettingsOptions { FilePath = filePath });

        try
        {
            Assert.Equal(4, await service.GetStoredSchemaVersionAsync(TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.LoadAsync(new TestSettings(), TestContext.Current.CancellationToken));
            Assert.Equal(original, File.ReadAllText(filePath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_FutureSchemaIsReportedWithoutDowngrading()
    {
        string directory = CreateTemporaryDirectory();
        string filePath = Path.Combine(directory, "settings.json");
        string original = """{"schemaVersion":4,"settings":{"Name":"newer"}}""";
        File.WriteAllText(filePath, original);
        JsonUserSettingsService service = new(new UserSettingsOptions { FilePath = filePath });

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.SaveAsync(new TestSettings { Name = "older" }, TestContext.Current.CancellationToken));

            Assert.Equal(original, File.ReadAllText(filePath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_MissingPrimaryRestoresValidBackup()
    {
        string directory = CreateTemporaryDirectory();
        string filePath = Path.Combine(directory, "settings.json");
        File.WriteAllText(
            $"{filePath}.bak",
            """{"schemaVersion":1,"settings":{"Name":"backup"}}""");
        JsonUserSettingsService service = new(new UserSettingsOptions { FilePath = filePath });

        try
        {
            TestSettings settings = await service.LoadAsync(
                new TestSettings(),
                TestContext.Current.CancellationToken);

            Assert.Equal("backup", settings.Name);
            Assert.True(File.Exists(filePath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_RecoversValidBackupBeforeReplacingCorruptPrimary()
    {
        string directory = CreateTemporaryDirectory();
        string filePath = Path.Combine(directory, "settings.json");
        string original = """{"schemaVersion":1,"settings":{"Name":"backup"}}""";
        File.WriteAllText(filePath, "{ invalid json");
        File.WriteAllText($"{filePath}.bak", original);
        JsonUserSettingsService service = new(new UserSettingsOptions { FilePath = filePath });

        try
        {
            await service.SaveAsync(
                new TestSettings { Name = "saved" },
                TestContext.Current.CancellationToken);

            Assert.Equal("saved", (await service.LoadAsync(
                new TestSettings(),
                TestContext.Current.CancellationToken)).Name);
            using JsonDocument backup = JsonDocument.Parse(File.ReadAllText($"{filePath}.bak"));
            Assert.Equal(1, backup.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("backup", backup.RootElement.GetProperty("settings").GetProperty("Name").GetString());
            Assert.Contains(Directory.GetFiles(directory, "settings.json.corrupt-*"), path =>
                File.ReadAllText(path) == "{ invalid json");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_CorruptPrimaryRecoversFromBackupAndPreservesCorruptFile()
    {
        string directory = CreateTemporaryDirectory();
        string filePath = Path.Combine(directory, "settings.json");
        JsonUserSettingsService service = new(new UserSettingsOptions { FilePath = filePath });

        try
        {
            await service.SaveAsync(
                new TestSettings { Name = "known-good" },
                TestContext.Current.CancellationToken);
            await service.SaveAsync(
                new TestSettings { Name = "newer" },
                TestContext.Current.CancellationToken);
            File.WriteAllText(filePath, "{ invalid json");

            TestSettings loaded = await service.LoadAsync(new TestSettings(), TestContext.Current.CancellationToken);
            string[] preservedFiles = Directory.GetFiles(directory, "settings.json.corrupt-*");

            Assert.Equal("known-good", loaded.Name);
            Assert.Single(preservedFiles);
            Assert.Equal("{ invalid json", File.ReadAllText(preservedFiles[0]));
            Assert.Equal(
                "known-good",
                (await service.LoadAsync(new TestSettings(), TestContext.Current.CancellationToken)).Name);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_CorruptFileWithoutBackupIsPreservedAndReported()
    {
        string directory = CreateTemporaryDirectory();
        string filePath = Path.Combine(directory, "settings.json");
        string corruptSettings = "{ invalid json";
        File.WriteAllText(filePath, corruptSettings);
        JsonUserSettingsService service = new(new UserSettingsOptions { FilePath = filePath });

        try
        {
            InvalidDataException exception =
                await Assert.ThrowsAsync<InvalidDataException>(
                    () => service.LoadAsync(new TestSettings(), TestContext.Current.CancellationToken));

            Assert.Contains(filePath, exception.Message);
            Assert.Equal(corruptSettings, File.ReadAllText(filePath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_CompetingServiceInstancesWriteCompleteDocuments()
    {
        string directory = CreateTemporaryDirectory();
        string filePath = Path.Combine(directory, "settings.json");
        JsonUserSettingsService first = new(new UserSettingsOptions { FilePath = filePath });
        JsonUserSettingsService second = new(new UserSettingsOptions { FilePath = filePath });

        try
        {
            Task[] writes = Enumerable.Range(0, 8)
                .Select(index =>
                {
                    JsonUserSettingsService service = index % 2 == 0 ? first : second;
                    return service.SaveAsync(
                        new TestSettings { Name = $"writer-{index}" },
                        TestContext.Current.CancellationToken);
                })
                .ToArray();
            await Task.WhenAll(writes);

            TestSettings result = await first.LoadAsync(new TestSettings(), TestContext.Current.CancellationToken);

            Assert.StartsWith("writer-", result.Name, StringComparison.Ordinal);
            Assert.InRange(int.Parse(result.Name["writer-".Length..]), 0, 7);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task MigrateFromLegacySettings_ImportsOnlyUserScopedValues()
    {
        string directory = CreateTemporaryDirectory();
        string filePath = Path.Combine(directory, "settings.json");
        JsonUserSettingsService service = new(new UserSettingsOptions { FilePath = filePath });
        LegacySettings legacySettings = new() { UserValue = "legacy-user-value" };

        try
        {
            Assert.True(await service.MigrateFromLegacySettingsAsync(
                legacySettings,
                TestContext.Current.CancellationToken));
            Assert.False(await service.MigrateFromLegacySettingsAsync(
                legacySettings,
                TestContext.Current.CancellationToken));

            ImportedSettings imported = await service.LoadAsync(
                new ImportedSettings(),
                TestContext.Current.CancellationToken);

            Assert.Equal("legacy-user-value", imported.UserValue);
            Assert.Equal("application-default", imported.ApplicationValue);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AddUserSettings_ProjectsSelectedValuesAndReloadsConfiguration()
    {
        string directory = CreateTemporaryDirectory();
        string filePath = Path.Combine(directory, "settings.json");
        WinFormsApplicationBuilder builder = WinFormsApplicationBuilder.CreateBuilder();
        builder.AddUserSettings(
            new UserSettingsOptions { FilePath = filePath },
            includeInConfiguration: true,
            settingKeys: ["Theme"]);
        using WinFormsApplication application = builder.UseApplicationContext().Build();
        IConfiguration configuration = builder.Configuration;
        IUserSettingsService service =
            application.Options.Host!.Services.GetRequiredService<IUserSettingsService>();
        bool configurationChanged = false;
        using IDisposable registration = configuration.GetReloadToken()
            .RegisterChangeCallback(_ => configurationChanged = true, null);

        try
        {
            await service.SaveAsync(
                new TestSettings { Theme = "dark", Name = "private" },
                TestContext.Current.CancellationToken);

            Assert.Equal("dark", configuration["UserSettings:Theme"]);
            Assert.Null(configuration["UserSettings:Name"]);
            Assert.True(configurationChanged);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AddUserSettings_RegistersCustomServiceInstance()
    {
        string directory = CreateTemporaryDirectory();
        JsonUserSettingsService settingsService = new(
            new UserSettingsOptions { FilePath = Path.Combine(directory, "settings.json") });
        WinFormsApplicationBuilder builder = WinFormsApplicationBuilder.CreateBuilder();
        builder.AddUserSettings(settingsService);
        using WinFormsApplication application = builder.UseApplicationContext().Build();

        try
        {
            IUserSettingsService registeredService =
                application.Options.Host!.Services.GetRequiredService<IUserSettingsService>();

            Assert.Same(settingsService, registeredService);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_InvalidPathFailureIsSurfaced()
    {
        string directory = CreateTemporaryDirectory();
        string blockerPath = Path.Combine(directory, "file-not-directory");
        File.WriteAllText(blockerPath, "blocker");
        JsonUserSettingsService service = new(
            new UserSettingsOptions { FilePath = Path.Combine(blockerPath, "settings.json") });

        try
        {
            await Assert.ThrowsAnyAsync<IOException>(
                () => service.SaveAsync(
                    new TestSettings { Name = "value" },
                    TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"WinFormsSettings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        return directory;
    }

    /// <summary>
    ///  Represents application settings used by the service tests.
    /// </summary>
    private sealed class TestSettings
    {
        /// <summary>
        ///  Gets or sets a display name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        ///  Gets or sets a theme name.
        /// </summary>
        public string Theme { get; set; } = string.Empty;
    }

    /// <summary>
    ///  Adds a theme value during the first schema migration.
    /// </summary>
    private sealed class AddThemeMigration : IUserSettingsMigration
    {
        /// <inheritdoc/>
        public int FromVersion => 1;

        /// <inheritdoc/>
        public JsonObject Migrate(JsonObject settings)
        {
            settings["Theme"] = "light";
            return settings;
        }
    }

    /// <summary>
    ///  Updates the display name during the second schema migration.
    /// </summary>
    private sealed class RenameNameMigration : IUserSettingsMigration
    {
        /// <inheritdoc/>
        public int FromVersion => 2;

        /// <inheritdoc/>
        public JsonObject Migrate(JsonObject settings)
        {
            settings["Name"] = "migrated";
            return settings;
        }
    }

    /// <summary>
    ///  Represents values produced by legacy generated settings.
    /// </summary>
    private sealed class LegacySettings : ApplicationSettingsBase
    {
        /// <summary>
        ///  Gets or sets a user-scoped value.
        /// </summary>
        [UserScopedSetting]
        [DefaultSettingValue("")]
        public string UserValue
        {
            get => (string)this[nameof(UserValue)];
            set => this[nameof(UserValue)] = value;
        }

        /// <summary>
        ///  Gets an application-scoped value.
        /// </summary>
        [ApplicationScopedSetting]
        [DefaultSettingValue("application-default")]
        public string ApplicationValue
            => (string)this[nameof(ApplicationValue)];
    }

    /// <summary>
    ///  Represents the typed settings returned by legacy migration.
    /// </summary>
    private sealed class ImportedSettings
    {
        /// <summary>
        ///  Gets or sets the imported user value.
        /// </summary>
        public string UserValue { get; set; } = string.Empty;

        /// <summary>
        ///  Gets or sets the application default.
        /// </summary>
        public string ApplicationValue { get; set; } = "application-default";
    }
}
