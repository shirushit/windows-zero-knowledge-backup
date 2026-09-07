using BackupApp.Domain;

namespace BackupApp.BackupEngine;

public record BackupProgressReport(
    int TotalFilesScanned,
    int FilesProcessed,
    long TotalBytesScanned,
    long BytesProcessed,
    string? CurrentFileName
);

public interface IBackupOrchestrator
{
    Task<SnapshotId> RunBackupAsync(
        IEnumerable<string> sourceDirectories,
        IProgress<BackupProgressReport>? progress = null,
        CancellationToken cancellationToken = default
    );
}

public sealed class BackupOrchestrator : IBackupOrchestrator
{
    public Task<SnapshotId> RunBackupAsync(
        IEnumerable<string> sourceDirectories,
        IProgress<BackupProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Backup orchestration will be implemented in Phase 4.");
    }
}
