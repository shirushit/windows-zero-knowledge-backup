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

public class HardeningTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _sourceDir;
    private readonly string _restoreDir;
    private readonly string _catalogDbPath;

    public HardeningTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"hardening_tests_{Guid.NewGuid():N}");
        _sourceDir = Path.Combine(_testRoot, "source");
        _restoreDir = Path.Combine(_testRoot, "restore");
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
    public async Task Hardening_UnicodeHebrewAndDeepNesting_ShouldBackupAndRestoreAccurately()
    {
        // Deep nesting with Hebrew, spaces, numbers, and special characters
        var deepDir = Path.Combine(_sourceDir, "רמה 1 - תיקייה ראשית", "רמה 2 - תת תיקייה", "2026 דוחות ונתונים 📊");
        Directory.CreateDirectory(deepDir);

        var complexFileName = "דוח סודי (2026) #1 - גרסה סופית!.txt";
        var fullPath = Path.Combine(deepDir, complexFileName);
        var content = "מידע מאובטח ברמת סיווג גבוהה 🔒 - בדיקת שחזור מחמירה.";
        await File.WriteAllTextAsync(fullPath, content, Encoding.UTF8);

        var originalHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

        var storage = new InMemoryStorageProvider();
        var catalog = new SqliteCatalogRepository(_catalogDbPath);
        await catalog.InitializeAsync();

        using var masterKey = MasterKey.Generate();
        var bsetId = BackupSetId.New();
        var backupSet = new BackupSet(bsetId, "DeepHebrewSet", [_sourceDir], [], DateTimeOffset.UtcNow);
        await catalog.SaveBackupSetAsync(backupSet);

        var backupEngine = new BackupOrchestrator();
        var snapId = await backupEngine.RunBackupAsync(bsetId, masterKey, storage, catalog);
        snapId.Should().NotBeNull();

        // Discover and restore
        var discovery = new RemoteCatalogDiscoveryService();
        var manifests = await discovery.DiscoverRemoteManifestsAsync(storage, masterKey);
        manifests.Should().ContainSingle();

        var restoreEngine = new RestoreOrchestrator();
        var options = new RestoreOptions(_restoreDir, RestoreConflictResolution.Overwrite, RestoreTimestamps: true);
        await restoreEngine.RestoreAllAsync(manifests[0], masterKey, storage, options);

        var restoredPath = Path.Combine(_restoreDir, "רמה 1 - תיקייה ראשית", "רמה 2 - תת תיקייה", "2026 דוחות ונתונים 📊", complexFileName);
        File.Exists(restoredPath).Should().BeTrue();

        var restoredContent = await File.ReadAllTextAsync(restoredPath, Encoding.UTF8);
        restoredContent.Should().Be(content);

        var restoredHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(restoredContent))).ToLowerInvariant();
        restoredHash.Should().Be(originalHash);
    }

    [Fact]
    public async Task Hardening_MultiChunkLargeFile_ShouldPartitionAndReassembleByteForByte()
    {
        // 512KB payload with custom chunk size 128KB (4 chunks)
        const int totalSize = 512 * 1024;
        const int chunkSize = 128 * 1024;
        var largeData = RandomNumberGenerator.GetBytes(totalSize);

        var largeFilePath = Path.Combine(_sourceDir, "large_binary.dat");
        await File.WriteAllBytesAsync(largeFilePath, largeData);
        var originalHash = Convert.ToHexString(SHA256.HashData(largeData)).ToLowerInvariant();

        var storage = new InMemoryStorageProvider();
        var catalog = new SqliteCatalogRepository(_catalogDbPath);
        await catalog.InitializeAsync();

        using var masterKey = MasterKey.Generate();
        var bsetId = BackupSetId.New();
        var backupSet = new BackupSet(bsetId, "LargeChunkSet", [_sourceDir], [], DateTimeOffset.UtcNow);
        await catalog.SaveBackupSetAsync(backupSet);

        var options = new BackupEngineOptions { ChunkSizeBytes = chunkSize };
        var backupEngine = new BackupOrchestrator();
        await backupEngine.RunBackupAsync(bsetId, masterKey, storage, catalog, options);

        // Verify manifest contains 4 chunk IDs
        var discovery = new RemoteCatalogDiscoveryService();
        var manifests = await discovery.DiscoverRemoteManifestsAsync(storage, masterKey);
        manifests.Should().ContainSingle();

        var manifestItem = manifests[0].Items.First(i => i.Path.Contains("large_binary.dat"));
        manifestItem.ChunkIds.Should().HaveCount(4);
        manifestItem.SizeBytes.Should().Be(totalSize);

        // Restore and verify
        var restoreEngine = new RestoreOrchestrator();
        var restoreOptions = new RestoreOptions(_restoreDir);
        await restoreEngine.RestoreAllAsync(manifests[0], masterKey, storage, restoreOptions);

        var restoredFile = Path.Combine(_restoreDir, "large_binary.dat");
        File.Exists(restoredFile).Should().BeTrue();

        var restoredBytes = await File.ReadAllBytesAsync(restoredFile);
        restoredBytes.Length.Should().Be(totalSize);

        var restoredHash = Convert.ToHexString(SHA256.HashData(restoredBytes)).ToLowerInvariant();
        restoredHash.Should().Be(originalHash);
    }

    [Fact]
    public async Task Hardening_CorruptedRemoteObject_ShouldFailIntegrityVerificationFatally()
    {
        var fileData = Encoding.UTF8.GetBytes("Data that will be corrupted remotely.");
        var filePath = Path.Combine(_sourceDir, "victim.txt");
        await File.WriteAllBytesAsync(filePath, fileData);

        var storage = new InMemoryStorageProvider();
        var catalog = new SqliteCatalogRepository(_catalogDbPath);
        await catalog.InitializeAsync();

        using var masterKey = MasterKey.Generate();
        var bsetId = BackupSetId.New();
        var backupSet = new BackupSet(bsetId, "CorruptSet", [_sourceDir], [], DateTimeOffset.UtcNow);
        await catalog.SaveBackupSetAsync(backupSet);

        var backupEngine = new BackupOrchestrator();
        await backupEngine.RunBackupAsync(bsetId, masterKey, storage, catalog);

        var discovery = new RemoteCatalogDiscoveryService();
        var manifests = await discovery.DiscoverRemoteManifestsAsync(storage, masterKey);
        manifests.Should().ContainSingle();

        // Corrupt the chunk on remote storage: tamper 1 byte
        var chunkId = ObjectId.FromHex(manifests[0].Items[0].ChunkIds[0]);
        var originalChunkBytes = await (await storage.GetObjectAsync(chunkId)).ReadAllBytesAsync();
        originalChunkBytes[^5] ^= 0xFF; // Flip bits in Poly1305 tag/ciphertext

        // Overwrite corrupted chunk in storage
        using (var tamperedStream = new MemoryStream(originalChunkBytes))
        {
            await storage.PutObjectAsync(chunkId, tamperedStream);
        }

        // Attempt restore: must fail fatally with CryptographicException
        var restoreEngine = new RestoreOrchestrator();
        var restoreOptions = new RestoreOptions(_restoreDir);

        var act = () => restoreEngine.RestoreAllAsync(manifests[0], masterKey, storage, restoreOptions);
        await act.Should().ThrowAsync<CryptographicException>();

        // Ensure no corrupt file was left at destination
        var targetFile = Path.Combine(_restoreDir, "victim.txt");
        File.Exists(targetFile).Should().BeFalse();
    }
}

internal static class StreamExtensions
{
    public static async Task<byte[]> ReadAllBytesAsync(this Stream stream)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        return ms.ToArray();
    }
}
