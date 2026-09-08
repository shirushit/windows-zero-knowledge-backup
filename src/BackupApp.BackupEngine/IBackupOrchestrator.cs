using System.Security.Cryptography;
using System.Text;
using BackupApp.BackupEngine.Capture;
using BackupApp.BackupEngine.ChangeDetection;
using BackupApp.BackupEngine.Scanner;
using BackupApp.Catalog;
using BackupApp.Crypto;
using BackupApp.Domain;
using BackupApp.Storage;

namespace BackupApp.BackupEngine;

public record BackupProgressReport(
    int TotalFilesScanned,
    int FilesProcessed,
    long TotalBytesScanned,
    long BytesProcessed,
    string? CurrentFileName
);

public interface IBackupOrchestrator
{
    Task<SnapshotId> RunBackupAsync(
        BackupSetId backupSetId,
        MasterKey masterKey,
        IStorageProvider storageProvider,
        ICatalogRepository catalogRepository,
        BackupEngineOptions? options = null,
        IProgress<BackupProgressReport>? progress = null,
        CancellationToken cancellationToken = default
    );
}

public sealed class BackupOrchestrator : IBackupOrchestrator
{
    private readonly IFileDiscoveryScanner _scanner;
    private readonly IStableFileCaptureService _capture;
    private readonly IChangeDetectionService _changeDetection;
    private readonly ICryptoService _crypto;
    private readonly IManifestCryptoService _manifestCrypto;

    public BackupOrchestrator(
        IFileDiscoveryScanner? scanner = null,
        IStableFileCaptureService? capture = null,
        IChangeDetectionService? changeDetection = null,
        ICryptoService? crypto = null,
        IManifestCryptoService? manifestCrypto = null)
    {
        _scanner = scanner ?? new FileDiscoveryScanner();
        _capture = capture ?? new StableFileCaptureService();
        _changeDetection = changeDetection ?? new ChangeDetectionService();
        _crypto = crypto ?? new CryptoService();
        _manifestCrypto = manifestCrypto ?? new ManifestCryptoService(_crypto);
    }

    public async Task<SnapshotId> RunBackupAsync(
        BackupSetId backupSetId,
        MasterKey masterKey,
        IStorageProvider storageProvider,
        ICatalogRepository catalogRepository,
        BackupEngineOptions? options = null,
        IProgress<BackupProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(masterKey);
        ArgumentNullException.ThrowIfNull(storageProvider);
        ArgumentNullException.ThrowIfNull(catalogRepository);

        options ??= BackupEngineOptions.Default;

        // 1. Fetch BackupSet
        var backupSet = await catalogRepository.GetBackupSetAsync(backupSetId, cancellationToken).ConfigureAwait(false)
            ?? throw new ArgumentException($"Backup set '{backupSetId.Value}' does not exist in catalog.", nameof(backupSetId));

        // 2. Discover files across all source roots
        var filter = new ExclusionFilter(backupSet.ExcludedPatterns);
        var discoveredFiles = new List<DiscoveredFile>();

        foreach (var sourceRoot in backupSet.IncludedRoots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(sourceRoot))
            {
                await foreach (var file in _scanner.DiscoverFilesAsync(sourceRoot, filter, onScanWarning: null, cancellationToken: cancellationToken).ConfigureAwait(false))
                {
                    discoveredFiles.Add(file);
                }
            }
        }

        long totalScannedBytes = discoveredFiles.Sum(f => f.SizeBytes);

        // 3. Change Detection against latest committed snapshot
        var latestSnapshot = await catalogRepository.GetLatestCommittedSnapshotAsync(backupSetId, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<FileVersion> previousFiles = latestSnapshot != null
            ? await catalogRepository.GetSnapshotFilesAsync(latestSnapshot.Id, cancellationToken).ConfigureAwait(false)
            : [];

        var changes = await _changeDetection.DetectChangesAsync(discoveredFiles, previousFiles, _capture, cancellationToken).ConfigureAwait(false);

        // 4. Initialize Monotonic Snapshot
        long nextSnapshotNumber = latestSnapshot != null ? latestSnapshot.SnapshotNumber + 1 : 1;
        var snapshot = SnapshotStateMachine.StartSnapshot(backupSetId, nextSnapshotNumber);
        await catalogRepository.CreateSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);

