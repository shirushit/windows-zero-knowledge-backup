using System.Security.Cryptography;
using System.Text;
using BackupApp.BackupEngine;
using BackupApp.Catalog;
using BackupApp.Crypto;
using BackupApp.Domain;
using BackupApp.RestoreEngine;
using BackupApp.Storage;
using FluentAssertions;
using Xunit;

namespace BackupApp.UnitTests;

public class Gate7VerificationTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _sourceDir;
    private readonly string _catalogDbPath;

    public Gate7VerificationTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"gate7_hardening_{Guid.NewGuid():N}");
        _sourceDir = Path.Combine(_testRoot, "source");
        _catalogDbPath = Path.Combine(_testRoot, "catalog.sqlite");

        Directory.CreateDirectory(_sourceDir);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_testRoot))
        {
            try
            {
                Directory.Delete(_testRoot, recursive: true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    [Fact]
    public async Task Gate7_RepeatedRecoverySuite_ZeroDefects_MultiCycleDisasterRecoverySucceedsDeterministically()
    {
        // ------------------------------------------------------------------
        // CYCLE 1: INITIAL BACKUP AND RESTORE
        // ------------------------------------------------------------------
        var fileA = Path.Combine(_sourceDir, "fileA.txt");
        var fileB = Path.Combine(_sourceDir, "קובץ_בעברית.txt");
        await File.WriteAllTextAsync(fileA, "Content of File A - Cycle 1");
        await File.WriteAllTextAsync(fileB, "תוכן עברי מקורי - מחזור 1");

        var storage = new InMemoryStorageProvider();
        var catalog = new SqliteCatalogRepository(_catalogDbPath);
        await catalog.InitializeAsync();

        using var masterKey = MasterKey.Generate();
        var bsetId = BackupSetId.New();
        var backupSet = new BackupSet(bsetId, "HardeningSet", [_sourceDir], [], DateTimeOffset.UtcNow);
        await catalog.SaveBackupSetAsync(backupSet);

        var backupEngine = new BackupOrchestrator();
        var snap1Id = await backupEngine.RunBackupAsync(bsetId, masterKey, storage, catalog);
        snap1Id.Should().NotBeNull();

        // Restore Cycle 1 into Clean Target 1
        var restoreDir1 = Path.Combine(_testRoot, "restore_cycle_1");
        var discovery = new RemoteCatalogDiscoveryService();
        var manifests1 = await discovery.DiscoverRemoteManifestsAsync(storage, masterKey);
        manifests1.Should().ContainSingle();

        var restoreEngine = new RestoreOrchestrator();
        await restoreEngine.RestoreAllAsync(manifests1[0], masterKey, storage, new RestoreOptions(restoreDir1));

        (await File.ReadAllTextAsync(Path.Combine(restoreDir1, "fileA.txt"))).Should().Be("Content of File A - Cycle 1");
        (await File.ReadAllTextAsync(Path.Combine(restoreDir1, "קובץ_בעברית.txt"))).Should().Be("תוכן עברי מקורי - מחזור 1");

        // ------------------------------------------------------------------
        // CYCLE 2: INCREMENTAL MUTATION AND RESTORE
        // ------------------------------------------------------------------
        await Task.Delay(50);
        await File.WriteAllTextAsync(fileA, "Content of File A - MODIFIED Cycle 2");
        var fileC = Path.Combine(_sourceDir, "new_file_c.bin");
        var binData = RandomNumberGenerator.GetBytes(1024 * 32);
        await File.WriteAllBytesAsync(fileC, binData);

        var snap2Id = await backupEngine.RunBackupAsync(bsetId, masterKey, storage, catalog);
        snap2Id.Should().NotBeNull();

        var restoreDir2 = Path.Combine(_testRoot, "restore_cycle_2");
        var manifests2 = await discovery.DiscoverRemoteManifestsAsync(storage, masterKey);
        manifests2.Should().HaveCount(2);
        var latestManifest = manifests2[0]; // Ordered by SnapshotNumber descending

        await restoreEngine.RestoreAllAsync(latestManifest, masterKey, storage, new RestoreOptions(restoreDir2));

        (await File.ReadAllTextAsync(Path.Combine(restoreDir2, "fileA.txt"))).Should().Be("Content of File A - MODIFIED Cycle 2");
        (await File.ReadAllTextAsync(Path.Combine(restoreDir2, "קובץ_בעברית.txt"))).Should().Be("תוכן עברי מקורי - מחזור 1");
        (await File.ReadAllBytesAsync(Path.Combine(restoreDir2, "new_file_c.bin"))).Should().BeEquivalentTo(binData);

        // ------------------------------------------------------------------
        // CYCLE 3: COMPLETE DISASTER RECOVERY VIA RECOVERY PHRASE ONLY
        // ------------------------------------------------------------------
        var recoveryService = new RecoveryKeyService();
        var recoveryPhrase = recoveryService.GenerateRecoveryKey(out var rawSecret);
        var recoveryEnvelope = recoveryService.WrapMasterKeyWithRecoverySecret(masterKey, rawSecret);

        // Destroy local SQLite DB completely
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_catalogDbPath))
        {
            File.Delete(_catalogDbPath);
        }

        // Clean-room recovery using only recovery phrase and envelope
        var unlockService = new KeyUnlockService(recoveryService);
        using var unlockedMasterKey = unlockService.UnlockWithRecoveryKey(recoveryPhrase, recoveryEnvelope);

        var manifestsClean = await discovery.DiscoverRemoteManifestsAsync(storage, unlockedMasterKey);
        manifestsClean.Should().HaveCount(2);

        var restoreDir3 = Path.Combine(_testRoot, "restore_cycle_3_clean_room");
        await restoreEngine.RestoreAllAsync(manifestsClean[0], unlockedMasterKey, storage, new RestoreOptions(restoreDir3));

        (await File.ReadAllTextAsync(Path.Combine(restoreDir3, "fileA.txt"))).Should().Be("Content of File A - MODIFIED Cycle 2");
        (await File.ReadAllTextAsync(Path.Combine(restoreDir3, "קובץ_בעברית.txt"))).Should().Be("תוכן עברי מקורי - מחזור 1");
        (await File.ReadAllBytesAsync(Path.Combine(restoreDir3, "new_file_c.bin"))).Should().BeEquivalentTo(binData);
    }

    [Fact]
    public void Gate7_SecretAndPrivacyAudit_NoPlaintextTokensOrKeysInStorageDescriptors()
    {
        const string secretToken = "123456789:AAE-SecretTelegramBotToken999";
        var config = new BackupApp.Storage.Telegram.TelegramStorageConfiguration(secretToken, "-10011223344");

        var serialized = config.ToString();
        serialized.Should().NotContain("SecretTelegramBotToken999");
        serialized.Should().Contain("1234...n999");
    }
}
