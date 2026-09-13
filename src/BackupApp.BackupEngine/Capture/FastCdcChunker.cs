using System.Security.Cryptography;
using BackupApp.Domain;

namespace BackupApp.BackupEngine.Capture;

/// <summary>
/// High-performance Content-Defined Chunking using the FastCDC algorithm
/// (USENIX ATC '16: "FastCDC: a Fast and Efficient Content-Defined Chunking Approach for Data Deduplication").
/// Features Gear hashing, fast skipping below MinSize, and normalized two-mask chunking.
/// </summary>
public sealed class FastCdcChunker
{
    public const int DefaultMinChunkSize = 4 * 1024 * 1024;  // 4 MB
    public const int DefaultAvgChunkSize = 16 * 1024 * 1024; // 16 MB
    public const int DefaultMaxChunkSize = 32 * 1024 * 1024; // 32 MB

    private readonly int _minChunkSize;
    private readonly int _avgChunkSize;
    private readonly int _maxChunkSize;
    private readonly ulong _maskS; // Stricter mask (bits + 1)
    private readonly ulong _maskL; // Relaxed mask (bits - 1)

    // Standard Gear Matrix with 256 pseudo-random 64-bit integers generated deterministically
    private static readonly ulong[] GearMatrix = GenerateGearMatrix();

    public int MinChunkSize => _minChunkSize;
    public int AvgChunkSize => _avgChunkSize;
    public int MaxChunkSize => _maxChunkSize;

    public FastCdcChunker(
        int minChunkSize = DefaultMinChunkSize,
        int avgChunkSize = DefaultAvgChunkSize,
        int maxChunkSize = DefaultMaxChunkSize)
    {
        if (minChunkSize <= 0) throw new ArgumentOutOfRangeException(nameof(minChunkSize), "Min chunk size must be positive.");
        if (avgChunkSize <= minChunkSize) throw new ArgumentOutOfRangeException(nameof(avgChunkSize), "Avg chunk size must be greater than min chunk size.");
        if (maxChunkSize <= avgChunkSize) throw new ArgumentOutOfRangeException(nameof(maxChunkSize), "Max chunk size must be greater than avg chunk size.");

        _minChunkSize = minChunkSize;
        _avgChunkSize = avgChunkSize;
        _maxChunkSize = maxChunkSize;

        var bits = (int)Math.Round(Math.Log2(avgChunkSize));
        _maskS = (1UL << (bits + 1)) - 1UL;
        _maskL = (1UL << (bits - 1)) - 1UL;
    }

    /// <summary>
    /// Finds the content-defined boundary cut-point in the given byte span.
    /// Returns the length of the chunk starting from offset 0.
    /// </summary>
    public int FindCutPoint(ReadOnlySpan<byte> data)
    {
        if (data.Length <= _minChunkSize)
        {
            return data.Length;
        }

        var limit = Math.Min(data.Length, _maxChunkSize);
        var normalCutLimit = Math.Min(limit, _avgChunkSize);

        ulong fp = 0;
        int i = _minChunkSize;

        // Phase 1: from minChunkSize to avgChunkSize using stricter maskS
        for (; i < normalCutLimit; i++)
        {
            fp = (fp << 1) + GearMatrix[data[i]];
            if ((fp & _maskS) == 0)
            {
                return i + 1;
            }
        }

        // Phase 2: from avgChunkSize to maxChunkSize using relaxed maskL
        for (; i < limit; i++)
        {
            fp = (fp << 1) + GearMatrix[data[i]];
            if ((fp & _maskL) == 0)
            {
                return i + 1;
            }
        }

        // Phase 3: reached maxChunkSize
        return limit;
    }

    /// <summary>
    /// Chunks a stream using FastCDC without loading the entire stream into memory.
    /// Emits a list of FileChunkDescriptors and computes the overall stream SHA-256 hash.
    /// </summary>
    public async Task<(IReadOnlyList<FileChunkDescriptor> Chunks, string OverallSha256, long TotalBytes)> ChunkStreamAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var overallHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var chunkDescriptors = new List<FileChunkDescriptor>();

        var windowBuffer = new byte[_maxChunkSize * 2];
        int bufferOffset = 0;
        int bufferCount = 0;
        int chunkPosition = 0;
        long totalBytesStreamed = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Read more data into buffer if needed
            if (bufferCount < _maxChunkSize && stream.CanRead)
            {
                // Compact buffer if needed
                if (bufferOffset > 0 && bufferCount > 0)
                {
                    Buffer.BlockCopy(windowBuffer, bufferOffset, windowBuffer, 0, bufferCount);
                    bufferOffset = 0;
                }
                else if (bufferCount == 0)
                {
                    bufferOffset = 0;
                }

                var spaceRemaining = windowBuffer.Length - (bufferOffset + bufferCount);
                if (spaceRemaining > 0)
                {
                    var read = await stream.ReadAsync(
                        windowBuffer.AsMemory(bufferOffset + bufferCount, spaceRemaining),
                        cancellationToken
                    ).ConfigureAwait(false);

                    if (read > 0)
                    {
                        bufferCount += read;
                    }
                }
            }

            if (bufferCount == 0)
            {
                break;
            }

            // Find FastCDC cut point and hash in current buffer (synchronous helper to avoid ref structs across await)
            var (chunkLen, chunkId, chunkHashHex) = ProcessChunk(windowBuffer, bufferOffset, bufferCount, overallHasher);

            chunkDescriptors.Add(new FileChunkDescriptor(
                chunkId,
                chunkPosition++,
                chunkLen,
                chunkHashHex
            ));

            totalBytesStreamed += chunkLen;
            bufferOffset += chunkLen;
            bufferCount -= chunkLen;
        }

        // Handle empty file: single empty chunk
        if (chunkDescriptors.Count == 0)
        {
            var emptyHash = Convert.ToHexString(SHA256.HashData([])).ToLowerInvariant();
            chunkDescriptors.Add(new FileChunkDescriptor(ObjectId.FromHex(emptyHash), 0, 0, emptyHash));
        }

        var overallHashHex = Convert.ToHexString(overallHasher.GetHashAndReset()).ToLowerInvariant();
        return (chunkDescriptors, overallHashHex, totalBytesStreamed);
    }

    private (int ChunkLen, ObjectId ChunkId, string ChunkHashHex) ProcessChunk(
        byte[] buffer, int offset, int count, IncrementalHash overallHasher)
    {
        var currentSpan = buffer.AsSpan(offset, count);
        var chunkLen = FindCutPoint(currentSpan);
        var chunkData = currentSpan[..chunkLen];
        var chunkHashBytes = SHA256.HashData(chunkData);
        var chunkHashHex = Convert.ToHexString(chunkHashBytes).ToLowerInvariant();
        var chunkId = ObjectId.FromHex(chunkHashHex);
        overallHasher.AppendData(chunkData);
        return (chunkLen, chunkId, chunkHashHex);
    }

    private static ulong[] GenerateGearMatrix()
    {
        var matrix = new ulong[256];
        // Deterministic generation from SHA256 of index
        for (int i = 0; i < 256; i++)
        {
            var hash = SHA256.HashData(new byte[] { (byte)i, 0x47, 0x45, 0x41, 0x52 }); // "GEAR"
            matrix[i] = BitConverter.ToUInt64(hash, 0);
        }
        return matrix;
    }
}
