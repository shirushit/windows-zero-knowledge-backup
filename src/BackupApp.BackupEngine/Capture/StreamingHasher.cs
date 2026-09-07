using System.Security.Cryptography;

namespace BackupApp.BackupEngine.Capture;

public static class StreamingHasher
{
    public const int DefaultBufferSize = 64 * 1024; // 64 KB bounded buffer

    public static async Task<string> ComputeHashSha256Async(
        Stream stream,
        int bufferSize = DefaultBufferSize,
        IProgress<long>? bytesReadProgress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[bufferSize];
        int read;
        long totalRead = 0;

        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
        {
            sha256.AppendData(buffer, 0, read);
            totalRead += read;
            bytesReadProgress?.Report(totalRead);
        }

        var hashBytes = sha256.GetHashAndReset();
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