        // 5. Initialize Resumable Job
        var jobId = JobId.New();
        var jobType = latestSnapshot == null ? JobType.InitialBackup : JobType.IncrementalBackup;
        var job = new BackupJob(
            Id: jobId,
            SnapshotId: snapshot.Id,
            Type: jobType,
            Status: JobStatus.Running,
            StartedAtUtc: DateTimeOffset.UtcNow,
            CompletedAtUtc: null,
            TotalFiles: discoveredFiles.Count,
            ProcessedFiles: 0,
            TotalBytes: totalScannedBytes,
            ProcessedBytes: 0,
            ErrorSummary: null
        );
        await catalogRepository.SaveBackupJobAsync(job, cancellationToken).ConfigureAwait(false);

        var entriesToSave = new List<FileEntry>();
        var versionsToSave = new List<FileVersion>();
        var manifestItems = new List<SnapshotManifestItem>();

        int filesProcessed = 0;
        long bytesProcessed = 0;

        var contentKey = masterKey.DeriveContentKey();

        try
        {
            // 6. Process each change
            foreach (var change in changes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (change.ChangeType == FileChangeType.Deleted)
                {
                    // Deleted files are omitted from the new snapshot version list
                    continue;
                }

                if (change.ChangeType == FileChangeType.Unchanged && change.PreviousVersion != null)
                {
                    // Fast path for unchanged files: reuse existing FileEntry and Chunks (zero encryption or upload)
                    var prev = change.PreviousVersion;
                    var ver = new FileVersion(
                        Id: FileVersionId.New(),
                        FileEntryId: prev.FileEntryId,
                        SnapshotId: snapshot.Id,
                        Path: prev.Path,
                        SizeBytes: prev.SizeBytes,
                        CreatedAtUtc: prev.CreatedAtUtc,
                        ModifiedUtc: prev.ModifiedUtc,
                        ContentHashSha256: prev.ContentHashSha256,
                        Attributes: prev.Attributes,
                        ChunkRefs: prev.ChunkRefs
                    );

                    versionsToSave.Add(ver);
                    manifestItems.Add(new SnapshotManifestItem(
                        Path: ver.Path.Value,
                        SizeBytes: ver.SizeBytes,
                        ContentHashSha256: ver.ContentHashSha256,
                        Attributes: (long)ver.Attributes,
                        CreatedAtUtc: ver.CreatedAtUtc,
                        ModifiedUtc: ver.ModifiedUtc,
                        ChunkIds: ver.ChunkRefs.Select(c => c.Value).ToList()
                    ));

                    filesProcessed++;
                    bytesProcessed += ver.SizeBytes;
                    progress?.Report(new BackupProgressReport(discoveredFiles.Count, filesProcessed, totalScannedBytes, bytesProcessed, ver.Path.Value));
                    continue;
                }

                if (change.ChangeType == FileChangeType.Renamed && change.PreviousVersion != null)
                {
                    // Renamed file: reuse existing chunks, update canonical path
                    var prev = change.PreviousVersion;
                    var fileEntryId = change.ReusedFileEntryId ?? prev.FileEntryId;

                    var ver = new FileVersion(
                        Id: FileVersionId.New(),
                        FileEntryId: fileEntryId,
                        SnapshotId: snapshot.Id,
                        Path: change.Path,
                        SizeBytes: prev.SizeBytes,
                        CreatedAtUtc: prev.CreatedAtUtc,
                        ModifiedUtc: change.Discovered?.ModifiedUtc ?? prev.ModifiedUtc,
                        ContentHashSha256: prev.ContentHashSha256,
                        Attributes: change.Discovered?.Attributes ?? prev.Attributes,
                        ChunkRefs: prev.ChunkRefs
                    );

                    versionsToSave.Add(ver);
                    manifestItems.Add(new SnapshotManifestItem(
                        Path: ver.Path.Value,
                        SizeBytes: ver.SizeBytes,
                        ContentHashSha256: ver.ContentHashSha256,
                        Attributes: (long)ver.Attributes,
                        CreatedAtUtc: ver.CreatedAtUtc,
                        ModifiedUtc: ver.ModifiedUtc,
                        ChunkIds: ver.ChunkRefs.Select(c => c.Value).ToList()
                    ));

                    filesProcessed++;
                    bytesProcessed += ver.SizeBytes;
                    progress?.Report(new BackupProgressReport(discoveredFiles.Count, filesProcessed, totalScannedBytes, bytesProcessed, ver.Path.Value));
                    continue;
                }

                // For New or Modified files: capture, chunk, encrypt, and upload
                var discovered = change.Discovered!;
                var captureResult = await _capture.CaptureFileAsync(
                    discovered.AbsolutePath,
                    options.ChunkSizeBytes,
                    cancellationToken: cancellationToken
                ).ConfigureAwait(false);

                if (captureResult.Status == StableCaptureStatus.Locked)
                {
                    if (options.SkipLockedFiles)
                    {
                        // Skip locked file gracefully without aborting entire job
                        continue;
                    }
                    throw new IOException($"File '{discovered.AbsolutePath}' is locked by another process.");
                }

                if (captureResult.Status != StableCaptureStatus.Success)
                {
                    continue;
                }

                var chunkList = new List<StoredChunk>();
                var chunkIds = new List<ObjectId>();

                FileStream fileStream;
                try
                {
                    fileStream = new FileStream(
                        discovered.AbsolutePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete,
                        bufferSize: 64 * 1024,
                        useAsync: true);
                }
                catch (IOException) when (options.SkipLockedFiles)
                {
                    continue;
                }
                catch (UnauthorizedAccessException) when (options.SkipLockedFiles)
                {
                    continue;
                }

                await using (fileStream.ConfigureAwait(false))
                {
                    foreach (var chunkDesc in captureResult.Chunks)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var chunkLen = (int)chunkDesc.SizeBytes;
                        var chunkData = new byte[chunkLen];
                        int chunkBytesRead = 0;
                        while (chunkBytesRead < chunkLen)
                        {
                            int bytesRead = await fileStream.ReadAsync(chunkData.AsMemory(chunkBytesRead, chunkLen - chunkBytesRead), cancellationToken).ConfigureAwait(false);
                            if (bytesRead == 0)
                            {
                                break;
                            }
                            chunkBytesRead += bytesRead;
                        }

                        // Check deduplication in catalog / remote storage
                        var existingRef = await catalogRepository.GetRemoteObjectRefAsync(chunkDesc.Id, storageProvider.ProviderId, cancellationToken).ConfigureAwait(false);

                        if (existingRef == null)
                        {
                            // Encrypt chunk using ContentKey
                            var aad = Encoding.UTF8.GetBytes($"chunk:{chunkDesc.Id.Value}");
                            var envelope = _crypto.Encrypt(chunkData, contentKey, aad);
                            var envelopeBytes = envelope.ToBytes();

                            using var uploadStream = new MemoryStream(envelopeBytes);
                            var descriptor = await storageProvider.PutObjectAsync(
                                chunkDesc.Id,
                                uploadStream,
                                progress: null,
                                cancellationToken: cancellationToken
                            ).ConfigureAwait(false);

                            var remoteRef = new RemoteObjectRef(
                                ObjectId: chunkDesc.Id,
                                ProviderId: storageProvider.ProviderId,
                                RemoteIdentifier: descriptor.ProviderReference ?? chunkDesc.Id.Value,
                                UploadedAtUtc: DateTimeOffset.UtcNow,
                                IsVerified: true
                            );
                            await catalogRepository.SaveRemoteObjectRefAsync(remoteRef, cancellationToken).ConfigureAwait(false);

                            if (options.ThrottleDelayMs > 0)
                            {
                                await Task.Delay(options.ThrottleDelayMs, cancellationToken).ConfigureAwait(false);
                            }
                        }

                        var storedChunk = new StoredChunk(
                            Id: chunkDesc.Id,
                            PlaintextSizeBytes: chunkDesc.SizeBytes,
                            PlaintextSha256: chunkDesc.PlaintextSha256,
                            EncryptedSizeBytes: chunkDesc.SizeBytes + EncryptedEnvelope.HeaderLength + 16,
                            EncryptedSha256: chunkDesc.PlaintextSha256
                        );
                        chunkList.Add(storedChunk);
                        chunkIds.Add(chunkDesc.Id);
                    }
                }

                await catalogRepository.SaveChunksAsync(chunkList, cancellationToken).ConfigureAwait(false);

                var entryId = change.ReusedFileEntryId ?? FileEntryId.New();
                if (change.ReusedFileEntryId == null)
                {
                    entriesToSave.Add(new FileEntry(entryId, backupSetId, snapshot.Id, DateTimeOffset.UtcNow));
                }

                var newVersion = new FileVersion(
                    Id: FileVersionId.New(),
                    FileEntryId: entryId,
                    SnapshotId: snapshot.Id,
                    Path: change.Path,
                    SizeBytes: discovered.SizeBytes,
                    CreatedAtUtc: discovered.CreatedAtUtc,
                    ModifiedUtc: discovered.ModifiedUtc,
                    ContentHashSha256: captureResult.ContentHashSha256!,
                    Attributes: discovered.Attributes,
                    ChunkRefs: chunkIds
                );

                versionsToSave.Add(newVersion);
                manifestItems.Add(new SnapshotManifestItem(
                    Path: newVersion.Path.Value,
                    SizeBytes: newVersion.SizeBytes,
                    ContentHashSha256: newVersion.ContentHashSha256,
                    Attributes: (long)newVersion.Attributes,
                    CreatedAtUtc: newVersion.CreatedAtUtc,
                    ModifiedUtc: newVersion.ModifiedUtc,
                    ChunkIds: newVersion.ChunkRefs.Select(c => c.Value).ToList()
                ));

                filesProcessed++;
                bytesProcessed += discovered.SizeBytes;

                await catalogRepository.UpdateBackupJobProgressAsync(jobId, filesProcessed, bytesProcessed, cancellationToken).ConfigureAwait(false);
                progress?.Report(new BackupProgressReport(discoveredFiles.Count, filesProcessed, totalScannedBytes, bytesProcessed, newVersion.Path.Value));
            }

