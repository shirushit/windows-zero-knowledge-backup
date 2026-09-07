using BackupApp.Domain;

namespace BackupApp.RestoreEngine;

public enum RestoreConflictResolution
{
    Overwrite,
    RenameExisting,
    Skip
}

public record RestoreOptions(
    string DestinationRootPath,
    RestoreConflictResolution ConflictResolution,
    bool RestoreTimestamps
);

public interface IRestoreOrchestrator
{
    Task RestoreFileAsync(
        SnapshotId snapshotId,
        string relativeFilePath,
        RestoreOptions options,
        CancellationToken cancellationToken = default
    );

    Task RestoreAllAsync(
        SnapshotId snapshotId,
        RestoreOptions options,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default
    );
}

public sealed class RestoreOrchestrator : IRestoreOrchestrator
{
    public Task RestoreFileAsync(
        SnapshotId snapshotId,
        string relativeFilePath,
        RestoreOptions options,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Restore will be implemented in Phase 5.");
    }

    public Task RestoreAllAsync(
        SnapshotId snapshotId,
        RestoreOptions options,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Full restore will be implemented in Phase 5.");
    }
}
