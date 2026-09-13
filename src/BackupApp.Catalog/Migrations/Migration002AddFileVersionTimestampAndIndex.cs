using Microsoft.Data.Sqlite;

namespace BackupApp.Catalog.Migrations;

public sealed class Migration002AddFileVersionTimestampAndIndex : ISchemaMigration
{
    public int Version => 2;
    public string Description => "Add version_timestamp column and file history indexes";

    public async Task ApplyAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken = default)
    {
        // Check if version_timestamp column exists in file_versions
        bool hasColumn = false;
        using (var checkCmd = connection.CreateCommand())
        {
            checkCmd.Transaction = transaction;
            checkCmd.CommandText = "PRAGMA table_info(file_versions);";
            using var reader = await checkCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var colName = reader.GetString(1);
                if (string.Equals(colName, "version_timestamp", StringComparison.OrdinalIgnoreCase))
                {
                    hasColumn = true;
                    break;
                }
            }
        }

        if (!hasColumn)
        {
            using var alterCmd = connection.CreateCommand();
            alterCmd.Transaction = transaction;
            alterCmd.CommandText = "ALTER TABLE file_versions ADD COLUMN version_timestamp TEXT;";
            await alterCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            // Populate existing rows' version_timestamp from created_at
            using var updateCmd = connection.CreateCommand();
            updateCmd.Transaction = transaction;
            updateCmd.CommandText = "UPDATE file_versions SET version_timestamp = created_at WHERE version_timestamp IS NULL;";
            await updateCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        const string indexSql = """
            CREATE INDEX IF NOT EXISTS idx_file_versions_path_history ON file_versions(path, modified_at DESC);
            CREATE INDEX IF NOT EXISTS idx_file_versions_entry_history ON file_versions(file_entry_id, snapshot_id);
            CREATE INDEX IF NOT EXISTS idx_file_versions_timestamp ON file_versions(version_timestamp DESC);
        """;

        using var indexCmd = connection.CreateCommand();
        indexCmd.Transaction = transaction;
        indexCmd.CommandText = indexSql;
        await indexCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
