using Microsoft.Data.Sqlite;

namespace BackupApp.Catalog.Migrations;

public sealed class SchemaMigrator
{
    private readonly IReadOnlyList<ISchemaMigration> _migrations;

    public SchemaMigrator(IEnumerable<ISchemaMigration>? migrations = null)
    {
        _migrations = migrations?.OrderBy(m => m.Version).ToList()
            ?? [new Migration001InitialSchema(), new Migration002AddFileVersionTimestampAndIndex()];
    }

    public async Task MigrateAsync(SqliteConnection connection, CancellationToken cancellationToken = default)
    {
        await EnsureMigrationTableAsync(connection, cancellationToken).ConfigureAwait(false);

        var currentVersion = await GetCurrentVersionAsync(connection, cancellationToken).ConfigureAwait(false);

        foreach (var migration in _migrations.Where(m => m.Version > currentVersion))
        {
            using var transaction = connection.BeginTransaction();
            try
            {
                await migration.ApplyAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

                using var recordCommand = connection.CreateCommand();
                recordCommand.Transaction = transaction;
                recordCommand.CommandText = "INSERT INTO schema_migrations (version, applied_at, description) VALUES (@v, @at, @desc);";
                recordCommand.Parameters.AddWithValue("@v", migration.Version);
                recordCommand.Parameters.AddWithValue("@at", DateTimeOffset.UtcNow.ToString("O"));
                recordCommand.Parameters.AddWithValue("@desc", migration.Description);
                await recordCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }

    private static async Task EnsureMigrationTableAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                version INTEGER PRIMARY KEY,
                applied_at TEXT NOT NULL,
                description TEXT NOT NULL
            );
        """;

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<int> GetCurrentVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        const string sql = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }
}
