using System.Security.Cryptography;
using BackupApp.Domain;

namespace BackupApp.BackupEngine.Capture;

public enum StableCaptureStatus
{
    Success,
    Locked,
    ChangedDuringRead,
    AccessDenied,
    NotFound,
    UnexpectedError
}

public record FileChunkDescriptor(
    ObjectId Id,
    int Position,
    long SizeBytes,
    string PlaintextSha256
);

public record StableCaptureResult(
    StableCaptureStatus Status,
    long SizeBytes,
    string? ContentHashSha256,
    IReadOnlyList<FileChunkDescriptor> Chunks,
    string? ErrorMessage = null
)
{
    public static StableCaptureResult Successful(long size, string hash, IReadOnlyList<FileChunkDescriptor> chunks) =>
        new(StableCaptureStatus.Success, size, hash, chunks);

    public static StableCaptureResult Failure(StableCaptureStatus status, string error) =>
        new(status, 0, null, [], error);
}

public interface IStableFileCaptureService
{
    Task<StableCaptureResult> CaptureFileAsync(
        string absoluteFilePath,
        long maxChunkSizeBytes = 8 * 1024 * 1024,
        int maxRetries = 2,
        CancellationToken cancellationToken = default
    );
}

public sealed class StableFileCaptureService : IStableFileCaptureService
{
    public const int ReadBufferSize = 64 * 1024; // 64 KB

    public async Task<StableCaptureResult> CaptureFileAsync(
        string absoluteFilePath,
        long maxChunkSizeBytes = 8 * 1024 * 1024,
        int maxRetries = 2,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteFilePath);

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var initialInfo = new FileInfo(absoluteFilePath);
            if (!initialInfo.Exists)
            {
                return StableCaptureResult.Failure(StableCaptureStatus.NotFound, $"File not found: {absoluteFilePath}");
            }

            var initialLength = initialInfo.Length;
            var initialModified = initialInfo.LastWriteTimeUtc;

            FileStream? fileStream = null;
            try
            {
                // Open with sharing enabled to coexist with other applications
                fileStream = new FileStream(
                    absoluteFilePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    ReadBufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan
                );
            }
            catch (UnauthorizedAccessException ex)
            {
                return StableCaptureResult.Failure(StableCaptureStatus.AccessDenied, ex.Message);
            }
            catch (IOException ex) when (IsSharingViolation(ex))
            {
                if (attempt < maxRetries)
                {
                    await Task.Delay(100 * (attempt + 1), cancellationToken).ConfigureAwait(false);
                    continue;
                }
                return StableCaptureResult.Failure(StableCaptureStatus.Locked, $"File is locked by another process: {ex.Message}");
            }
            catch (Exception ex)
            {
                return StableCaptureResult.Failure(StableCaptureStatus.UnexpectedError, ex.Message);
            }

            await using (fileStream.ConfigureAwait(false))
            {
                using var overallHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var chunks = new List<FileChunkDescriptor>();
                var buffer = new byte[ReadBufferSize];

                var chunkIndex = 0;
                long totalBytesRead = 0;

                while (totalBytesRead < initialLength || fileStream.Position < fileStream.Length)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    using var chunkHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    long chunkBytesRead = 0;

                    while (chunkBytesRead < maxChunkSizeBytes)
                    {
                        var toRead = (int)Math.Min(buffer.Length, maxChunkSizeBytes - chunkBytesRead);
                        var bytesRead = await fileStream.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken).ConfigureAwait(false);

                        if (bytesRead == 0)
                        {
                            break;
                        }

                        chunkHasher.AppendData(buffer, 0, bytesRead);
                        overallHasher.AppendData(buffer, 0, bytesRead);

                        chunkBytesRead += bytesRead;
                        totalBytesRead += bytesRead;
                    }

                    if (chunkBytesRead == 0)
                    {
                        break;
                    }

                    var chunkHash = Convert.ToHexString(chunkHasher.GetHashAndReset()).ToLowerInvariant();
                    var chunkId = ObjectId.FromHex(chunkHash);

                    chunks.Add(new FileChunkDescriptor(chunkId, chunkIndex++, chunkBytesRead, chunkHash));
                }

                // If empty file, emit single empty chunk
                if (chunks.Count == 0)
                {
                    var emptyHash = Convert.ToHexString(SHA256.HashData([])).ToLowerInvariant();
                    chunks.Add(new FileChunkDescriptor(ObjectId.FromHex(emptyHash), 0, 0, emptyHash));
                }

                // Verify file stability after complete read
                var finalInfo = new FileInfo(absoluteFilePath);
                if (!finalInfo.Exists || finalInfo.Length != initialLength || finalInfo.LastWriteTimeUtc != initialModified)
                {
                    if (attempt < maxRetries)
                    {
                        await Task.Delay(100 * (attempt + 1), cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    return StableCaptureResult.Failure(
                        StableCaptureStatus.ChangedDuringRead,
                        $"File '{absoluteFilePath}' changed during capture (initial length: {initialLength}, final length: {finalInfo.Length})."
                    );
                }

                var overallHash = Convert.ToHexString(overallHasher.GetHashAndReset()).ToLowerInvariant();
                return StableCaptureResult.Successful(totalBytesRead, overallHash, chunks);
            }
        }

        return StableCaptureResult.Failure(StableCaptureStatus.ChangedDuringRead, "File remained unstable after retries.");
    }

    private static bool IsSharingViolation(IOException ex)
    {
        // Win32 ERROR_SHARING_VIOLATION = 32 (0x20)
        // HRESULT = 0x80070020
        return (ex.HResult & 0xFFFF) == 32;
    }
}
