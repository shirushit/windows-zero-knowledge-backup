namespace BackupApp.Domain;

public readonly record struct SnapshotId(Guid Value)
{
    public static SnapshotId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("N");
}

public readonly record struct ObjectId(string Value)
{
    public static ObjectId FromHex(string hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hex);
        return new(hex.ToLowerInvariant());
    }

    public override string ToString() => Value;
}

public readonly record struct BackupSetId(Guid Value)
{
    public static BackupSetId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("N");
}

public readonly record struct FileEntryId(Guid Value)
{
    public static FileEntryId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("N");
}

public readonly record struct FileVersionId(Guid Value)
{
    public static FileVersionId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("N");
}

public readonly record struct JobId(Guid Value)
{
    public static JobId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("N");
}
