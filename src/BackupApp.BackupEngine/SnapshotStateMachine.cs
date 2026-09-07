using BackupApp.Domain;

namespace BackupApp.BackupEngine;

public sealed class SnapshotStateMachine
{
    public static Snapshot StartSnapshot(BackupSetId backupSetId, long nextSnapshotNumber)
    {
        if (nextSnapshotNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(nextSnapshotNumber), "Snapshot number must be positive.");
        }

        return new Snapshot(
            SnapshotId.New(),
            backupSetId,
            nextSnapshotNumber,
            DateTimeOffset.UtcNow,
            SnapshotStatus.InProgress,
            TotalFiles: 0,
            TotalBytes: 0
        );
    }

    public static Snapshot CommitSnapshot(Snapshot current, int totalFiles, long totalBytes)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (current.Status != SnapshotStatus.InProgress)
        {
            throw new InvalidOperationException($"Cannot commit snapshot in '{current.Status}' state.");
        }

        if (totalFiles < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalFiles), "Total files cannot be negative.");
        }

        if (totalBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalBytes), "Total bytes cannot be negative.");
        }

        return current with
        {
            Status = SnapshotStatus.Committed,
            TotalFiles = totalFiles,
            TotalBytes = totalBytes,
            ErrorMessage = null
        };
    }

    public static Snapshot FailSnapshot(Snapshot current, string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);

        if (current.Status == SnapshotStatus.Committed)
        {
            throw new InvalidOperationException("Cannot fail an already committed snapshot.");
        }

        return current with
        {
            Status = SnapshotStatus.Failed,
            ErrorMessage = errorMessage
        };
    }
}