            // 7. Save file entries and versions to local catalog atomically
            await catalogRepository.SaveFileEntriesAndVersionsAsync(entriesToSave, versionsToSave, cancellationToken).ConfigureAwait(false);

            // 8. Create, Encrypt, and Upload Snapshot Manifest
            var manifest = new SnapshotManifest(
                BackupSetId: backupSetId.ToString(),
                SnapshotId: snapshot.Id.ToString(),
                SnapshotNumber: nextSnapshotNumber,
                CreatedAtUtc: DateTimeOffset.UtcNow,
                TotalFiles: versionsToSave.Count,
                TotalBytes: bytesProcessed,
                Items: manifestItems
            );

            var manifestBytes = _manifestCrypto.CreateManifestPackageBytes(manifest, masterKey);
            var manifestObjectId = ObjectId.FromHex(Convert.ToHexString(SHA256.HashData(manifestBytes)));

            using var manifestStream = new MemoryStream(manifestBytes);
            var manifestDescriptor = await storageProvider.PutObjectAsync(manifestObjectId, manifestStream, null, isCatalogAnchor: true, cancellationToken).ConfigureAwait(false);

            await catalogRepository.SaveRemoteObjectRefAsync(new RemoteObjectRef(
                ObjectId: manifestObjectId,
                ProviderId: storageProvider.ProviderId,
                RemoteIdentifier: manifestDescriptor.ProviderReference ?? manifestObjectId.Value,
                UploadedAtUtc: DateTimeOffset.UtcNow,
                IsVerified: true
            ), cancellationToken).ConfigureAwait(false);

            // 9. Atomic Snapshot Commit only after required remote success
            await catalogRepository.CommitSnapshotAsync(snapshot.Id, versionsToSave.Count, bytesProcessed, cancellationToken).ConfigureAwait(false);

            // 10. Mark Job as Completed
            await catalogRepository.CompleteBackupJobAsync(jobId, JobStatus.Completed, cancellationToken: cancellationToken).ConfigureAwait(false);

            return snapshot.Id;
        }
        catch (Exception ex)
        {
            await catalogRepository.MarkSnapshotFailedAsync(snapshot.Id, ex.Message, CancellationToken.None).ConfigureAwait(false);

            var jobStatus = ex is OperationCanceledException ? JobStatus.Cancelled : JobStatus.Failed;
            await catalogRepository.CompleteBackupJobAsync(jobId, jobStatus, ex.Message, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(contentKey);
        }
    }
}
