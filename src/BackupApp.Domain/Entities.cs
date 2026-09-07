namespace BackupApp.Domain;

public enum SnapshotStatus
{
    InProgress,
    Committed,
    Failed,
    Aborted
}

public record BackupSet(
    BackupSetId Id,
    string Name,
    IReadOnlyList<string> IncludedRoots,
    IReadOnlyList<string> ExcludedPatterns,
    DateTimeOffset CreatedAtUtc
);

public record Snapshot(
    SnapshotId Id,
    BackupSetId BackupSetId,
    long SnapshotNumber,
    DateTimeOffset CreatedAtUtc,
    SnapshotStatus Status,
    int TotalFiles,
    long TotalBytes,
    string? ErrorMessage = null
)
{
    public bool IsCommitted => Status == SnapshotStatus.Committed;
}

public record FileEntry(
    FileEntryId Id,
    BackupSetId BackupSetId,
    SnapshotId FirstSeenSnapshotId,
    DateTimeOffset CreatedAtUtc
);

[Flags]
public enum FileEntryAttributes
{
    None = 0,
    ReadOnly = 1 << 0,
    Hidden = 1 << 1,
    System = 1 << 2,
    Archive = 1 << 3,
    Encrypted = 1 << 4
}

public record FileVersion(
    FileVersionId Id,
    FileEntryId FileEntryId,
    SnapshotId SnapshotId,
    CanonicalPath Path,
    long SizeBytes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ModifiedUtc,
    string ContentHashSha256,
    FileEntryAttributes Attributes,
    IReadOnlyList<ObjectId> ChunkRefs
);

public record StoredChunk(
    ObjectId Id,
    long PlaintextSizeBytes,
    string PlaintextSha256,
    long EncryptedSizeBytes,
    string EncryptedSha256
);

public record RemoteObjectRef(
    ObjectId ObjectId,
    string ProviderId,
    string RemoteIdentifier,
    DateTimeOffset UploadedAtUtc,
    bool IsVerified
);

public enum JobType
{
    InitialBackup,
    IncrementalBackup,
    Restore
}

public enum JobStatus
{
    Pending,
    Running,
    Paused,
    Completed,
    Failed,
    Cancelled
}

public record BackupJob(
    JobId Id,
    SnapshotId SnapshotId,
    JobType Type,
    JobStatus Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int TotalFiles,
    int ProcessedFiles,
    long TotalBytes,
    long ProcessedBytes,
    string? ErrorSummary = null
);

public enum ConflictResolutionPolicy
{
    Overwrite,
    RenameExisting,
    Skip
}

public record RestoreJob(
    JobId Id,
    SnapshotId SnapshotId,
    string DestinationRoot,
    ConflictResolutionPolicy ConflictPolicy,
    JobStatus Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int TotalFiles,
    int RestoredFiles,
    int FailedFiles,
    string? ErrorSummary = null
);

public record ProviderAccount(
    string ProviderId,
    string AccountIdentifier,
    byte[] EncryptedCredentials,
    DateTimeOffset CreatedAtUtc
);
