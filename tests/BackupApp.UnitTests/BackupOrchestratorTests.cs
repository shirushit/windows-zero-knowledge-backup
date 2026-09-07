using BackupApp.BackupEngine;
using BackupApp.Catalog;
using BackupApp.Crypto;
using BackupApp.Domain;
using BackupApp.Storage;
using FluentAssertions;
using Xunit;

namespace BackupApp.UnitTests;

public class BackupOrchestratorTests : IAsyncDisposable
{
    private readonly string _testRoot;
    private readonly string _dbPath;
    private readonly SqliteCatalogRepository _catalog;
    private readonly InMemoryStorageProvider _storage;
    private readonly BackupOrchestrator _orchestrator;

    public BackupOrchestratorTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"orch_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);

        _dbPath = Path.Combine(Path.GetTempPath(), $"orch_db_{Guid.NewGuid():N}.db");
        _catalog = new SqliteCatalogRepository(_dbPath);
        _storage = new InMemoryStorageProvider();
        _orchestrator = new BackupOrchestrator();
    }

    public async ValueTask DisposeAsync()
    {
        await _catalog.DisposeAsync();
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, recursive: true);
            }
            foreach (var file in Directory.GetFiles(Path.GetTempPath(), Path.GetFileNameWithoutExtension(_dbPath) + "*"))
            {
                File.Delete(file);
            }
        }
        catch
        {
            // Best effort cleanup
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task InitialBackup_ShouldUploadChunks_AndCommitSnapshot()
    {
        await _catalog.InitializeAsync();
        using var masterKey = MasterKey.Generate();

        // 1. Create BackupSet
        var bsetId = BackupSetId.New();
        var backupSet = new BackupSet(bsetId, "TestSet", [_testRoot], ["*.tmp"], DateTimeOffset.UtcNow);
        await _catalog.SaveBackupSetAsync(backupSet);

        // 2. Create test files
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "doc1.txt"), "Hello Document 1");
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "doc2.txt"), "Hello Document 2");

        // 3. Run initial backup
        var snapId = await _orchestrator.RunBackupAsync(bsetId, masterKey, _storage, _catalog);

        // 4. Verify snapshot state
        var snapshot = await _catalog.GetSnapshotAsync(snapId);
        snapshot.Should().NotBeNull();
        snapshot!.IsCommitted.Should().BeTrue();
        snapshot.SnapshotNumber.Should().Be(1);
        snapshot.TotalFiles.Should().Be(2);

        // 5. Verify catalog files
        var files = await _catalog.GetSnapshotFilesAsync(snapId);
        files.Should().HaveCount(2);

        // 6. Verify chunks were uploaded to storage provider
        var manifestAnchors = await _storage.EnumerateCatalogAnchorsAsync();
        manifestAnchors.Should().ContainSingle();
    }

    [Fact]
    public async Task IncrementalBackup_Deduplication_ShouldNotReuploadExistingChunks()
    {
        await _catalog.InitializeAsync();
        using var masterKey = MasterKey.Generate();

        var bsetId = BackupSetId.New();
        var backupSet = new BackupSet(bsetId, "DedupSet", [_testRoot], [], DateTimeOffset.UtcNow);
        await _catalog.SaveBackupSetAsync(backupSet);

        var fileA = Path.Combine(_testRoot, "fileA.txt");
        var fileB = Path.Combine(_testRoot, "fileB.txt");
        await File.WriteAllTextAsync(fileA, "Content of File A");
        await File.WriteAllTextAsync(fileB, "Content of File B");

        // First backup
        var snap1Id = await _orchestrator.RunBackupAsync(bsetId, masterKey, _storage, _catalog);
        var snapshot1Files = await _catalog.GetSnapshotFilesAsync(snap1Id);
        var chunkIdFileA = snapshot1Files.First(f => f.Path.Value == "fileA.txt").ChunkRefs[0];

        // Mutate: Keep fileA identical, modify fileB, add fileC
        await Task.Delay(50);
        await File.WriteAllTextAsync(fileB, "Content of File B - Modified!");
        var fileC = Path.Combine(_testRoot, "fileC.txt");
        await File.WriteAllTextAsync(fileC, "Content of File C - Brand New");

        // Second backup (Incremental)
        var snap2Id = await _orchestrator.RunBackupAsync(bsetId, masterKey, _storage, _catalog);

        var snapshot2 = await _catalog.GetSnapshotAsync(snap2Id);
        snapshot2.Should().NotBeNull();
        snapshot2!.SnapshotNumber.Should().Be(2);
        snapshot2.TotalFiles.Should().Be(3);

        var snapshot2Files = await _catalog.GetSnapshotFilesAsync(snap2Id);
        var chunkIdFileA_Snap2 = snapshot2Files.First(f => f.Path.Value == "fileA.txt").ChunkRefs[0];

        // Chunks for fileA should be reused
        chunkIdFileA_Snap2.Should().Be(chunkIdFileA);
    }

    [Fact]
    public async Task LockedFile_WithSkipOption_ShouldSkipGracefully()
    {
        await _catalog.InitializeAsync();
        using var masterKey = MasterKey.Generate();

        var bsetId = BackupSetId.New();
        var backupSet = new BackupSet(bsetId, "LockSet", [_testRoot], [], DateTimeOffset.UtcNow);
        await _catalog.SaveBackupSetAsync(backupSet);

        var fileOk = Path.Combine(_testRoot, "ok.txt");
        await File.WriteAllTextAsync(fileOk, "Regular readable file");

        var fileLocked = Path.Combine(_testRoot, "locked.txt");
        await File.WriteAllTextAsync(fileLocked, "Locked content");

        // Hold exclusive lock on fileLocked
        using var fs = new FileStream(fileLocked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var options = new BackupEngineOptions { SkipLockedFiles = true };
        var snapId = await _orchestrator.RunBackupAsync(bsetId, masterKey, _storage, _catalog, options);

        var snapshot = await _catalog.GetSnapshotAsync(snapId);
        snapshot!.IsCommitted.Should().BeTrue();
        snapshot.TotalFiles.Should().Be(1); // Only ok.txt backed up

        var files = await _catalog.GetSnapshotFilesAsync(snapId);
        files.Should().ContainSingle();
        files[0].Path.Value.Should().Be("ok.txt");
    }
}
