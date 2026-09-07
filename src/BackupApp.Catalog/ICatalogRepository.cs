using BackupApp.Domain;

namespace BackupApp.Catalog;

public interface ICatalogRepository : IAsyncDisposable
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task CommitSnapshotAsync(SnapshotId snapshotId, IEnumerable<FileMetadata> files, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FileMetadata>> GetSnapshotFilesAsync(SnapshotId snapshotId, CancellationToken cancellationToken = default);
}

public sealed class SqliteCatalogRepository : ICatalogRepository
{
    private readonly string _connectionString;

    public SqliteCatalogRepository(string databasePath)
    {
        _connectionString = $"Data Source={databasePath};Mode=ReadWriteCreate;Cache=Shared";
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // Phase 1 will implement migrations and schema
        return Task.CompletedTask;
    }

    public Task CommitSnapshotAsync(SnapshotId snapshotId, IEnumerable<FileMetadata> files, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Catalog transactions will be implemented in Phase 1.");
    }

    public Task<IReadOnlyList<FileMetadata>> GetSnapshotFilesAsync(SnapshotId snapshotId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Catalog retrieval will be implemented in Phase 1.");
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
