using BackupApp.Catalog;
using BackupApp.Domain;
using FluentAssertions;
using Xunit;

namespace BackupApp.UnitTests;

public class CatalogRepositoryTests : IAsyncDisposable
{
    private readonly string _dbPath;
    private readonly SqliteCatalogRepository _repo;

    public CatalogRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"catalog_test_{Guid.NewGuid():N}.db");
        _repo = new SqliteCatalogRepository(_dbPath);
    }

    public async ValueTask DisposeAsync()
    {
        await _repo.DisposeAsync();
        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch
        {
            // Best effort cleanup in tests
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task InitializeAsync_ShouldApplyMigrationsSuccessfully()
    {
        await _repo.InitializeAsync();
        File.Exists(_dbPath).Should().BeTrue();
    }

    [Fact]
    public async Task BackupSet_SaveAndRetrieve_ShouldMatchProperties()
    {
        await _repo.InitializeAsync();

        var id = BackupSetId.New();
        var backupSet = new BackupSet(
            id,
            "My Documents",
            [@"C:\Users\Alice\Documents"],
            [@"*.tmp", @"node_modules/*"],
            DateTimeOffset.UtcNow
        );

        await _repo.SaveBackupSetAsync(backupSet);
        var retrieved = await _repo.GetBackupSetAsync(id);

        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(id);
        retrieved.Name.Should().Be("My Documents");
        retrieved.IncludedRoots.Should().ContainSingle().Which.Should().Be(@"C:\Users\Alice\Documents");
        retrieved.ExcludedPatterns.Should().HaveCount(2);
    }

    [Fact]
    public async Task Snapshot_Lifecycle_ShouldTransitionAndCommit()
    {
        await _repo.InitializeAsync();

        var setId = BackupSetId.New();
        await _repo.SaveBackupSetAsync(new BackupSet(setId, "Work", ["C:/Work"], [], DateTimeOffset.UtcNow));

        var snapId = SnapshotId.New();
        var snapshot = new Snapshot(
            snapId,
            setId,
            1,
            DateTimeOffset.UtcNow,
            SnapshotStatus.InProgress,
            0,
            0
        );

        await _repo.CreateSnapshotAsync(snapshot);

        var retrieved = await _repo.GetSnapshotAsync(snapId);
        retrieved.Should().NotBeNull();
        retrieved!.Status.Should().Be(SnapshotStatus.InProgress);

        await _repo.CommitSnapshotAsync(snapId, 5, 20480);

        var committed = await _repo.GetSnapshotAsync(snapId);
        committed.Should().NotBeNull();
        committed!.Status.Should().Be(SnapshotStatus.Committed);
        committed.TotalFiles.Should().Be(5);
        committed.TotalBytes.Should().Be(20480);
        committed.IsCommitted.Should().BeTrue();

        var latest = await _repo.GetLatestCommittedSnapshotAsync(setId);
        latest.Should().NotBeNull();
        latest!.Id.Should().Be(snapId);
    }

    [Fact]
    public async Task FileVersions_SaveAndQuery_ShouldPersistHierarchies()
    {
        await _repo.InitializeAsync();

        var setId = BackupSetId.New();
        await _repo.SaveBackupSetAsync(new BackupSet(setId, "Photos", ["C:/Photos"], [], DateTimeOffset.UtcNow));

        var snapId = SnapshotId.New();
        await _repo.CreateSnapshotAsync(new Snapshot(snapId, setId, 1, DateTimeOffset.UtcNow, SnapshotStatus.InProgress, 0, 0));

        var fileEntryId = FileEntryId.New();
        var entry = new FileEntry(fileEntryId, setId, snapId, DateTimeOffset.UtcNow);

        var chunkId = ObjectId.FromHex("abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890");
        var chunk = new StoredChunk(chunkId, 1024, "plain_sha", 1040, "enc_sha");
        await _repo.SaveChunksAsync([chunk]);

        var path = CanonicalPath.From("trips/italy/colosseum.jpg");
        var version = new FileVersion(
            FileVersionId.New(),
            fileEntryId,
            snapId,
            path,
            1024,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "hash123",
            FileEntryAttributes.Archive,
            [chunkId]
        );

        await _repo.SaveFileEntriesAndVersionsAsync([entry], [version]);
        await _repo.CommitSnapshotAsync(snapId, 1, 1024);

        var files = await _repo.GetSnapshotFilesAsync(snapId);
        files.Should().ContainSingle();
        files[0].Path.Value.Should().Be("trips/italy/colosseum.jpg");
        files[0].SizeBytes.Should().Be(1024);

        var latestVer = await _repo.GetLatestFileVersionAsync(setId, path);
        latestVer.Should().NotBeNull();
        latestVer!.ContentHashSha256.Should().Be("hash123");
    }

    [Fact]
    public async Task RemoteObjectRef_SaveAndRetrieve_ShouldTrackStatus()
    {
        await _repo.InitializeAsync();

        var objId = ObjectId.FromHex("deadbeef0102030405060708deadbeef0102030405060708deadbeef01020304");
        var remoteRef = new RemoteObjectRef(
            objId,
            "telegram",
            "msg_12345",
            DateTimeOffset.UtcNow,
            true
        );

        await _repo.SaveRemoteObjectRefAsync(remoteRef);
        var retrieved = await _repo.GetRemoteObjectRefAsync(objId, "telegram");

        retrieved.Should().NotBeNull();
        retrieved!.RemoteIdentifier.Should().Be("msg_12345");
        retrieved.IsVerified.Should().BeTrue();
    }

    [Fact]
    public async Task BackupJob_SaveAndProgress_ShouldUpdateTruthfully()
    {
        await _repo.InitializeAsync();

        var setId = BackupSetId.New();
        await _repo.SaveBackupSetAsync(new BackupSet(setId, "Documents", ["C:/Docs"], [], DateTimeOffset.UtcNow));

        var snapId = SnapshotId.New();
        await _repo.CreateSnapshotAsync(new Snapshot(snapId, setId, 1, DateTimeOffset.UtcNow, SnapshotStatus.InProgress, 0, 0));

        var jobId = JobId.New();
        var job = new BackupJob(
            jobId,
            snapId,
            JobType.InitialBackup,
            JobStatus.Running,
            DateTimeOffset.UtcNow,
            null,
            100,
            0,
            1024000,
            0
        );

        await _repo.SaveBackupJobAsync(job);
        await _repo.UpdateBackupJobProgressAsync(jobId, 45, 450000);

        var updated = await _repo.GetBackupJobAsync(jobId);
        updated.Should().NotBeNull();
        updated!.ProcessedFiles.Should().Be(45);
        updated.ProcessedBytes.Should().Be(450000);

        await _repo.CompleteBackupJobAsync(jobId, JobStatus.Completed);
        var completed = await _repo.GetBackupJobAsync(jobId);
        completed.Should().NotBeNull();
        completed!.Status.Should().Be(JobStatus.Completed);
        completed.CompletedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task GetFileVersionsAsync_ShouldReturnAllVersionsDescending_WithoutOverwritingPriorRecords()
    {
        await _repo.InitializeAsync();

        var setId = BackupSetId.New();
        await _repo.SaveBackupSetAsync(new BackupSet(setId, "Docs", ["C:/Docs"], [], DateTimeOffset.UtcNow));

        var snap1Id = SnapshotId.New();
        await _repo.CreateSnapshotAsync(new Snapshot(snap1Id, setId, 1, DateTimeOffset.UtcNow.AddHours(-2), SnapshotStatus.InProgress, 0, 0));
        await _repo.CommitSnapshotAsync(snap1Id, 1, 100);

        var snap2Id = SnapshotId.New();
        await _repo.CreateSnapshotAsync(new Snapshot(snap2Id, setId, 2, DateTimeOffset.UtcNow.AddHours(-1), SnapshotStatus.InProgress, 0, 0));
        await _repo.CommitSnapshotAsync(snap2Id, 1, 200);

        var entryId = FileEntryId.New();
        var entry = new FileEntry(entryId, setId, snap1Id, DateTimeOffset.UtcNow.AddHours(-2));

        var chunk1 = ObjectId.FromHex("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var chunk2 = ObjectId.FromHex("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
        await _repo.SaveChunksAsync([
            new StoredChunk(chunk1, 100, "hash1", 120, "hash1"),
            new StoredChunk(chunk2, 200, "hash2", 220, "hash2")
        ]);

        var v1 = new FileVersion(
            FileVersionId.New(),
            entryId,
            snap1Id,
            CanonicalPath.From("report.docx"),
            100,
            DateTimeOffset.UtcNow.AddHours(-2),
            DateTimeOffset.UtcNow.AddHours(-2),
            "hash1",
            FileEntryAttributes.None,
            [chunk1]
        );

        var v2 = new FileVersion(
            FileVersionId.New(),
            entryId,
            snap2Id,
            CanonicalPath.From("report.docx"),
            200,
            DateTimeOffset.UtcNow.AddHours(-1),
            DateTimeOffset.UtcNow.AddHours(-1),
            "hash2",
            FileEntryAttributes.None,
            [chunk2]
        );

        await _repo.SaveFileEntriesAndVersionsAsync([entry], [v1, v2]);

        var versions = await _repo.GetFileVersionsAsync(CanonicalPath.From("report.docx"), setId);

        versions.Should().HaveCount(2);
        versions[0].SnapshotId.Should().Be(snap2Id);
        versions[0].SizeBytes.Should().Be(200);
        versions[0].ChunkRefs.Should().ContainSingle().Which.Should().Be(chunk2);

        versions[1].SnapshotId.Should().Be(snap1Id);
        versions[1].SizeBytes.Should().Be(100);
        versions[1].ChunkRefs.Should().ContainSingle().Which.Should().Be(chunk1);
    }
}
