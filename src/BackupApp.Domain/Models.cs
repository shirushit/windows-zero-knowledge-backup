namespace BackupApp.Domain;

public enum BackupStatus
{
    NotStarted,
    Scanning,
    BackingUp,
    Protected,
    Paused,
    Offline,
    NeedsAttention,
    Failed
}

public record FileMetadata(
    CanonicalPath RelativePath,
    long SizeBytes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ModifiedUtc,
    string ContentHashSha256
);
