using BackupApp.BackupEngine;
using BackupApp.BackupEngine.Capture;
using BackupApp.BackupEngine.ChangeDetection;
using BackupApp.BackupEngine.Scanner;
using BackupApp.Catalog;
using BackupApp.Domain;
using FluentAssertions;
using Xunit;

namespace BackupApp.UnitTests;

public class Gate1VerificationTests : IAsyncDisposable
{
    private readonly string _testRoot;
    private readonly string _dbPath;
    private readonly SqliteCatalogRepository _catalog;
    private readonly FileDiscoveryScanner _scanner;
    private readonly StableFileCaptureService _capture;
    private readonly ChangeDetectionService _changeDetection;

    public Gate1VerificationTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"gate1_fixture_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);

        _dbPath = Path.Combine(Path.GetTempPath(), $"gate1_catalog_{Guid.NewGuid():N}.db");
        _catalog = new SqliteCatalogRepository(_dbPath);

        _scanner = new FileDiscoveryScanner();
        _capture = new StableFileCaptureService();
        _changeDetection = new ChangeDetectionService();
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
    public async Task Gate1_DeterministicScanTwice_YieldsCorrectIncrementalDelta_AndConsistentCatalog()
    {
        // 1. Initialize catalog
        await _catalog.InitializeAsync();

        var backupSetId = BackupSetId.New();
        var backupSet = new BackupSet(backupSetId, "Gate1Set", [_testRoot], ["*.tmp"], DateTimeOffset.UtcNow);
        await _catalog.SaveBackupSetAsync(backupSet);

        // 2. Populate Initial Test Fixture (5 files)
        var fileA = Path.Combine(_testRoot, "fileA.txt");
        var fileB = Path.Combine(_testRoot, "fileB.txt");
        var fileC = Path.Combine(_testRoot, "fileC.txt");
        var fileD = Path.Combine(_testRoot, "fileD.txt");
        var fileE = Path.Combine(_testRoot, "sub", "fileE.txt");
        Directory.CreateDirectory(Path.Combine(_testRoot, "sub"));

        await File.WriteAllTextAsync(fileA, "Content A");
        await File.WriteAllTextAsync(fileB, "Content B");
        await File.WriteAllTextAsync(fileC, "Content C");
        await File.WriteAllTextAsync(fileD, "Content D to be renamed");
        await File.WriteAllTextAsync(fileE, "Content E");

        // --- FIRST SCAN (Initial Backup) ---
        var filter = new ExclusionFilter(["*.tmp"]);
        var scan1Files = new List<DiscoveredFile>();
        await foreach (var file in _scanner.DiscoverFilesAsync(_testRoot, filter))
        {
            scan1Files.Add(file);
        }

        scan1Files.Should().HaveCount(5);

        // Change detection against empty history
        var changes1 = await _changeDetection.DetectChangesAsync(scan1Files, [], _capture);
        changes1.Should().HaveCount(5);
        changes1.Should().OnlyContain(c => c.ChangeType == FileChangeType.New);

        // Commit Snapshot #1
        var snap1 = SnapshotStateMachine.StartSnapshot(backupSetId, nextSnapshotNumber: 1);
        await _catalog.CreateSnapshotAsync(snap1);

        var entries1 = new List<FileEntry>();
        var versions1 = new List<FileVersion>();
        long totalBytes1 = 0;

        foreach (var change in changes1)
        {
            var cap = await _capture.CaptureFileAsync(change.Discovered!.AbsolutePath);
            var chunks = cap.Chunks.Select(c => new StoredChunk(c.Id, c.SizeBytes, c.PlaintextSha256, c.SizeBytes, c.PlaintextSha256));
            await _catalog.SaveChunksAsync(chunks);

            var entryId = FileEntryId.New();
            entries1.Add(new FileEntry(entryId, backupSetId, snap1.Id, DateTimeOffset.UtcNow));

            var ver = new FileVersion(
                FileVersionId.New(),
                entryId,
                snap1.Id,
                change.Path,
                change.Discovered.SizeBytes,
                change.Discovered.CreatedAtUtc,
                change.Discovered.ModifiedUtc,
                cap.ContentHashSha256!,
                change.Discovered.Attributes,
                cap.Chunks.Select(c => c.Id).ToList()
            );
            versions1.Add(ver);
            totalBytes1 += change.Discovered.SizeBytes;
        }

        await _catalog.SaveFileEntriesAndVersionsAsync(entries1, versions1);
        await _catalog.CommitSnapshotAsync(snap1.Id, versions1.Count, totalBytes1);

        var snap1FromDb = await _catalog.GetSnapshotAsync(snap1.Id);
        snap1FromDb!.IsCommitted.Should().BeTrue();
        snap1FromDb.TotalFiles.Should().Be(5);

        // 3. Mutate Fixture for Incremental Backup:
        // - fileA: Unchanged
        // - fileB: Modified
        // - fileC: Deleted
        // - fileD: Renamed to fileD_renamed.txt (same content)
        // - fileE: Unchanged
        // - fileF: New file added

        await Task.Delay(50); // slight time shift to ensure modified timestamp updates cleanly
        await File.WriteAllTextAsync(fileB, "Content B - Modified!");
        File.Delete(fileC);
        var fileDrenamed = Path.Combine(_testRoot, "fileD_renamed.txt");
        File.Move(fileD, fileDrenamed);
        var fileF = Path.Combine(_testRoot, "fileF.txt");
        await File.WriteAllTextAsync(fileF, "Content F - Brand New");

        // --- SECOND SCAN (Incremental Delta) ---
        var scan2Files = new List<DiscoveredFile>();
        await foreach (var file in _scanner.DiscoverFilesAsync(_testRoot, filter))
        {
            scan2Files.Add(file);
        }

        scan2Files.Should().HaveCount(5); // A, B, D_renamed, E, F

        var prevSnapshotFiles = await _catalog.GetSnapshotFilesAsync(snap1.Id);
        prevSnapshotFiles.Should().HaveCount(5);

        var changes2 = await _changeDetection.DetectChangesAsync(scan2Files, prevSnapshotFiles, _capture);

        // Validate delta:
        // 2 Unchanged (fileA, fileE)
        // 1 Modified (fileB)
        // 1 Deleted (fileC)
        // 1 Renamed (fileD -> fileD_renamed.txt)
        // 1 New (fileF)
        changes2.Should().HaveCount(6); // 5 current + 1 deleted

        var unchanged = changes2.Where(c => c.ChangeType == FileChangeType.Unchanged).ToList();
        var modified = changes2.Where(c => c.ChangeType == FileChangeType.Modified).ToList();
        var deleted = changes2.Where(c => c.ChangeType == FileChangeType.Deleted).ToList();
        var renamed = changes2.Where(c => c.ChangeType == FileChangeType.Renamed).ToList();
        var newFiles = changes2.Where(c => c.ChangeType == FileChangeType.New).ToList();

        unchanged.Should().HaveCount(2);
        unchanged.Select(u => u.Path.Value).Should().Contain(["fileA.txt", "sub/fileE.txt"]);

        modified.Should().ContainSingle();
        modified[0].Path.Value.Should().Be("fileB.txt");

        deleted.Should().ContainSingle();
        deleted[0].Path.Value.Should().Be("fileC.txt");

        renamed.Should().ContainSingle();
        renamed[0].Path.Value.Should().Be("fileD_renamed.txt");
        renamed[0].OldPath!.Value.Value.Should().Be("fileD.txt");

        newFiles.Should().ContainSingle();
        newFiles[0].Path.Value.Should().Be("fileF.txt");

        // Commit Snapshot #2
        var snap2 = SnapshotStateMachine.StartSnapshot(backupSetId, nextSnapshotNumber: 2);
        await _catalog.CreateSnapshotAsync(snap2);

        var entries2 = new List<FileEntry>();
        var versions2 = new List<FileVersion>();
        long totalBytes2 = 0;

        foreach (var change in changes2.Where(c => c.ChangeType != FileChangeType.Deleted))
        {
            var cap = await _capture.CaptureFileAsync(change.Discovered!.AbsolutePath);
            var chunks = cap.Chunks.Select(c => new StoredChunk(c.Id, c.SizeBytes, c.PlaintextSha256, c.SizeBytes, c.PlaintextSha256));
            await _catalog.SaveChunksAsync(chunks);

            var entryId = change.ReusedFileEntryId ?? FileEntryId.New();
            if (change.ReusedFileEntryId == null)
            {
                entries2.Add(new FileEntry(entryId, backupSetId, snap2.Id, DateTimeOffset.UtcNow));
            }

            var ver = new FileVersion(
                FileVersionId.New(),
                entryId,
                snap2.Id,
                change.Path,
                change.Discovered.SizeBytes,
                change.Discovered.CreatedAtUtc,
                change.Discovered.ModifiedUtc,
                cap.ContentHashSha256!,
                change.Discovered.Attributes,
                cap.Chunks.Select(c => c.Id).ToList()
            );
            versions2.Add(ver);
            totalBytes2 += change.Discovered.SizeBytes;
        }

        await _catalog.SaveFileEntriesAndVersionsAsync(entries2, versions2);
        await _catalog.CommitSnapshotAsync(snap2.Id, versions2.Count, totalBytes2);

        // 4. Test Search (Task 1.10)
        var searchResults = await _catalog.SearchFilesAsync(backupSetId, "renamed");
        searchResults.Should().ContainSingle();
        searchResults[0].Path.Value.Should().Be("fileD_renamed.txt");

        // 5. Test Crash/Restart Resilience
        // Close current connection, instantiate a new repository instance pointing to same file
        await _catalog.DisposeAsync();

        await using var restartedCatalog = new SqliteCatalogRepository(_dbPath);
        await restartedCatalog.InitializeAsync();

        var latestCommitted = await restartedCatalog.GetLatestCommittedSnapshotAsync(backupSetId);
        latestCommitted.Should().NotBeNull();
        latestCommitted!.SnapshotNumber.Should().Be(2);
        latestCommitted.TotalFiles.Should().Be(5);

        var recoveredFiles = await restartedCatalog.GetSnapshotFilesAsync(latestCommitted.Id);
        recoveredFiles.Should().HaveCount(5);
        recoveredFiles.Select(f => f.Path.Value).Should().Contain("fileD_renamed.txt");
    }
}
