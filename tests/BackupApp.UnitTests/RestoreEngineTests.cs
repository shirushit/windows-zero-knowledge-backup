using System.Security;
using System.Security.Cryptography;
using System.Text;
using BackupApp.Crypto;
using BackupApp.Domain;
using BackupApp.RestoreEngine;
using BackupApp.Storage;
using FluentAssertions;
using Xunit;

namespace BackupApp.UnitTests;

public class RestoreEngineTests : IDisposable
{
    private readonly string _testRoot;

    public RestoreEngineTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"restore_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
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
                // Best effort test cleanup
            }
        }
    }

    [Fact]
    public void KeyUnlockService_UnlockWithPassword_ShouldSucceed()
    {
        using var master = MasterKey.Generate();
        const string password = "SecretMasterPassword123!";
        var envelope = WrappedKeyEnvelope.Wrap(master, password);

        var unlockService = new KeyUnlockService();
        using var unlocked = unlockService.UnlockWithPassword(envelope, password);

        unlocked.ExportRawKey().Should().BeEquivalentTo(master.ExportRawKey());
    }

    [Fact]
    public void KeyUnlockService_UnlockWithRecoveryKey_ShouldSucceed()
    {
        var recoveryService = new RecoveryKeyService();
        using var master = MasterKey.Generate();

        var recoveryPhrase = recoveryService.GenerateRecoveryKey(out var rawSecret);
        var envelope = recoveryService.WrapMasterKeyWithRecoverySecret(master, rawSecret);

        var unlockService = new KeyUnlockService(recoveryService);
        using var unlocked = unlockService.UnlockWithRecoveryKey(recoveryPhrase, envelope);

        unlocked.ExportRawKey().Should().BeEquivalentTo(master.ExportRawKey());
    }

    [Fact]
    public void KeyUnlockService_WrongPassword_ShouldThrowCryptographicException()
    {
        using var master = MasterKey.Generate();
        var envelope = WrappedKeyEnvelope.Wrap(master, "PasswordA");

        var unlockService = new KeyUnlockService();
        var act = () => unlockService.UnlockWithPassword(envelope, "PasswordB");

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void RestoreTreeBrowser_HierarchyNavigationAndSearch_ShouldWorkAccurately()
    {
        var manifest = new SnapshotManifest(
            BackupSetId: "bset1",
            SnapshotId: "snap1",
            SnapshotNumber: 1,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            TotalFiles: 4,
            TotalBytes: 400,
            Items:
            [
                new SnapshotManifestItem("root.txt", 100, "hash1", 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, ["c1"]),
                new SnapshotManifestItem("docs/report.pdf", 100, "hash2", 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, ["c2"]),
                new SnapshotManifestItem("docs/notes.txt", 100, "hash3", 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, ["c3"]),
                new SnapshotManifestItem("photos/2026/trip.jpg", 100, "hash4", 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, ["c4"])
            ]
        );

        var browser = new RestoreTreeBrowser(manifest);

        // Root files
        var rootFiles = browser.GetFiles();
        rootFiles.Should().ContainSingle().Which.Path.Should().Be("root.txt");

        // Root directories
        var rootDirs = browser.GetDirectories();
        rootDirs.Should().BeEquivalentTo(["docs", "photos"]);

        // Subdirectory files
        var docFiles = browser.GetFiles("docs");
        docFiles.Should().HaveCount(2);

        // Search
        var txtFiles = browser.SearchFiles(".txt");
        txtFiles.Should().HaveCount(2);
        txtFiles.Select(f => f.Path).Should().Contain(["root.txt", "docs/notes.txt"]);
    }

    [Fact]
    public void RestoreOrchestrator_PathTraversal_ShouldBeFatallyRejected()
    {
        var act = () => RestoreOrchestrator.ValidateAndResolveTargetPath(_testRoot, "../../../escape.txt");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RestoreOrchestrator_ReservedWindowsDevice_ShouldBeFatallyRejected()
    {
        var act = () => RestoreOrchestrator.ValidateAndResolveTargetPath(_testRoot, "sub/CON.txt");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task RestoreOrchestrator_RestoreSingleFile_ShouldRestoreExactBytesAndTimestamps()
    {
        using var master = MasterKey.Generate();
        var contentKey = master.DeriveContentKey();
        var crypto = new CryptoService();
        var storage = new InMemoryStorageProvider();

        var originalContent = Encoding.UTF8.GetBytes("Critical system document payload 2026.");
        var chunkId = ObjectId.FromHex("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var aad = Encoding.UTF8.GetBytes($"chunk:{chunkId.Value}");
        var envelope = crypto.Encrypt(originalContent, contentKey, aad);

        using (var stream = new MemoryStream(envelope.ToBytes()))
        {
            await storage.PutObjectAsync(chunkId, stream);
        }

        var contentHash = Convert.ToHexString(SHA256.HashData(originalContent)).ToLowerInvariant();
        var created = DateTimeOffset.UtcNow.AddHours(-2);
        var modified = DateTimeOffset.UtcNow.AddHours(-1);

        var manifest = new SnapshotManifest(
            BackupSetId: "bset1",
            SnapshotId: "snap1",
            SnapshotNumber: 1,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            TotalFiles: 1,
            TotalBytes: originalContent.Length,
            Items:
            [
                new SnapshotManifestItem("sub/doc.txt", originalContent.Length, contentHash, 0, created, modified, [chunkId.Value])
            ]
        );

        var orchestrator = new RestoreOrchestrator(crypto);
        var destDir = Path.Combine(_testRoot, "restored");

        var options = new RestoreOptions(destDir, RestoreConflictResolution.Overwrite, RestoreTimestamps: true);
        await orchestrator.RestoreFileAsync(manifest, "sub/doc.txt", master, storage, options);

        var targetFile = Path.Combine(destDir, "sub", "doc.txt");
        File.Exists(targetFile).Should().BeTrue();

        var restoredBytes = await File.ReadAllBytesAsync(targetFile);
        restoredBytes.Should().BeEquivalentTo(originalContent);

        var restoredInfo = new FileInfo(targetFile);
        restoredInfo.CreationTimeUtc.Should().BeCloseTo(created.UtcDateTime, TimeSpan.FromSeconds(2));
        restoredInfo.LastWriteTimeUtc.Should().BeCloseTo(modified.UtcDateTime, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task RestoreOrchestrator_ConflictResolution_Skip_ShouldPreserveExistingFile()
    {
        using var master = MasterKey.Generate();
        var contentKey = master.DeriveContentKey();
        var crypto = new CryptoService();
        var storage = new InMemoryStorageProvider();

        var originalContent = Encoding.UTF8.GetBytes("Remote original content");
        var chunkId = ObjectId.FromHex("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
        var envelope = crypto.Encrypt(originalContent, contentKey, Encoding.UTF8.GetBytes($"chunk:{chunkId.Value}"));

        using (var stream = new MemoryStream(envelope.ToBytes()))
        {
            await storage.PutObjectAsync(chunkId, stream);
        }

        var contentHash = Convert.ToHexString(SHA256.HashData(originalContent)).ToLowerInvariant();
        var manifest = new SnapshotManifest(
            BackupSetId: "bset1",
            SnapshotId: "snap1",
            SnapshotNumber: 1,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            TotalFiles: 1,
            TotalBytes: originalContent.Length,
            Items: [new SnapshotManifestItem("file.txt", originalContent.Length, contentHash, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, [chunkId.Value])]
        );

        var destDir = Path.Combine(_testRoot, "dest_skip");
        Directory.CreateDirectory(destDir);
        var existingFile = Path.Combine(destDir, "file.txt");
        await File.WriteAllTextAsync(existingFile, "Local pre-existing content");

        var orchestrator = new RestoreOrchestrator(crypto);
        var options = new RestoreOptions(destDir, RestoreConflictResolution.Skip);

        await orchestrator.RestoreFileAsync(manifest, "file.txt", master, storage, options);

        var contentAfter = await File.ReadAllTextAsync(existingFile);
        contentAfter.Should().Be("Local pre-existing content");
    }

    [Fact]
    public async Task RestoreOrchestrator_TamperedChunk_ShouldFailIntegrityVerificationAndCleanTempFile()
    {
        using var master = MasterKey.Generate();
        var contentKey = master.DeriveContentKey();
        var crypto = new CryptoService();
        var storage = new InMemoryStorageProvider();

        var originalContent = Encoding.UTF8.GetBytes("Legitimate payload data");
        var chunkId = ObjectId.FromHex("cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc");
        var envelope = crypto.Encrypt(originalContent, contentKey, Encoding.UTF8.GetBytes($"chunk:{chunkId.Value}"));

        using (var stream = new MemoryStream(envelope.ToBytes()))
        {
            await storage.PutObjectAsync(chunkId, stream);
        }

        // Expected hash is intentionally different to simulate corrupted data / hash forgery
        const string wrongHash = "0000000000000000000000000000000000000000000000000000000000000000";
        var manifest = new SnapshotManifest(
            BackupSetId: "bset1",
            SnapshotId: "snap1",
            SnapshotNumber: 1,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            TotalFiles: 1,
            TotalBytes: originalContent.Length,
            Items: [new SnapshotManifestItem("corrupt.txt", originalContent.Length, wrongHash, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, [chunkId.Value])]
        );

        var destDir = Path.Combine(_testRoot, "dest_corrupt");
        var orchestrator = new RestoreOrchestrator(crypto);
        var options = new RestoreOptions(destDir, RestoreConflictResolution.Overwrite);

        var act = () => orchestrator.RestoreFileAsync(manifest, "corrupt.txt", master, storage, options);
        await act.Should().ThrowAsync<CryptographicException>().WithMessage("*Integrity verification failed*");

        var targetFile = Path.Combine(destDir, "corrupt.txt");
        File.Exists(targetFile).Should().BeFalse();

        // Ensure no leftover temp files
        if (Directory.Exists(destDir))
        {
            Directory.GetFiles(destDir, "*.tmp*").Should().BeEmpty();
        }
    }
}
