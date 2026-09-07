namespace BackupApp.BackupEngine;

public sealed record BackupEngineOptions
{
    public int MaxConcurrentUploads { get; init; } = 2;
    public int ThrottleDelayMs { get; init; }
    public int ChunkSizeBytes { get; init; } = 8 * 1024 * 1024; // 8MB default chunk size
    public bool SkipLockedFiles { get; init; } = true;

    public static BackupEngineOptions Default { get; } = new();
}
