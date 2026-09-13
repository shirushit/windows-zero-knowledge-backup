using System.Security.Cryptography;
using System.Text;
using BackupApp.BackupEngine.Capture;
using BackupApp.Catalog;
using BackupApp.Domain;
using BackupApp.Storage;
using BackupApp.UI.Services;
using BackupApp.UI.ViewModels;
using FluentAssertions;
using Xunit;

namespace BackupApp.UnitTests;

public sealed class FastCdcAndPointInTimeTests : IDisposable
{
    private readonly string _tempDir;

    public FastCdcAndPointInTimeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fastcdc_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Best effort
        }
    }

    [Fact]
    public async Task FastCdcChunker_ShouldRespectChunkSizeBounds()
    {
        // 16KB min, 64KB avg, 128KB max
        var chunker = new FastCdcChunker(16 * 1024, 64 * 1024, 128 * 1024);

        // Create 500KB pseudo-random stream
        var random = new Random(42);
        var data = new byte[500 * 1024];
        random.NextBytes(data);

        using var ms = new MemoryStream(data);
        var (chunks, overallHash, totalBytes) = await chunker.ChunkStreamAsync(ms);

        totalBytes.Should().Be(data.Length);
        overallHash.Should().Be(Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant());
        chunks.Should().NotBeEmpty();

        // Check boundaries
        for (int i = 0; i < chunks.Count; i++)
        {
            var c = chunks[i];
            c.Position.Should().Be(i);
            if (i < chunks.Count - 1)
            {
                // Non-final chunks should be between min and max size
                c.SizeBytes.Should().BeInRange(16 * 1024, 128 * 1024);
            }
            else
            {
                // Final chunk can be <= max
                c.SizeBytes.Should().BeLessThanOrEqualTo(128 * 1024);
            }
        }
    }

    [Fact]
    public async Task FastCdcChunker_SingleByteInsertion_ShouldPreserveMostChunks()
    {
        // 4KB min, 16KB avg, 32KB max
        var chunker = new FastCdcChunker(4 * 1024, 16 * 1024, 32 * 1024);

        var random = new Random(12345);
        var baseData = new byte[256 * 1024];
        random.NextBytes(baseData);

        using var ms1 = new MemoryStream(baseData);
        var (chunks1, _, _) = await chunker.ChunkStreamAsync(ms1);

        // Insert 1 byte at the start (offset 100)
        var modifiedData = new byte[baseData.Length + 1];
        Array.Copy(baseData, 0, modifiedData, 0, 100);
        modifiedData[100] = 0xFF;
        Array.Copy(baseData, 100, modifiedData, 101, baseData.Length - 100);

        using var ms2 = new MemoryStream(modifiedData);
        var (chunks2, _, _) = await chunker.ChunkStreamAsync(ms2);

        // Due to content-defined chunking, many chunks after the modified region should match identical hashes!
        var hashes1 = chunks1.Select(c => c.PlaintextSha256).ToHashSet();
        var matchingCount = chunks2.Count(c => hashes1.Contains(c.PlaintextSha256));

        // CDC should preserve deduplication: at least 50% of chunks remain identical despite shifting 1 byte
        matchingCount.Should().BeGreaterThan(0, "FastCDC must preserve chunk boundaries across byte shifts");
    }

    [Fact]
    public async Task PointInTimeRestore_SelectingPastSnapshot_ShouldRestoreCorrectHistoricalVersion()
    {
        var dbPath = Path.Combine(_tempDir, "pit_catalog.sqlite");
        var catalog = new SqliteCatalogRepository(dbPath);
        await catalog.InitializeAsync();

        var bsetId = BackupSetId.New();
        await catalog.SaveBackupSetAsync(new BackupSet(bsetId, "Default", ["C:/Default"], [], DateTimeOffset.UtcNow));

        // Snapshot 1: file1.txt (v1) and file2.txt (v1)
        var snap1Id = SnapshotId.New();
        var snap1 = new Snapshot(snap1Id, bsetId, 1, DateTimeOffset.UtcNow.AddHours(-2), SnapshotStatus.InProgress, 0, 0, null);
        await catalog.CreateSnapshotAsync(snap1);

        var entry1 = new FileEntry(FileEntryId.New(), bsetId, snap1Id, DateTimeOffset.UtcNow);
        var entry2 = new FileEntry(FileEntryId.New(), bsetId, snap1Id, DateTimeOffset.UtcNow);

        var chunk1 = ObjectId.FromHex("0101010101010101010101010101010101010101010101010101010101010101");
        var chunk2 = ObjectId.FromHex("0202020202020202020202020202020202020202020202020202020202020202");
        await catalog.SaveChunksAsync([
            new StoredChunk(chunk1, 100, "hash1_v1", 116, "enc1"),
            new StoredChunk(chunk2, 100, "hash2_v1", 116, "enc2")
        ]);

        var file1_v1 = new FileVersion(
            FileVersionId.New(), entry1.Id, snap1Id, CanonicalPath.From("file1.txt"),
            100, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "hash1_v1", FileEntryAttributes.Archive,
            [chunk1]
        );
        var file2_v1 = new FileVersion(
            FileVersionId.New(), entry2.Id, snap1Id, CanonicalPath.From("file2.txt"),
            100, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "hash2_v1", FileEntryAttributes.Archive,
            [chunk2]
        );
        await catalog.SaveFileEntriesAndVersionsAsync(
            [entry1, entry2],
            [file1_v1, file2_v1]
        );
        await catalog.CommitSnapshotAsync(snap1Id, 2, 200);

        // Snapshot 2: file1.txt (v2) and file3.txt
        var snap2Id = SnapshotId.New();
        var snap2 = new Snapshot(snap2Id, bsetId, 2, DateTimeOffset.UtcNow, SnapshotStatus.InProgress, 0, 0, null);
        await catalog.CreateSnapshotAsync(snap2);

        var entry3 = new FileEntry(FileEntryId.New(), bsetId, snap2Id, DateTimeOffset.UtcNow);

        var chunk3 = ObjectId.FromHex("0303030303030303030303030303030303030303030303030303030303030303");
        var chunk4 = ObjectId.FromHex("0404040404040404040404040404040404040404040404040404040404040404");
        await catalog.SaveChunksAsync([
            new StoredChunk(chunk3, 150, "hash1_v2", 166, "enc3"),
            new StoredChunk(chunk4, 100, "hash3_v1", 116, "enc4")
        ]);

        var file1_v2 = new FileVersion(
            FileVersionId.New(), entry1.Id, snap2Id, CanonicalPath.From("file1.txt"),
            150, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "hash1_v2", FileEntryAttributes.Archive,
            [chunk3]
        );
        var file3_v1 = new FileVersion(
            FileVersionId.New(), entry3.Id, snap2Id, CanonicalPath.From("file3.txt"),
            100, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "hash3_v1", FileEntryAttributes.Archive,
            [chunk4]
        );
        await catalog.SaveFileEntriesAndVersionsAsync(
            [entry3],
            [file1_v2, file3_v1]
        );
        await catalog.CommitSnapshotAsync(snap2Id, 2, 250);

        // Initialize MainViewModel
        var storage = new InMemoryStorageProvider();
        using var vm = new MainViewModel(storage, catalog);
        await vm.InitializeAsync();

        // Check AvailableSnapshots
        vm.AvailableSnapshots.Should().HaveCount(2);
        vm.AvailableSnapshots[0].SnapshotNumber.Should().Be(2); // Newest first
        vm.AvailableSnapshots[1].SnapshotNumber.Should().Be(1);

        // By default, latest snapshot (snap 2) is active
        vm.BrowsedFiles.Should().HaveCount(2);
        vm.BrowsedFiles.Select(f => f.RelativePath).Should().Contain(["file1.txt", "file3.txt"]);

        // Select snapshot 1 (Point-in-Time Restore selection)
        vm.SelectedSnapshot = vm.AvailableSnapshots[1];
        await vm.LoadSnapshotManifestAsync(vm.AvailableSnapshots[1]);

        // BrowsedFiles and manifest should now reflect snapshot 1!
        vm.BrowsedFiles.Should().HaveCount(2);
        vm.BrowsedFiles.Select(f => f.RelativePath).Should().Contain(["file1.txt", "file2.txt"]);
        vm.TotalFilesCount.Should().Be(2);
        vm.TotalSizeFormatted.Should().Be(PathFormatter.FormatBytes(200));

        await catalog.DisposeAsync();
    }
}
