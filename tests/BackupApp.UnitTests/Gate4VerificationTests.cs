using BackupApp.BackupEngine;
using BackupApp.Catalog;
using BackupApp.Crypto;
using BackupApp.Domain;
using BackupApp.Storage;
using FluentAssertions;
using Xunit;

namespace BackupApp.UnitTests;

public class Gate4VerificationTests : IAsyncDisposable
{
    private readonly string _testRoot;
    private readonly string _dbPath;
    private readonly SqliteCatalogRepository _catalog;
    private readonly InMemoryStorageProvider _storage;
    private readonly BackupOrchestrator _orchestrator;

    public Gate4VerificationTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"gate4_dataset_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);

        _dbPath = Path.Combine(Path.GetTempPath(), $"gate4_catalog_{Guid.NewGuid():N}.db");
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
    public async Task Gate4_LargeDataset_IncrementalModifications_ForcedTermination_ResumesTruthfully()
    {
        await _catalog.InitializeAsync();
        using var masterKey = MasterKey.Generate();

        // 1. Create Backup Set
        var bsetId = BackupSetId.New();
        var backupSet = new BackupSet(bsetId, "Gate4BackupSet", [_testRoot], ["*.tmp"], DateTimeOffset.UtcNow);
        await _catalog.SaveBackupSetAsync(backupSet);

        // 2. Populate Deterministic Dataset (6 files across subdirectories with Hebrew and text)
        var subDir = Path.Combine(_testRoot, "subfolder");
        Directory.CreateDirectory(subDir);

        var file1 = Path.Combine(_testRoot, "file1.txt");
        var file2 = Path.Combine(_testRoot, "file2_hebrew_שם_קובץ.txt");
        var file3 = Path.Combine(_testRoot, "file3_to_modify.bin");
        var file4 = Path.Combine(_testRoot, "file4_to_rename.txt");
        var file5 = Path.Combine(_testRoot, "file5_to_delete.txt");
        var file6 = Path.Combine(subDir, "file6_nested.txt");

        await File.WriteAllTextAsync(file1, "Content of File 1");
        await File.WriteAllTextAsync(file2, "תוכן עברי של קובץ מספר שתיים");
        await File.WriteAllBytesAsync(file3, [1, 2, 3, 4, 5, 6, 7, 8]);
        await File.WriteAllTextAsync(file4, "Content of File 4 that will be renamed later");
        await File.WriteAllTextAsync(file5, "Content of File 5 to be deleted");
        await File.WriteAllTextAsync(file6, "Nested file in subfolder");

        // 3. Initial Backup -> Snapshot #1
        var snap1Id = await _orchestrator.RunBackupAsync(bsetId, masterKey, _storage, _catalog);

        var snap1 = await _catalog.GetSnapshotAsync(snap1Id);
        snap1.Should().NotBeNull();
        snap1!.IsCommitted.Should().BeTrue();
        snap1.SnapshotNumber.Should().Be(1);
        snap1.TotalFiles.Should().Be(6);

        // 4. Incremental Modifications
        await Task.Delay(50);
        // Modify file3
        await File.WriteAllBytesAsync(file3, [10, 20, 30, 40, 50, 60, 70, 80, 90]);
        // Rename file4 -> file4_renamed.txt
        var file4Renamed = Path.Combine(_testRoot, "file4_renamed.txt");
        File.Move(file4, file4Renamed);
        // Delete file5
        File.Delete(file5);
        // Add file7
        var file7 = Path.Combine(subDir, "file7_new.txt");
        await File.WriteAllTextAsync(file7, "Brand new file 7");

        // 5. SIMULATE FORCED TERMINATION (Crash / Cancellation during backup)
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately to simulate abrupt interruption

        var actAborted = () => _orchestrator.RunBackupAsync(bsetId, masterKey, _storage, _catalog, cancellationToken: cts.Token);
        await actAborted.Should().ThrowAsync<OperationCanceledException>();

        // 6. Verify Truthful State after Abort:
        // - Latest committed snapshot is still Snapshot #1
        // - Database is NOT corrupted
        var latestCommittedAfterAbort = await _catalog.GetLatestCommittedSnapshotAsync(bsetId);
        latestCommittedAfterAbort.Should().NotBeNull();
        latestCommittedAfterAbort!.SnapshotNumber.Should().Be(1);
        latestCommittedAfterAbort.TotalFiles.Should().Be(6);

        // 7. RESUME BACKUP: run cleanly
        var snap2Id = await _orchestrator.RunBackupAsync(bsetId, masterKey, _storage, _catalog);

        var snap2 = await _catalog.GetSnapshotAsync(snap2Id);
        snap2.Should().NotBeNull();
        snap2!.IsCommitted.Should().BeTrue();
        snap2.SnapshotNumber.Should().Be(2);

        // Total files in Snapshot #2 should be 6:
        // file1 (unchanged)
        // file2 (unchanged)
        // file3 (modified)
        // file4_renamed (renamed)
        // (file5 deleted)
        // file6 (unchanged)
        // file7 (new)
        // Total = 6 files!
        snap2.TotalFiles.Should().Be(6);

        var snap2Files = await _catalog.GetSnapshotFilesAsync(snap2Id);
        snap2Files.Should().HaveCount(6);

        var paths = snap2Files.Select(f => f.Path.Value).ToList();
        paths.Should().Contain("file1.txt");
        paths.Should().Contain("file2_hebrew_שם_קובץ.txt");
        paths.Should().Contain("file3_to_modify.bin");
        paths.Should().Contain("file4_renamed.txt");
        paths.Should().NotContain("file4_to_rename.txt");
        paths.Should().NotContain("file5_to_delete.txt");
        paths.Should().Contain("subfolder/file6_nested.txt");
        paths.Should().Contain("subfolder/file7_new.txt");

        // 8. Verify Crash/Restart Consistency on fresh repository instance
        await _catalog.DisposeAsync();

        await using var freshCatalog = new SqliteCatalogRepository(_dbPath);
        await freshCatalog.InitializeAsync();

        var committedFromFresh = await freshCatalog.GetLatestCommittedSnapshotAsync(bsetId);
        committedFromFresh.Should().NotBeNull();
        committedFromFresh!.SnapshotNumber.Should().Be(2);
        committedFromFresh.TotalFiles.Should().Be(6);
    }
}
