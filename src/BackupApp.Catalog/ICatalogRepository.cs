using BackupApp.Domain;

namespace BackupApp.Catalog;

public interface ICatalogRepository : IAsyncDisposable
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    // Backup Sets
    Task SaveBackupSetAsync(BackupSet backupSet, CancellationToken cancellationToken = default);
    Task<BackupSet?> GetBackupSetAsync(BackupSetId id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BackupSet>> ListBackupSetsAsync(CancellationToken cancellationToken = default);

    // Snapshots
    Task CreateSnapshotAsync(Snapshot snapshot, CancellationToken cancellationToken = default);
    Task<Snapshot?> GetSnapshotAsync(SnapshotId id, CancellationToken cancellationToken = default);
    Task<Snapshot?> GetLatestCommittedSnapshotAsync(BackupSetId backupSetId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Snapshot>> ListSnapshotsAsync(BackupSetId backupSetId, CancellationToken cancellationToken = default);
    Task CommitSnapshotAsync(SnapshotId snapshotId, int totalFiles, long totalBytes, CancellationToken cancellationToken = default);
    Task MarkSnapshotFailedAsync(SnapshotId snapshotId, string errorMessage, CancellationToken cancellationToken = default);

    // Files & Versions
    Task SaveFileEntriesAndVersionsAsync(
        IEnumerable<FileEntry> entries,
        IEnumerable<FileVersion> versions,
        CancellationToken cancellationToken = default
    );
    Task<IReadOnlyList<FileVersion>> GetSnapshotFilesAsync(SnapshotId snapshotId, CancellationToken cancellationToken = default);
    Task<FileVersion?> GetLatestFileVersionAsync(BackupSetId backupSetId, CanonicalPath path, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FileVersion>> GetFileVersionsAsync(CanonicalPath path, BackupSetId? backupSetId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FileVersion>> SearchFilesAsync(BackupSetId backupSetId, string searchTerm, SnapshotId? snapshotId = null, int maxResults = 100, CancellationToken cancellationToken = default);

    // Chunks & Remote Refs
    Task SaveChunksAsync(IEnumerable<StoredChunk> chunks, CancellationToken cancellationToken = default);
    Task<StoredChunk?> GetChunkAsync(ObjectId id, CancellationToken cancellationToken = default);
    Task SaveRemoteObjectRefAsync(RemoteObjectRef remoteRef, CancellationToken cancellationToken = default);
    Task<RemoteObjectRef?> GetRemoteObjectRefAsync(ObjectId objectId, string providerId, CancellationToken cancellationToken = default);

    // Jobs
    Task SaveBackupJobAsync(BackupJob job, CancellationToken cancellationToken = default);
    Task<BackupJob?> GetBackupJobAsync(JobId id, CancellationToken cancellationToken = default);
    Task UpdateBackupJobProgressAsync(JobId id, int processedFiles, long processedBytes, CancellationToken cancellationToken = default);
    Task CompleteBackupJobAsync(JobId id, JobStatus status, string? errorSummary = null, CancellationToken cancellationToken = default);
}
