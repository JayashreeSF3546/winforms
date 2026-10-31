// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.DotNet.RemoteExecutor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.WinForms;

namespace System.Windows.Forms.Tests;

/// <summary>
///  Verifies persistence, failure boundaries, migration, and concurrent/reentrant settings notifications.
/// </summary>
[Collection("Sequential")]
public class WinFormsUserSettingsTests
{
    [Theory]
    [InlineData("../other")]
    [InlineData("CON")]
    [InlineData("lpt1.txt")]
    [InlineData("trailing.")]
    [InlineData("trailing ")]
    public void Registration_RejectsUnstableOrInvalidDirectoryIdentity(string applicationId)
    {
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddJsonUserSettings(
            applicationId, 1, SettingsJsonContext.Default.SettingsModel, () => new SettingsModel()));
    }

    [Fact]
    public void Registration_IsLazyAndSnapshotsOptions()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("SettingsRegistration-");
        string root = Path.Combine(directory.FullName, "not-created");
        try
        {
            JsonUserSettingsOptions<SettingsModel>? captured = null;
            using ServiceProvider provider = new ServiceCollection()
                .AddJsonUserSettings("product", 1, SettingsJsonContext.Default.SettingsModel, () => new SettingsModel(), options =>
                {
                    options.StorageDirectory = root;
                    captured = options;
                })
                .BuildServiceProvider();
            captured!.StorageDirectory = "relative-invalid-path";
            provider.GetRequiredService<IUserSettingsService<SettingsModel>>().Should().NotBeNull();
            Directory.Exists(root).Should().BeFalse();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task LoadSaveReset_PersistsAcrossInstancesAndKeepsValidatedBackup()
    {
        using SettingsScope scope = new();
        int changed = 0;
        scope.Service.Changed += (_, _) => changed++;
        (await scope.Service.LoadAsync(TestContext.Current.CancellationToken)).Value.Should().Be(7);
        (await scope.Service.GetSchemaVersionAsync(TestContext.Current.CancellationToken)).Should().BeNull();
        File.Exists(scope.Primary).Should().BeFalse();

        UserSettingsCommit first = await scope.Service.SaveAsync(new() { Value = 10 }, TestContext.Current.CancellationToken);
        await first.NotificationCompletion;
        using SettingsScope other = new(scope.Root);
        (await other.Service.LoadAsync(TestContext.Current.CancellationToken)).Value.Should().Be(10);
        (await other.Service.GetSchemaVersionAsync(TestContext.Current.CancellationToken)).Should().Be(1);
        UserSettingsCommit second = await scope.Service.SaveAsync(new() { Value = 20 }, TestContext.Current.CancellationToken);
        await second.NotificationCompletion;
        using JsonDocument backup = JsonDocument.Parse(await File.ReadAllTextAsync(scope.Backup, TestContext.Current.CancellationToken));
        backup.RootElement.GetProperty("settings").GetProperty("Value").GetInt32().Should().Be(10);
        first.Settings.GetProperty("Value").GetInt32().Should().Be(10);
        UserSettingsCommit reset = await scope.Service.ResetAsync(TestContext.Current.CancellationToken);
        await reset.NotificationCompletion;

        (await scope.Service.LoadAsync(TestContext.Current.CancellationToken)).Value.Should().Be(7);
        changed.Should().Be(3);
        reset.Revision.Should().BeGreaterThan(second.Revision);
        second.Revision.Should().BeGreaterThan(first.Revision);
        Directory.GetFiles(scope.Root, "*.tmp").Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Upgrade_ValidatesMigratedTypeBeforeCommitting(bool valid)
    {
        using SettingsScope scope = new(version: 2, configure: options =>
            options.Migrations.Add(1, _ => Parse(valid ? """{"Value":12}""" : """{"Value":"invalid"}""")));
        string original = """{"schemaVersion":1,"revision":3,"settings":{"Value":5}}""";
        await File.WriteAllTextAsync(scope.Primary, original, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidDataException>(() => scope.Service.LoadAsync(TestContext.Current.CancellationToken));

        if (valid)
        {
            UserSettingsCommit commit = await scope.Service.UpgradeAsync(TestContext.Current.CancellationToken);
            await commit.NotificationCompletion;
            (await scope.Service.LoadAsync(TestContext.Current.CancellationToken)).Value.Should().Be(12);
            (await scope.Service.GetSchemaVersionAsync(TestContext.Current.CancellationToken)).Should().Be(2);
            (await File.ReadAllTextAsync(scope.Backup, TestContext.Current.CancellationToken)).Should().Be(original);
            UserSettingsCommit repeated = await scope.Service.UpgradeAsync(TestContext.Current.CancellationToken);
            repeated.Revision.Should().Be(commit.Revision);
        }
        else
        {
            await Assert.ThrowsAsync<JsonException>(() => scope.Service.UpgradeAsync(TestContext.Current.CancellationToken));
            (await File.ReadAllTextAsync(scope.Primary, TestContext.Current.CancellationToken)).Should().Be(original);
            File.Exists(scope.Backup).Should().BeFalse();
            (await scope.Service.GetSchemaVersionAsync(TestContext.Current.CancellationToken)).Should().Be(1);
        }
    }

    [Fact]
    public async Task Upgrade_MissingOrFailingMigration_PreservesPrimary()
    {
        using SettingsScope scope = new(version: 3);
        string original = """{"schemaVersion":1,"revision":1,"settings":{"Value":5}}""";
        await File.WriteAllTextAsync(scope.Primary, original, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidDataException>(() => scope.Service.UpgradeAsync(TestContext.Current.CancellationToken));
        using SettingsScope failing = new(scope.Root, version: 3, configure: options =>
        {
            options.Migrations.Add(1, data => data);
            options.Migrations.Add(2, _ => throw new InvalidOperationException("Migration failed."));
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => failing.Service.UpgradeAsync(TestContext.Current.CancellationToken));
        (await File.ReadAllTextAsync(scope.Primary, TestContext.Current.CancellationToken)).Should().Be(original);
        File.Exists(scope.Backup).Should().BeFalse();
    }

    [Fact]
    public async Task NewerSchema_CannotBeOverwrittenEvenThroughRecovery()
    {
        using SettingsScope scope = new();
        string original = """{"schemaVersion":2,"revision":6,"settings":{"Value":15}}""";
        await File.WriteAllTextAsync(scope.Primary, original, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(scope.Backup, """{"schemaVersion":1,"revision":5,"settings":{"Value":10}}""", TestContext.Current.CancellationToken);

        (await scope.Service.GetSchemaVersionAsync(TestContext.Current.CancellationToken)).Should().Be(2);
        await Assert.ThrowsAsync<InvalidDataException>(() => scope.Service.LoadAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidDataException>(() => scope.Service.SaveAsync(new(), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidDataException>(() => scope.Service.ResetAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidDataException>(() => scope.Service.UpgradeAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Service.RecoverAsync(TestContext.Current.CancellationToken));
        (await File.ReadAllTextAsync(scope.Primary, TestContext.Current.CancellationToken)).Should().Be(original);
        Directory.GetFiles(scope.Root, "*.corrupt.*").Should().BeEmpty();
    }

    [Theory]
    [InlineData("{ broken")]
    [InlineData("{}")]
    [InlineData("""{"schemaVersion":1,"revision":2,"settings":{"Value":"wrong-type"}}""")]
    [InlineData("""{"schemaVersion":1,"revision":2,"settings":{"Value":-1}}""")]
    public async Task Recovery_IsExplicitAndPreservesDamagedPrimaryAndCompatibleBackup(string corrupt)
    {
        using SettingsScope scope = new();
        await scope.Service.SaveAsync(new() { Value = 10 }, TestContext.Current.CancellationToken);
        await scope.Service.SaveAsync(new() { Value = 20 }, TestContext.Current.CancellationToken);
        string backup = await File.ReadAllTextAsync(scope.Backup, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(scope.Primary, corrupt, TestContext.Current.CancellationToken);
        Exception? failure = await Record.ExceptionAsync(() => scope.Service.LoadAsync(TestContext.Current.CancellationToken));
        failure.Should().NotBeNull();
        (await File.ReadAllTextAsync(scope.Primary, TestContext.Current.CancellationToken)).Should().Be(corrupt);

        UserSettingsCommit recovered = await scope.Service.RecoverAsync(TestContext.Current.CancellationToken);
        await recovered.NotificationCompletion;

        (await scope.Service.LoadAsync(TestContext.Current.CancellationToken)).Value.Should().Be(10);
        (await File.ReadAllTextAsync(scope.Backup, TestContext.Current.CancellationToken)).Should().Be(backup);
        string quarantine = Directory.GetFiles(scope.Root, "*.corrupt.*").Should().ContainSingle().Subject;
        (await File.ReadAllTextAsync(quarantine, TestContext.Current.CancellationToken)).Should().Be(corrupt);
        recovered.Revision.Should().BeGreaterThan(2);
    }

    [Theory]
    [InlineData("{ broken")]
    [InlineData("""{"schemaVersion":1,"revision":1,"settings":{"Value":-1}}""")]
    public async Task Recovery_InvalidBackup_DoesNotMutateEitherInput(string backup)
    {
        using SettingsScope scope = new();
        await File.WriteAllTextAsync(scope.Primary, "{ original", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(scope.Backup, backup, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidDataException>(() => scope.Service.RecoverAsync(TestContext.Current.CancellationToken));

        (await File.ReadAllTextAsync(scope.Primary, TestContext.Current.CancellationToken)).Should().Be("{ original");
        (await File.ReadAllTextAsync(scope.Backup, TestContext.Current.CancellationToken)).Should().Be(backup);
        Directory.GetFiles(scope.Root, "*.corrupt.*").Should().BeEmpty();
    }

    [Fact]
    public async Task MissingPrimaryWithBackup_RequiresRecoveryNotDefaults()
    {
        using SettingsScope scope = new();
        await scope.Service.SaveAsync(new() { Value = 10 }, TestContext.Current.CancellationToken);
        await scope.Service.SaveAsync(new() { Value = 20 }, TestContext.Current.CancellationToken);
        File.Delete(scope.Primary);

        await Assert.ThrowsAsync<InvalidDataException>(() => scope.Service.LoadAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidDataException>(() => scope.Service.ResetAsync(TestContext.Current.CancellationToken));
        (await scope.Service.ImportAsync(() => new(), TestContext.Current.CancellationToken)).Should().BeNull();
        UserSettingsCommit recovered = await scope.Service.RecoverAsync(TestContext.Current.CancellationToken);
        await recovered.NotificationCompletion;
        (await scope.Service.LoadAsync(TestContext.Current.CancellationToken)).Value.Should().Be(10);
    }

    [Fact]
    public async Task Save_InvalidModelOrCanceledCommit_DoesNotMutateOrNotify()
    {
        using SettingsScope scope = new();
        await scope.Service.SaveAsync(new() { Value = 10 }, TestContext.Current.CancellationToken);
        string original = await File.ReadAllTextAsync(scope.Primary, TestContext.Current.CancellationToken);
        int changes = 0;
        scope.Service.Changed += (_, _) => changes++;
        await Assert.ThrowsAsync<InvalidDataException>(() => scope.Service.SaveAsync(new() { Value = -1 }, TestContext.Current.CancellationToken));
        using CancellationTokenSource cancellation = new();
        using SettingsScope cancelDuringValidation = new(scope.Root, configure: options => options.Validate = _ => cancellation.Cancel());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cancelDuringValidation.Service.SaveAsync(new() { Value = 20 }, cancellation.Token));

        (await File.ReadAllTextAsync(scope.Primary, TestContext.Current.CancellationToken)).Should().Be(original);
        changes.Should().Be(0);
        Directory.GetFiles(scope.Root, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task Contention_ReportsTimeoutAndCallerCancellationSeparately()
    {
        using SettingsScope scope = new(configure: options => options.LockTimeout = TimeSpan.FromMilliseconds(100));
        using FileStream held = new(scope.Primary + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        await Assert.ThrowsAsync<TimeoutException>(() => scope.Service.SaveAsync(new(), TestContext.Current.CancellationToken));
        using CancellationTokenSource cancellation = new();
        Task<UserSettingsCommit> save = scope.Service.SaveAsync(new(), cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => save);
        File.Exists(scope.Primary).Should().BeFalse();
    }

    [Fact]
    public async Task Contention_CoordinatesAnotherProcessAndReleasesEveryHandle()
    {
        using SettingsScope scope = new();
        using (FileStream held = new(scope.Primary + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            using RemoteInvokeHandle child = RemoteExecutor.Invoke(root =>
            {
                using SettingsScope other = new(root, configure: options => options.LockTimeout = TimeSpan.FromMilliseconds(100));
                Assert.Throws<TimeoutException>(() => other.Service.SaveAsync(new(), TestContext.Current.CancellationToken).GetAwaiter().GetResult());
            }, scope.Root);
        }

        UserSettingsCommit commit = await scope.Service.SaveAsync(new() { Value = 15 }, TestContext.Current.CancellationToken);
        await commit.NotificationCompletion;
        (await scope.Service.LoadAsync(TestContext.Current.CancellationToken)).Value.Should().Be(15);
    }

    [Fact]
    public async Task Notifications_AreOrderedAcrossConcurrentAndReentrantWrites()
    {
        using SettingsScope scope = new();
        List<long> revisions = [];
        UserSettingsCommit? reentrant = null;
        scope.Service.Changed += (_, e) =>
        {
            revisions.Add(e.Commit.Revision);
            if (e.Commit.Settings.GetProperty("Value").GetInt32() == 100)
            {
                reentrant = scope.Service.SaveAsync(new() { Value = 200 }).GetAwaiter().GetResult();
            }
        };
        UserSettingsCommit[] concurrent = await Task.WhenAll(Enumerable.Range(1, 10)
            .Select(value => scope.Service.SaveAsync(new() { Value = value })));
        await Task.WhenAll(concurrent.Select(commit => commit.NotificationCompletion));
        UserSettingsCommit first = await scope.Service.SaveAsync(new() { Value = 100 }, TestContext.Current.CancellationToken);
        await first.NotificationCompletion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await reentrant!.NotificationCompletion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        revisions.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems().And.HaveCount(12);
        (await scope.Service.LoadAsync(TestContext.Current.CancellationToken)).Value.Should().Be(200);
    }

    [Fact]
    public async Task SubscriberFailure_IsObservableAfterCommitAndDoesNotBlockOtherSubscribers()
    {
        using SettingsScope scope = new();
        InvalidOperationException expected = new("Subscriber failed after commit.");
        int delivered = 0;
        scope.Service.Changed += (_, _) => throw expected;
        scope.Service.Changed += (_, _) => delivered++;

        UserSettingsCommit commit = await scope.Service.SaveAsync(new() { Value = 10 }, TestContext.Current.CancellationToken);
        (await Assert.ThrowsAsync<InvalidOperationException>(() => commit.NotificationCompletion)).Should().BeSameAs(expected);

        delivered.Should().Be(1);
        (await scope.Service.LoadAsync(TestContext.Current.CancellationToken)).Value.Should().Be(10);
    }

    [Fact]
    public async Task ConcurrentInstances_CommitCompleteDocumentsUnderOneStoreLock()
    {
        using SettingsScope scope = new();
        using SettingsScope other = new(scope.Root);
        UserSettingsCommit[] commits = await Task.WhenAll(Enumerable.Range(1, 20)
            .Select(value => (value % 2 == 0 ? scope.Service : other.Service).SaveAsync(new() { Value = value })));
        await Task.WhenAll(commits.Select(commit => commit.NotificationCompletion));

        commits.Select(commit => commit.Revision).Order().Should().Equal(Enumerable.Range(1, 20).Select(value => (long)value));
        UserSettingsCommit last = commits.MaxBy(commit => commit.Revision)!;
        (await scope.Service.LoadAsync(TestContext.Current.CancellationToken)).Value.Should().Be(last.Settings.GetProperty("Value").GetInt32());
        Directory.GetFiles(scope.Root, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task StorageFailure_IsNotReportedAsMissingSettingsOrLockTimeout()
    {
        using SettingsScope scope = new();
        string file = Path.Combine(scope.Root, "not-a-directory");
        await File.WriteAllTextAsync(file, "existing file", TestContext.Current.CancellationToken);
        using SettingsScope invalidStore = new(file);

        await Assert.ThrowsAsync<IOException>(() => invalidStore.Service.LoadAsync(TestContext.Current.CancellationToken));
        (await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken)).Should().Be("existing file");
    }

    [Fact]
    public async Task LegacyImport_IsExplicitOneTimeMappingAndDoesNotChangeSource()
    {
        using SettingsScope scope = new();
        string legacy = Path.Combine(scope.Root, "legacy.config");
        await File.WriteAllTextAsync(legacy, "preserved legacy store", TestContext.Current.CancellationToken);
        int calls = 0;
        UserSettingsCommit? imported = await scope.Service.ImportAsync(() =>
        {
            calls++;
            return new SettingsModel { Value = 30 };
        }, TestContext.Current.CancellationToken);
        await imported!.NotificationCompletion;
        (await scope.Service.ImportAsync(() =>
        {
            calls++;
            return new SettingsModel { Value = 40 };
        }, TestContext.Current.CancellationToken)).Should().BeNull();

        calls.Should().Be(1);
        (await scope.Service.LoadAsync(TestContext.Current.CancellationToken)).Value.Should().Be(30);
        (await File.ReadAllTextAsync(legacy, TestContext.Current.CancellationToken)).Should().Be("preserved legacy store");
    }

    private static JsonElement Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    /// <summary>
    ///  Owns a provider and isolated store directory for a settings scenario.
    /// </summary>
    private sealed class SettingsScope : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly bool _ownsDirectory;
        internal string Root { get; }
        internal string Primary => Path.Combine(Root, "settings.json");
        internal string Backup => Primary + ".bak";
        internal IUserSettingsService<SettingsModel> Service { get; }

        internal SettingsScope(string? root = null, int version = 1, Action<JsonUserSettingsOptions<SettingsModel>>? configure = null)
        {
            _ownsDirectory = root is null;
            Root = root ?? Directory.CreateTempSubdirectory("WinFormsSettings-").FullName;
            _provider = new ServiceCollection()
                .AddJsonUserSettings("stable-product", version, SettingsJsonContext.Default.SettingsModel,
                    () => new SettingsModel { Value = 7 }, options =>
                    {
                        options.StorageDirectory = Root;
                        options.Validate = model =>
                        {
                            if (model.Value < 0)
                            {
                                throw new InvalidDataException("Value must be nonnegative.");
                            }
                        };
                        configure?.Invoke(options);
                    })
                .BuildServiceProvider();
            Service = _provider.GetRequiredService<IUserSettingsService<SettingsModel>>();
        }

        public void Dispose()
        {
            _provider.Dispose();
            if (_ownsDirectory)
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}

/// <summary>
///  Supplies a small settings model for typed persistence and migration tests.
/// </summary>
public sealed class SettingsModel
{
    public int Value { get; set; }
}

/// <summary>
///  Uses explicit source-generated metadata for settings without reflection-based serialization.
/// </summary>
[JsonSerializable(typeof(SettingsModel))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext
{
}
