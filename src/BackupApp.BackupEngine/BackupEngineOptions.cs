using BackupApp.BackupEngine.Capture;

namespace BackupApp.BackupEngine;

public sealed record BackupEngineOptions
{
    public int MaxConcurrentUploads { get; init; } = 2;
    public int ThrottleDelayMs { get; init; }
    public int ChunkSizeBytes { get; init; } = 32 * 1024 * 1024; // 32MB default chunk size
    public bool SkipLockedFiles { get; init; } = true;
    public bool UseFastCdc { get; init; } = true;
    public int FastCdcMinChunkSizeBytes { get; init; } = FastCdcChunker.DefaultMinChunkSize;
    public int FastCdcAvgChunkSizeBytes { get; init; } = FastCdcChunker.DefaultAvgChunkSize;
    public int FastCdcMaxChunkSizeBytes { get; init; } = FastCdcChunker.DefaultMaxChunkSize;

    public static BackupEngineOptions Default { get; } = new();
}
