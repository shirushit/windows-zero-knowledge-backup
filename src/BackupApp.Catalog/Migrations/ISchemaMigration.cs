using Microsoft.Data.Sqlite;

namespace BackupApp.Catalog.Migrations;

public interface ISchemaMigration
{
    int Version { get; }
    string Description { get; }
    Task ApplyAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken = default);
}
