namespace BackupApp.Domain;

public readonly record struct SnapshotId(Guid Value)
{
    public static SnapshotId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("N");
}

public readonly record struct ObjectId(string Value)
{
    public static ObjectId FromHex(string hex) => new(hex);
    public override string ToString() => Value;
}

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
    string RelativePath,
    long SizeBytes,
    DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt,
    string ContentHashSha256
);
