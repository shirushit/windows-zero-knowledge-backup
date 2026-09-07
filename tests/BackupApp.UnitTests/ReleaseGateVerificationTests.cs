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

public class ReleaseGateVerificationTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _sourceDir;
    private readonly string _catalogDbPath;
    private readonly string _restoreCleanDir;

    public ReleaseGateVerificationTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"release_gate9_{Guid.NewGuid():N}");
        _sourceDir = Path.Combine(_testRoot, "source_pc");
        _catalogDbPath = Path.Combine(_testRoot, "catalog.sqlite");
        _restoreCleanDir = Path.Combine(_testRoot, "restored_clean_machine");

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
    public async Task Gate9_CompleteReleaseVerification_CleanInstall_Backup_Incremental_Interruption_DisasterRecovery()
    {
        // -----------------------------------------------------------------------------------------
        // 1. CLEAN INSTALLATION FOOTPRINT & KEY INITIALIZATION
        // -----------------------------------------------------------------------------------------
        var masterPassword = "Gate9-SuperSecure-Password-2026!";
        using var originalMasterKey = MasterKey.Generate();
        var passwordEnvelope = WrappedKeyEnvelope.Wrap(originalMasterKey, masterPassword);

        var recoveryService = new RecoveryKeyService();
        var recoveryPhrase = recoveryService.GenerateRecoveryKey(out var rawRecoverySecret);
        var recoveryEnvelope = recoveryService.WrapMasterKeyWithRecoverySecret(originalMasterKey, rawRecoverySecret);

        var storage = new InMemoryStorageProvider();
        var catalog = new SqliteCatalogRepository(_catalogDbPath);
        await catalog.InitializeAsync();

        var bsetId = BackupSetId.New();
        var backupSet = new BackupSet(bsetId, "Gate9 Release Set", [_sourceDir], [], DateTimeOffset.UtcNow);
        await catalog.SaveBackupSetAsync(backupSet);

        // -----------------------------------------------------------------------------------------
        // 2. FIRST BACKUP SUCCEEDS WITH FULL FIXTURE
        // -----------------------------------------------------------------------------------------
        var subDir = Path.Combine(_sourceDir, "תיקייה_עברית");
        Directory.CreateDirectory(subDir);

        var file1 = Path.Combine(_sourceDir, "document1.txt");
        var file2 = Path.Combine(subDir, "קובץ_בדיקה.bin");
        await File.WriteAllTextAsync(file1, "First Backup Content - Release Gate 9");

        var binaryPayload = new byte[65536]; // 64 KB
        RandomNumberGenerator.Fill(binaryPayload);
        await File.WriteAllBytesAsync(file2, binaryPayload);

        var fixedTimestamp = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        File.SetLastWriteTimeUtc(file1, fixedTimestamp.UtcDateTime);
        File.SetLastWriteTimeUtc(file2, fixedTimestamp.UtcDateTime);

        var backupOrchestrator = new BackupOrchestrator();
        var snap1Id = await backupOrchestrator.RunBackupAsync(bsetId, originalMasterKey, storage, catalog);
        snap1Id.Should().NotBeNull();

        var discovery = new RemoteCatalogDiscoveryService();
        var manifests1 = await discovery.DiscoverRemoteManifestsAsync(storage, originalMasterKey);
        manifests1.Should().ContainSingle();
        manifests1[0].SnapshotNumber.Should().Be(1);
        manifests1[0].TotalFiles.Should().Be(2);

        var chunkCountAfterFirstBackup = manifests1[0].Items.Sum(i => i.ChunkIds.Count);
        chunkCountAfterFirstBackup.Should().BeGreaterThan(0);

        // -----------------------------------------------------------------------------------------
        // 3. INCREMENTAL BACKUP SUCCEEDS WITHOUT REDUNDANT CHUNK UPLOADS
        // -----------------------------------------------------------------------------------------
        var file3 = Path.Combine(_sourceDir, "new_incremental.txt");
        await File.WriteAllTextAsync(file3, "New Incremental Content");

        var snap2Id = await backupOrchestrator.RunBackupAsync(bsetId, originalMasterKey, storage, catalog);
        snap2Id.Should().NotBeNull();

        var manifests2 = await discovery.DiscoverRemoteManifestsAsync(storage, originalMasterKey);
        manifests2.Should().HaveCount(2);
        manifests2[0].SnapshotNumber.Should().Be(2);
        manifests2[0].TotalFiles.Should().Be(3);

        // Deduplication verification: file1 and file2 chunks were reused in snapshot 2
        var snap2Item1 = manifests2[0].Items.First(i => i.Path.EndsWith("document1.txt", StringComparison.OrdinalIgnoreCase));
        var snap1Item1 = manifests1[0].Items.First(i => i.Path.EndsWith("document1.txt", StringComparison.OrdinalIgnoreCase));
        snap2Item1.ChunkIds.Should().BeEquivalentTo(snap1Item1.ChunkIds);

        // -----------------------------------------------------------------------------------------
        // 4. FORCED INTERRUPTION RESUMES SAFELY
        // -----------------------------------------------------------------------------------------
        var file4 = Path.Combine(_sourceDir, "interrupted_file.txt");
        await File.WriteAllTextAsync(file4, "Content before simulated interruption");

        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel(); // Cancel immediately
            var act = () => backupOrchestrator.RunBackupAsync(bsetId, originalMasterKey, storage, catalog, cancellationToken: cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        // Resume backup after interruption
        var snap3Id = await backupOrchestrator.RunBackupAsync(bsetId, originalMasterKey, storage, catalog);
        snap3Id.Should().NotBeNull();

        var manifests3 = await discovery.DiscoverRemoteManifestsAsync(storage, originalMasterKey);
        manifests3.Should().HaveCount(3);
        manifests3[0].SnapshotNumber.Should().Be(3);
        manifests3[0].TotalFiles.Should().Be(4);

        // -----------------------------------------------------------------------------------------
        // 5. CLEAN-MACHINE DISASTER RECOVERY (NO LOCAL CATALOG DB)
        // -----------------------------------------------------------------------------------------
        // Destroy local SQLite catalog completely
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_catalogDbPath);

        // Unlock MasterKey using ONLY recovery phrase
        var unlockService = new KeyUnlockService(recoveryService);
        using var recoveredKeyFromPhrase = unlockService.UnlockWithRecoveryKey(recoveryPhrase, recoveryEnvelope);
        recoveredKeyFromPhrase.Should().NotBeNull();

        // Remote manifest discovery on clean machine
        var cleanDiscovery = new RemoteCatalogDiscoveryService();
        var cleanManifests = await cleanDiscovery.DiscoverRemoteManifestsAsync(storage, recoveredKeyFromPhrase);
        cleanManifests.Should().HaveCount(3);

        var latestSnapshotManifest = cleanManifests[0];
        latestSnapshotManifest.SnapshotNumber.Should().Be(3);
        latestSnapshotManifest.TotalFiles.Should().Be(4);

        // Full restore into clean target directory
        var restoreEngine = new RestoreOrchestrator();
        var restoreOptions = new RestoreOptions(
            DestinationRootPath: _restoreCleanDir,
            ConflictResolution: RestoreConflictResolution.Overwrite,
            RestoreTimestamps: true
        );

        await restoreEngine.RestoreAllAsync(latestSnapshotManifest, recoveredKeyFromPhrase, storage, restoreOptions);

        // -----------------------------------------------------------------------------------------
        // 6. RESTORED HASHES MATCH SOURCE FIXTURE 100% BYTE-FOR-BYTE
        // -----------------------------------------------------------------------------------------
        foreach (var originalFile in new[] { file1, file2, file3, file4 })
        {
            var relativePath = Path.GetRelativePath(_sourceDir, originalFile);
            var restoredFile = Path.Combine(_restoreCleanDir, relativePath);

            File.Exists(restoredFile).Should().BeTrue($"Restored file '{relativePath}' must exist.");

            var originalBytes = await File.ReadAllBytesAsync(originalFile);
            var restoredBytes = await File.ReadAllBytesAsync(restoredFile);

            var originalHash = Convert.ToHexString(SHA256.HashData(originalBytes)).ToLowerInvariant();
            var restoredHash = Convert.ToHexString(SHA256.HashData(restoredBytes)).ToLowerInvariant();

            restoredHash.Should().Be(originalHash, $"Hash of '{relativePath}' must match original byte-for-byte.");

            // -------------------------------------------------------------------------------------
            // 7. TIMESTAMPS AND METADATA MATCH EXPECTED VALUES
            // -------------------------------------------------------------------------------------
            var originalWriteTime = File.GetLastWriteTimeUtc(originalFile);
            var restoredWriteTime = File.GetLastWriteTimeUtc(restoredFile);
            Math.Abs((restoredWriteTime - originalWriteTime).TotalSeconds).Should().BeLessThan(2, $"Timestamp of '{relativePath}' must be preserved.");
        }
    }

    [Fact]
    public async Task Gate9_CiphertextTampering_Poly1305MACFailureDetected_RestorationFailsSafely()
    {
        var fileData = Encoding.UTF8.GetBytes("Critical payload to be tampered");
        var filePath = Path.Combine(_sourceDir, "tamper_victim.txt");
        await File.WriteAllBytesAsync(filePath, fileData);

        var storage = new InMemoryStorageProvider();
        var catalog = new SqliteCatalogRepository(_catalogDbPath);
        await catalog.InitializeAsync();

        using var masterKey = MasterKey.Generate();
        var bsetId = BackupSetId.New();
        var backupSet = new BackupSet(bsetId, "TamperSet", [_sourceDir], [], DateTimeOffset.UtcNow);
        await catalog.SaveBackupSetAsync(backupSet);

        var backupEngine = new BackupOrchestrator();
        await backupEngine.RunBackupAsync(bsetId, masterKey, storage, catalog);

        var discovery = new RemoteCatalogDiscoveryService();
        var manifests = await discovery.DiscoverRemoteManifestsAsync(storage, masterKey);
        manifests.Should().ContainSingle();

        // Corrupt chunk on remote storage
        var chunkId = ObjectId.FromHex(manifests[0].Items[0].ChunkIds[0]);
        var originalChunkBytes = await (await storage.GetObjectAsync(chunkId)).ReadAllBytesAsync();
        originalChunkBytes[^3] ^= 0xAA; // Corrupt Poly1305 MAC tag or ciphertext

        using (var tamperedStream = new MemoryStream(originalChunkBytes))
        {
            await storage.PutObjectAsync(chunkId, tamperedStream);
        }

        // Attempt restore into clean directory
        var cleanTarget = Path.Combine(_testRoot, "tamper_clean_target");
        var restoreEngine = new RestoreOrchestrator();
        var restoreOptions = new RestoreOptions(cleanTarget);

        var act = () => restoreEngine.RestoreAllAsync(manifests[0], masterKey, storage, restoreOptions);
        await act.Should().ThrowAsync<CryptographicException>();

        // Ensure no partial or corrupt file was written to disk
        var targetFile = Path.Combine(cleanTarget, "tamper_victim.txt");
        File.Exists(targetFile).Should().BeFalse();
    }

    [Fact]
    public void Gate9_WrongPassword_FailsSafelyWithoutStateLeak()
    {
        using var masterKey = MasterKey.Generate();
        var envelope = WrappedKeyEnvelope.Wrap(masterKey, "CorrectPassword123!");

        var unlockService = new KeyUnlockService();

        // Correct password succeeds
        using var unlocked = unlockService.UnlockWithPassword(envelope, "CorrectPassword123!");
        unlocked.Should().NotBeNull();

        // Wrong password fails safely with CryptographicException
        var act = () => unlockService.UnlockWithPassword(envelope, "WrongPassword999!");
        act.Should().Throw<CryptographicException>();
    }
}
