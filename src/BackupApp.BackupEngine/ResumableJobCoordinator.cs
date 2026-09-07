using BackupApp.Catalog;
using BackupApp.Domain;

namespace BackupApp.BackupEngine;

public interface IResumableJobCoordinator
{
    Task<BackupJob> StartJobAsync(SnapshotId snapshotId, JobType type, int totalFiles, long totalBytes, CancellationToken cancellationToken = default);
    Task RecordProgressAsync(JobId jobId, int processedFiles, long processedBytes, CancellationToken cancellationToken = default);
    Task CompleteJobAsync(JobId jobId, CancellationToken cancellationToken = default);
    Task FailJobAsync(JobId jobId, string errorMessage, CancellationToken cancellationToken = default);
    Task PauseJobAsync(JobId jobId, CancellationToken cancellationToken = default);
    Task ResumeJobAsync(JobId jobId, CancellationToken cancellationToken = default);
}

public sealed class ResumableJobCoordinator : IResumableJobCoordinator
{
    private readonly ICatalogRepository _catalog;

    public ResumableJobCoordinator(ICatalogRepository catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public async Task<BackupJob> StartJobAsync(
        SnapshotId snapshotId,
        JobType type,
        int totalFiles,
        long totalBytes,
        CancellationToken cancellationToken = default)
    {
        var job = new BackupJob(
            JobId.New(),
            snapshotId,
            type,
            JobStatus.Running,
            DateTimeOffset.UtcNow,
            null,
            totalFiles,
            0,
            totalBytes,
            0
        );

        await _catalog.SaveBackupJobAsync(job, cancellationToken).ConfigureAwait(false);
        return job;
    }

    public Task RecordProgressAsync(JobId jobId, int processedFiles, long processedBytes, CancellationToken cancellationToken = default)
    {
        return _catalog.UpdateBackupJobProgressAsync(jobId, processedFiles, processedBytes, cancellationToken);
    }

    public Task CompleteJobAsync(JobId jobId, CancellationToken cancellationToken = default)
    {
        return _catalog.CompleteBackupJobAsync(jobId, JobStatus.Completed, errorSummary: null, cancellationToken);
    }

    public Task FailJobAsync(JobId jobId, string errorMessage, CancellationToken cancellationToken = default)
    {
        return _catalog.CompleteBackupJobAsync(jobId, JobStatus.Failed, errorMessage, cancellationToken);
    }

    public Task PauseJobAsync(JobId jobId, CancellationToken cancellationToken = default)
    {
        return _catalog.CompleteBackupJobAsync(jobId, JobStatus.Paused, errorSummary: null, cancellationToken);
    }

    public Task ResumeJobAsync(JobId jobId, CancellationToken cancellationToken = default)
    {
        return _catalog.CompleteBackupJobAsync(jobId, JobStatus.Running, errorSummary: null, cancellationToken);
    }
}
