using System.Security.Cryptography;
using BackupApp.BackupEngine;
using BackupApp.Catalog;
using BackupApp.Crypto;
using BackupApp.Domain;
using BackupApp.RestoreEngine;
using BackupApp.Storage;
using BackupApp.Storage.Telegram;
using FluentAssertions;
using Xunit;

namespace BackupApp.StressTests;

public sealed class ChaosEndToEndStressTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _sourceDir;
    private readonly string _restoreDir;
    private readonly string _catalogDbPath;
    private FileStream? _lockedFileStream;

    public ChaosEndToEndStressTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"chaos_stress_{Guid.NewGuid():N}");
        _sourceDir = Path.Combine(_testRoot, "source");
        _restoreDir = Path.Combine(_testRoot, "restore");
        _catalogDbPath = Path.Combine(_testRoot, "catalog.sqlite");

        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_restoreDir);
    }

    public void Dispose()
    {
        _lockedFileStream?.Dispose();
        _lockedFileStream = null;

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, recursive: true);
            }
        }
        catch
        {
            // Best effort
        }
    }

    [Fact]
    public async Task EndToEnd_ChaosUnderTelegramFailures_ShouldCompleteAndRestoreBitForBit()
    {
        // 1. Generate synthetic file tree with Hebrew, deep folders, special chars, small and large files
        var generatedFiles = await SyntheticFileTreeGenerator.GenerateAsync(_sourceDir, includeLockedFile: true);

        // Keep the locked file open with FileShare.ReadWrite to simulate active concurrent writer
        var lockedFilePath = Path.Combine(_sourceDir, "active_in_use_log.log");
        if (File.Exists(lockedFilePath))
        {
            _lockedFileStream = new FileStream(
                lockedFilePath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.ReadWrite
            );
        }

        // 2. Set up Chaos HTTP Handler
        var chaosOptions = new ChaosOptions
        {
            FaultProbability = 0.25, // 25% chance of fault
            MaxConsecutiveFaultsPerResource = 2,
            LatencyMinMs = 5,
            LatencyMaxMs = 25,
            InjectRateLimit429 = true,
            InjectNetworkReset = true,
            InjectServerError500 = true
        };

        var chaosHandler = new TelegramChaosHttpMessageHandler(chaosOptions);
        var httpClient = new HttpClient(chaosHandler) { Timeout = TimeSpan.FromSeconds(30) };

        var config = new TelegramStorageConfiguration("123456:FAKE_CHAOS_BOT_TOKEN", "-1001234567890");
        var retryPolicy = new StorageRetryPolicy(
            maxRetries: 5,
            initialDelay: TimeSpan.FromMilliseconds(50),
            maxDelay: TimeSpan.FromMilliseconds(300)
        );

        var telegramStorage = new TelegramStorageAdapter(config, httpClient, retryPolicy);

        // 3. Initialize Catalog
        var catalog = new SqliteCatalogRepository(_catalogDbPath);
        await catalog.InitializeAsync();

        var bsetId = BackupSetId.New();
        var backupSet = new BackupSet(bsetId, "StressBackupSet", [_sourceDir], [], DateTimeOffset.UtcNow);
        await catalog.SaveBackupSetAsync(backupSet);

        using var masterKey = MasterKey.Generate();

        // 4. Run Backup under Chaos
        var backupEngine = new BackupOrchestrator();
        var backupOptions = new BackupEngineOptions
        {
            UseFastCdc = true,
            ChunkSizeBytes = 1024 * 1024,
            SkipLockedFiles = false // Must read locked files with FileShare.ReadWrite
        };

        var snapshotId = await backupEngine.RunBackupAsync(
            bsetId,
            masterKey,
            telegramStorage,
            catalog,
            options: backupOptions
        );

        snapshotId.Should().NotBeNull();

        // Verify that chaos was actually injected during the run
        var totalFaults = chaosHandler.Injected429Count +
                          chaosHandler.InjectedNetworkResetCount +
                          chaosHandler.Injected500Count;
        totalFaults.Should().BeGreaterThan(0, "Chaos simulator must have injected at least one transient failure");

        // Verify snapshot was committed
        var committedSnap = await catalog.GetSnapshotAsync(snapshotId);
        committedSnap.Should().NotBeNull();
        committedSnap!.IsCommitted.Should().BeTrue();
        committedSnap.TotalFiles.Should().Be(generatedFiles.Count);

        // 5. Discover Manifest for Disaster Recovery
        var discoveryService = new RemoteCatalogDiscoveryService();
        var manifests = await discoveryService.DiscoverRemoteManifestsAsync(telegramStorage, masterKey);
        manifests.Should().NotBeEmpty();
        var latestManifest = manifests.OrderByDescending(m => m.SnapshotNumber).First();

        // 6. Run Restore under Chaos
        var restoreEngine = new RestoreOrchestrator();
        var restoreOptions = new RestoreOptions(
            DestinationRootPath: _restoreDir,
            ConflictResolution: RestoreConflictResolution.Overwrite,
            RestoreTimestamps: true
        );

        await restoreEngine.RestoreAllAsync(
            latestManifest,
            masterKey,
            telegramStorage,
            restoreOptions
        );

        // 7. Verify Every Single File Bit-for-Bit
        foreach (var (relPath, originalInfo) in generatedFiles)
        {
            var restoredFilePath = RestoreOrchestrator.ValidateAndResolveTargetPath(_restoreDir, relPath);
            File.Exists(restoredFilePath).Should().BeTrue($"Restored file '{relPath}' must exist at '{restoredFilePath}'");

            var restoredBytes = await File.ReadAllBytesAsync(restoredFilePath);
            restoredBytes.Length.Should().Be((int)originalInfo.SizeBytes, $"Restored file size for '{relPath}' must match original");

            var restoredSha256 = Convert.ToHexString(SHA256.HashData(restoredBytes)).ToLowerInvariant();
            restoredSha256.Should().Be(originalInfo.Sha256Hex, $"Restored SHA-256 for '{relPath}' must match original bit-for-bit");
        }

        await catalog.DisposeAsync();
    }
}
