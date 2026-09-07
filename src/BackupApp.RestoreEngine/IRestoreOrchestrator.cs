using System.Security;
using System.Security.Cryptography;
using System.Text;
using BackupApp.Crypto;
using BackupApp.Domain;
using BackupApp.Storage;

namespace BackupApp.RestoreEngine;

public enum RestoreConflictResolution
{
    Overwrite,
    RenameExisting,
    Skip
}

public sealed record RestoreOptions(
    string DestinationRootPath,
    RestoreConflictResolution ConflictResolution = RestoreConflictResolution.Overwrite,
    bool RestoreTimestamps = true,
    bool RestoreAttributes = false
);

public sealed record RestoreProgressReport(
    int TotalFiles,
    int FilesProcessed,
    long TotalBytes,
    long BytesProcessed,
    string? CurrentFileName
);

public interface IRestoreOrchestrator
{
    Task RestoreFileAsync(
        SnapshotManifest manifest,
        string relativeFilePath,
        MasterKey masterKey,
        IStorageProvider storageProvider,
        RestoreOptions options,
        CancellationToken cancellationToken = default
    );

    Task RestoreFolderAsync(
        SnapshotManifest manifest,
        string folderPrefix,
        MasterKey masterKey,
        IStorageProvider storageProvider,
        RestoreOptions options,
        IProgress<RestoreProgressReport>? progress = null,
        CancellationToken cancellationToken = default
    );

    Task RestoreAllAsync(
        SnapshotManifest manifest,
        MasterKey masterKey,
        IStorageProvider storageProvider,
        RestoreOptions options,
        IProgress<RestoreProgressReport>? progress = null,
        CancellationToken cancellationToken = default
    );
}

public sealed class RestoreOrchestrator : IRestoreOrchestrator
{
    private static readonly HashSet<string> ReservedDevices = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private readonly ICryptoService _crypto;
    private readonly StorageRetryPolicy _retryPolicy;

    public RestoreOrchestrator(
        ICryptoService? crypto = null,
        StorageRetryPolicy? retryPolicy = null)
    {
        _crypto = crypto ?? new CryptoService();
        _retryPolicy = retryPolicy ?? new StorageRetryPolicy();
    }

