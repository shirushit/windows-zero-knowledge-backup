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

public class Gate5VerificationTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _sourceDir;
    private readonly string _catalogDbPath;
    private readonly string _restoreCleanDir;

    public Gate5VerificationTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"gate5_dr_{Guid.NewGuid():N}");
        _sourceDir = Path.Combine(_testRoot, "source_pc");
        _catalogDbPath = Path.Combine(_testRoot, "catalog.sqlite");
        _restoreCleanDir = Path.Combine(_testRoot, "clean_machine_restored");

        Directory.CreateDirectory(_sourceDir);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
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
    public async Task Gate5_CleanEnvironmentDisasterRecovery_UsingOnlyRecoveryMaterial_ShouldRestoreFixtureByteForByte()
    {
        // -------------------------------------------------------------
        // STEP 1: POPULATE RICH FIXTURE ON ORIGINAL USER MACHINE
        // -------------------------------------------------------------
        var subDirHebrew = Path.Combine(_sourceDir, "מסמכים וחשבונות");
        var subDirPhotos = Path.Combine(_sourceDir, "Photos", "2026");
        Directory.CreateDirectory(subDirHebrew);
        Directory.CreateDirectory(subDirPhotos);

        var file1Path = Path.Combine(_sourceDir, "readme.txt");
        var file2Path = Path.Combine(subDirHebrew, "דו_ח כספי שנתי 2026.txt");
        var file3Path = Path.Combine(subDirPhotos, "binary_data.bin");

        var file1Data = Encoding.UTF8.GetBytes("System Documentation and Notes");
        var file2Data = Encoding.UTF8.GetBytes("מידע כספי רגיש ודוחות מס לשנת 2026 - סודי ביותר");
        var file3Data = RandomNumberGenerator.GetBytes(1024 * 64); // 64KB random binary data

        await File.WriteAllBytesAsync(file1Path, file1Data);
        await File.WriteAllBytesAsync(file2Path, file2Data);
        await File.WriteAllBytesAsync(file3Path, file3Data);

        var file1Hash = Convert.ToHexString(SHA256.HashData(file1Data)).ToLowerInvariant();
        var file2Hash = Convert.ToHexString(SHA256.HashData(file2Data)).ToLowerInvariant();
        var file3Hash = Convert.ToHexString(SHA256.HashData(file3Data)).ToLowerInvariant();

        var fixedDate = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
        File.SetCreationTimeUtc(file1Path, fixedDate.UtcDateTime);
        File.SetLastWriteTimeUtc(file1Path, fixedDate.UtcDateTime);

        // -------------------------------------------------------------
        // STEP 2: SETUP BACKUP & EXECUTE TO REMOTE STORAGE
        // -------------------------------------------------------------
        var storage = new InMemoryStorageProvider();
        var catalog = new SqliteCatalogRepository(_catalogDbPath);
        await catalog.InitializeAsync();

        using var originalMasterKey = MasterKey.Generate();
        var recoveryService = new RecoveryKeyService();
        var recoveryPhrase = recoveryService.GenerateRecoveryKey(out var rawRecoverySecret);
        var recoveryEnvelope = recoveryService.WrapMasterKeyWithRecoverySecret(originalMasterKey, rawRecoverySecret);

        var bsetId = BackupSetId.New();
        var backupSet = new BackupSet(bsetId, "DisasterRecoverySet", [_sourceDir], [], DateTimeOffset.UtcNow);
        await catalog.SaveBackupSetAsync(backupSet);

        var backupOrchestrator = new BackupOrchestrator();
        var snapshotId = await backupOrchestrator.RunBackupAsync(bsetId, originalMasterKey, storage, catalog);

        snapshotId.Should().NotBeNull();

        // -------------------------------------------------------------
        // STEP 3: SIMULATE COMPLETE DISASTER
        // - PC was destroyed / hard drive failed
        // - Local SQLite catalog database is DELETED
        // - In-memory MasterKey is completely disposed / forgotten
        // - The user only has:
        //     1. Remote Storage Account (storage provider with remote chunks & anchors)
        //     2. Recovery Key Phrase (written down on paper)
        //     3. Recovery Envelope
        // -------------------------------------------------------------
        // Clear SQLite connection pool so file lock is released
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        if (File.Exists(_catalogDbPath))
        {
            File.Delete(_catalogDbPath);
        }

        // -------------------------------------------------------------
        // STEP 4: CLEAN MACHINE DISASTER RECOVERY
        // -------------------------------------------------------------
        // 4.1 Unlock MasterKey using ONLY recovery material
        var unlockService = new KeyUnlockService(recoveryService);
        using var recoveredMasterKey = unlockService.UnlockWithRecoveryKey(recoveryPhrase, recoveryEnvelope);
        recoveredMasterKey.Should().NotBeNull();

        // 4.2 Discover remote manifests directly from remote storage provider
        var discoveryService = new RemoteCatalogDiscoveryService();
        var manifests = await discoveryService.DiscoverRemoteManifestsAsync(storage, recoveredMasterKey);

        manifests.Should().NotBeEmpty("Disaster recovery must discover remote snapshot manifest anchors");
        var latestManifest = manifests[0];
        latestManifest.TotalFiles.Should().Be(3);
        latestManifest.SnapshotNumber.Should().Be(1);

        // 4.3 Full Restore of entire snapshot into clean directory
        var restoreOrchestrator = new RestoreOrchestrator();
        var restoreOptions = new RestoreOptions(
            DestinationRootPath: _restoreCleanDir,
            ConflictResolution: RestoreConflictResolution.Overwrite,
            RestoreTimestamps: true
        );

        int progressReportCount = 0;
        var progress = new SyncProgress<RestoreProgressReport>(_ => progressReportCount++);

        await restoreOrchestrator.RestoreAllAsync(
            latestManifest,
            recoveredMasterKey,
            storage,
            restoreOptions,
            progress
        );

        progressReportCount.Should().Be(3);

        // -------------------------------------------------------------
        // STEP 5: VERIFY INTEGRITY, HASHES, AND METADATA
        // -------------------------------------------------------------
        var restoredFile1 = Path.Combine(_restoreCleanDir, "readme.txt");
        var restoredFile2 = Path.Combine(_restoreCleanDir, "מסמכים וחשבונות", "דו_ח כספי שנתי 2026.txt");
        var restoredFile3 = Path.Combine(_restoreCleanDir, "Photos", "2026", "binary_data.bin");

        File.Exists(restoredFile1).Should().BeTrue("Restored readme.txt must exist");
        File.Exists(restoredFile2).Should().BeTrue("Restored Hebrew file must exist");
        File.Exists(restoredFile3).Should().BeTrue("Restored binary file must exist");

        // Verify SHA-256 hashes match byte-for-byte
        var restored1Bytes = await File.ReadAllBytesAsync(restoredFile1);
        var restored2Bytes = await File.ReadAllBytesAsync(restoredFile2);
        var restored3Bytes = await File.ReadAllBytesAsync(restoredFile3);

        Convert.ToHexString(SHA256.HashData(restored1Bytes)).ToLowerInvariant().Should().Be(file1Hash);
        Convert.ToHexString(SHA256.HashData(restored2Bytes)).ToLowerInvariant().Should().Be(file2Hash);
        Convert.ToHexString(SHA256.HashData(restored3Bytes)).ToLowerInvariant().Should().Be(file3Hash);

        // Verify timestamps
        var info1 = new FileInfo(restoredFile1);
        info1.LastWriteTimeUtc.Should().BeCloseTo(fixedDate.UtcDateTime, TimeSpan.FromSeconds(2));

        // -------------------------------------------------------------
        // STEP 6: NEGATIVE SECURITY CHECK
        // - Tampered recovery key must fail safely
        // -------------------------------------------------------------
        var tamperedChars = recoveryPhrase.ToCharArray();
        tamperedChars[0] = tamperedChars[0] == 'A' ? 'B' : 'A';
        var tamperedKey = new string(tamperedChars);

        var actTampered = () => unlockService.UnlockWithRecoveryKey(tamperedKey, recoveryEnvelope);
        actTampered.Should().Throw<ArgumentException>();
    }

    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
