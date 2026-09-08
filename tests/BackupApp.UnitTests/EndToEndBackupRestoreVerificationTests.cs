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

public class EndToEndBackupRestoreVerificationTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _sourceDir;
    private readonly string _restoreDir;
    private readonly string _catalogDbPath;

    public EndToEndBackupRestoreVerificationTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"dry_run_e2e_{Guid.NewGuid():N}");
        _sourceDir = Path.Combine(_testRoot, "source");
        _restoreDir = Path.Combine(_testRoot, "restore");
        _catalogDbPath = Path.Combine(_testRoot, "catalog.sqlite");

        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_restoreDir);
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
    public async Task EndToEnd_BackupAndRestore_DryRun_ShouldPreserveAllFilesAndHashesWith100PercentAccuracy()
    {
        // -------------------------------------------------------------
        // 1. POPULATE SOURCE DIRECTORY WITH COMPLEX & REALISTIC DATASET
        // -------------------------------------------------------------
        var expectedHashes = new Dictionary<string, (long Size, string Sha256)>(StringComparer.OrdinalIgnoreCase);

        async Task CreateFileAsync(string relativePath, byte[] content)
        {
            var fullPath = Path.Combine(_sourceDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllBytesAsync(fullPath, content);

            var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
            var canonicalRel = relativePath.Replace('\\', '/');
            expectedHashes[canonicalRel] = (content.Length, hash);
        }

        // A. Standard English text
        await CreateFileAsync("document_english.txt", Encoding.UTF8.GetBytes("Hello World, this is an automated backup test!"));

        // B. Hebrew filenames and content
        await CreateFileAsync("מסמך_בעברית_מלא.txt", Encoding.UTF8.GetBytes("שלום עולם! זהו קובץ בדיקה מלא בעברית הכולל תווי יוניקוד מורכבים 2026."));

        // C. Special characters in path and filename
        await CreateFileAsync("special_chars/קובץ_מיוחד_!@#$%^&()_+={}[];',.txt", Encoding.UTF8.GetBytes("Special symbols inside filename and payload #%&!"));

        // D. Nested subdirectories
        await CreateFileAsync("תת_תיקייה/עמוקה_מאוד/2026/נתונים/financial_report.csv", Encoding.UTF8.GetBytes("id,name,amount\n1,Reuven,1500\n2,Shimon,3200\n3,Levi,4500\n"));

        // E. Binary payload (2 MB randomized bytes)
        var binaryPayload = new byte[2 * 1024 * 1024];
        RandomNumberGenerator.Fill(binaryPayload);
        await CreateFileAsync("media/large_binary.dat", binaryPayload);

        // F. Empty file
        await CreateFileAsync("system/empty_file.log", []);

        // G. In-use / Locked file: open file with FileShare.ReadWrite to simulate active file in other process
        var inUsePath = Path.Combine(_sourceDir, "active_in_use.db");
        var inUseContent = Encoding.UTF8.GetBytes("Active database log file currently open by another application.");
        await File.WriteAllBytesAsync(inUsePath, inUseContent);
        expectedHashes["active_in_use.db"] = (inUseContent.Length, Convert.ToHexString(SHA256.HashData(inUseContent)).ToLowerInvariant());

        // Hold file open in shared read/write mode during the entire backup
        await using var openFileStream = new FileStream(
            inUsePath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.ReadWrite | FileShare.Delete);

        // -------------------------------------------------------------
        // 2. RUN BACKUP ORCHESTRATION
        // -------------------------------------------------------------
        using var masterKey = MasterKey.Generate();
        var storage = new InMemoryStorageProvider();
        var catalog = new SqliteCatalogRepository(_catalogDbPath);
        await catalog.InitializeAsync();

        var bsetId = BackupSetId.New();
        var backupSet = new BackupSet(bsetId, "DryRunBackupSet", [_sourceDir], [], DateTimeOffset.UtcNow);
        await catalog.SaveBackupSetAsync(backupSet);

        var orchestrator = new BackupOrchestrator();
        var snapshotId = await orchestrator.RunBackupAsync(
            bsetId,
            masterKey,
            storage,
            catalog,
            options: BackupEngineOptions.Default
        );

        snapshotId.Should().NotBeNull();

        // Verify snapshot status in catalog
        var committedSnapshot = await catalog.GetSnapshotAsync(snapshotId);
        committedSnapshot.Should().NotBeNull();
        committedSnapshot!.Status.Should().Be(SnapshotStatus.Committed);
        committedSnapshot.TotalFiles.Should().Be(expectedHashes.Count);

        // Discover remote manifest
        var discovery = new RemoteCatalogDiscoveryService();
        var manifests = await discovery.DiscoverRemoteManifestsAsync(storage, masterKey);
        manifests.Should().ContainSingle();
        var manifest = manifests[0];
        manifest.Items.Should().HaveCount(expectedHashes.Count);

        // -------------------------------------------------------------
        // 3. RUN FULL RESTORE TO ISOLATED DESTINATION
        // -------------------------------------------------------------
        var restoreOrchestrator = new RestoreOrchestrator();
        var restoreOptions = new RestoreOptions(
            DestinationRootPath: _restoreDir,
            ConflictResolution: RestoreConflictResolution.Overwrite,
            RestoreTimestamps: true
        );

        await restoreOrchestrator.RestoreAllAsync(
            manifest,
            masterKey,
            storage,
            restoreOptions
        );

        // -------------------------------------------------------------
        // 4. VERIFY 100% BYTE-FOR-BYTE FIDELITY ACROSS ALL FILES
        // -------------------------------------------------------------
        foreach (var (relPath, (expectedSize, expectedSha256)) in expectedHashes)
        {
            var restoredFilePath = Path.Combine(_restoreDir, relPath.Replace('/', Path.DirectorySeparatorChar));

            File.Exists(restoredFilePath).Should().BeTrue($"Restored file '{relPath}' must exist at target location.");

            var restoredBytes = await File.ReadAllBytesAsync(restoredFilePath);
            restoredBytes.LongLength.Should().Be(expectedSize, $"File '{relPath}' size must match original.");

            var restoredHash = Convert.ToHexString(SHA256.HashData(restoredBytes)).ToLowerInvariant();
            restoredHash.Should().Be(expectedSha256, $"File '{relPath}' SHA-256 hash must be 100% identical to original.");
        }

        // -------------------------------------------------------------
        // 5. TEST FOLDER RESTORE (RESTORE FOLDER PREFIX ISOLATED)
        // -------------------------------------------------------------
        var subFolderRestoreDir = Path.Combine(_testRoot, "restore_subfolder");
        Directory.CreateDirectory(subFolderRestoreDir);

        await restoreOrchestrator.RestoreFolderAsync(
            manifest,
            "תת_תיקייה",
            masterKey,
            storage,
            new RestoreOptions(subFolderRestoreDir)
        );

        var restoredSubDoc = Path.Combine(subFolderRestoreDir, "תת_תיקייה", "עמוקה_מאוד", "2026", "נתונים", "financial_report.csv");
        File.Exists(restoredSubDoc).Should().BeTrue("Subfolder restore must recursively reconstruct directory structure.");

        var subDocHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(restoredSubDoc))).ToLowerInvariant();
        subDocHash.Should().Be(expectedHashes["תת_תיקייה/עמוקה_מאוד/2026/נתונים/financial_report.csv"].Sha256);
    }
}