    public async Task RestoreFileAsync(
        SnapshotManifest manifest,
        string relativeFilePath,
        MasterKey masterKey,
        IStorageProvider storageProvider,
        RestoreOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(relativeFilePath);
        ArgumentNullException.ThrowIfNull(masterKey);
        ArgumentNullException.ThrowIfNull(storageProvider);
        ArgumentNullException.ThrowIfNull(options);

        var canonical = CanonicalPath.From(relativeFilePath);
        var item = manifest.Items.FirstOrDefault(i => CanonicalPath.From(i.Path) == canonical)
            ?? throw new FileNotFoundException($"File '{relativeFilePath}' not found in snapshot manifest.");

        var contentKey = masterKey.DeriveContentKey();
        try
        {
            await RestoreItemInternalAsync(item, contentKey, storageProvider, options, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(contentKey);
        }
    }

    public async Task RestoreFolderAsync(
        SnapshotManifest manifest,
        string folderPrefix,
        MasterKey masterKey,
        IStorageProvider storageProvider,
        RestoreOptions options,
        IProgress<RestoreProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(folderPrefix);
        ArgumentNullException.ThrowIfNull(masterKey);
        ArgumentNullException.ThrowIfNull(storageProvider);
        ArgumentNullException.ThrowIfNull(options);

        var normalizedPrefix = folderPrefix.Replace('\\', '/').Trim('/') + "/";
        var matchingItems = manifest.Items
            .Where(i => i.Path.Replace('\\', '/').StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var contentKey = masterKey.DeriveContentKey();
        try
        {
            await RestoreItemsBatchAsync(matchingItems, contentKey, storageProvider, options, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(contentKey);
        }
    }

    public async Task RestoreAllAsync(
        SnapshotManifest manifest,
        MasterKey masterKey,
        IStorageProvider storageProvider,
        RestoreOptions options,
        IProgress<RestoreProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(masterKey);
        ArgumentNullException.ThrowIfNull(storageProvider);
        ArgumentNullException.ThrowIfNull(options);

        var contentKey = masterKey.DeriveContentKey();
        try
        {
            await RestoreItemsBatchAsync(manifest.Items, contentKey, storageProvider, options, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(contentKey);
        }
    }

    private async Task RestoreItemsBatchAsync(
        IReadOnlyList<SnapshotManifestItem> items,
        byte[] contentKey,
        IStorageProvider storageProvider,
        RestoreOptions options,
        IProgress<RestoreProgressReport>? progress,
        CancellationToken cancellationToken)
    {
        long totalBytes = items.Sum(i => i.SizeBytes);
        int filesProcessed = 0;
        long bytesProcessed = 0;

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await RestoreItemInternalAsync(item, contentKey, storageProvider, options, cancellationToken).ConfigureAwait(false);

            filesProcessed++;
            bytesProcessed += item.SizeBytes;
            progress?.Report(new RestoreProgressReport(items.Count, filesProcessed, totalBytes, bytesProcessed, item.Path));
        }
    }

    private async Task RestoreItemInternalAsync(
        SnapshotManifestItem item,
        byte[] contentKey,
        IStorageProvider storageProvider,
        RestoreOptions options,
        CancellationToken cancellationToken)
    {
        var targetPath = ValidateAndResolveTargetPath(options.DestinationRootPath, item.Path);

        if (File.Exists(targetPath))
        {
            if (options.ConflictResolution == RestoreConflictResolution.Skip)
            {
                return;
            }

            if (options.ConflictResolution == RestoreConflictResolution.RenameExisting)
            {
                var backupPath = $"{targetPath}.conflict_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
                File.Move(targetPath, backupPath);
            }
        }

        var targetDir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        var tempPath = targetPath + $".tmp_{Guid.NewGuid():N}";
        try
        {
            await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                foreach (var chunkIdStr in item.ChunkIds)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var chunkId = ObjectId.FromHex(chunkIdStr);

                    await using var chunkStream = await _retryPolicy.ExecuteWithRetryAsync(
                        ct => storageProvider.GetObjectAsync(chunkId, ct),
                        cancellationToken: cancellationToken
                    ).ConfigureAwait(false);

                    using var ms = new MemoryStream();
                    await chunkStream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
                    var chunkBytes = ms.ToArray();

                    var aad = Encoding.UTF8.GetBytes($"chunk:{chunkId.Value}");
                    var plaintext = _crypto.Decrypt(chunkBytes, contentKey, aad);

                    await fs.WriteAsync(plaintext, cancellationToken).ConfigureAwait(false);
                }
            }

            // Verify content hash integrity
            string computedHash;
            await using (var verifyStream = File.OpenRead(tempPath))
            {
                var hashBytes = await SHA256.HashDataAsync(verifyStream, cancellationToken).ConfigureAwait(false);
                computedHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
            }

            if (!string.Equals(computedHash, item.ContentHashSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new CryptographicException(
                    $"Integrity verification failed for restored file '{item.Path}'. Expected hash '{item.ContentHashSha256}', but computed '{computedHash}'."
                );
            }

            // Atomic replacement to final path
            File.Move(tempPath, targetPath, overwrite: true);

            // Restore metadata
            if (options.RestoreTimestamps)
            {
                File.SetCreationTimeUtc(targetPath, item.CreatedAtUtc.UtcDateTime);
                File.SetLastWriteTimeUtc(targetPath, item.ModifiedUtc.UtcDateTime);
            }

            if (options.RestoreAttributes && item.Attributes != 0)
            {
                try
                {
                    File.SetAttributes(targetPath, (FileAttributes)item.Attributes);
                }
                catch
                {
                    // Best-effort for system-level attribute flags
                }
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // Best effort cleanup of temp files
                }
            }
        }
    }

    public static string ValidateAndResolveTargetPath(string destinationRoot, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var canonical = CanonicalPath.From(relativePath);
        var segments = canonical.Value.Split('/');

        foreach (var segment in segments)
        {
            if (segment == ".." || segment == ".")
            {
                throw new SecurityException($"Path traversal token '{segment}' detected in relative path '{relativePath}'.");
            }

            var dotIdx = segment.IndexOf('.', StringComparison.Ordinal);
            var baseName = dotIdx > 0 ? segment[..dotIdx] : segment;
            if (ReservedDevices.Contains(baseName))
            {
                throw new SecurityException($"Reserved Windows device name '{segment}' detected in relative path '{relativePath}'.");
            }
        }

        var fullDest = Path.GetFullPath(destinationRoot);
        var combined = Path.Combine(fullDest, canonical.Value.Replace('/', Path.DirectorySeparatorChar));
        var fullTarget = Path.GetFullPath(combined);

        var normalizedDest = fullDest.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullTarget.StartsWith(normalizedDest, StringComparison.OrdinalIgnoreCase))
        {
            throw new SecurityException($"Target path '{fullTarget}' escapes destination root '{fullDest}'.");
        }

        return fullTarget;
    }
}
