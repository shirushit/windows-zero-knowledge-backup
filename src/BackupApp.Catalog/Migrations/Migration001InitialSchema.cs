using Microsoft.Data.Sqlite;

namespace BackupApp.Catalog.Migrations;

public sealed class Migration001InitialSchema : ISchemaMigration
{
    public int Version => 1;
    public string Description => "Create initial catalog tables, foreign keys, and indexes";

    public async Task ApplyAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken = default)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS backup_sets (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                included_roots_json TEXT NOT NULL,
                excluded_patterns_json TEXT NOT NULL,
                created_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS snapshots (
                id TEXT PRIMARY KEY,
                backup_set_id TEXT NOT NULL,
                snapshot_number INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                status INTEGER NOT NULL,
                total_files INTEGER NOT NULL,
                total_bytes INTEGER NOT NULL,
                error_message TEXT,
                FOREIGN KEY(backup_set_id) REFERENCES backup_sets(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_snapshots_backup_set ON snapshots(backup_set_id, snapshot_number DESC);

            CREATE TABLE IF NOT EXISTS file_entries (
                id TEXT PRIMARY KEY,
                backup_set_id TEXT NOT NULL,
                first_seen_snapshot_id TEXT NOT NULL,
                created_at TEXT NOT NULL,
                FOREIGN KEY(backup_set_id) REFERENCES backup_sets(id) ON DELETE CASCADE,
                FOREIGN KEY(first_seen_snapshot_id) REFERENCES snapshots(id)
            );

            CREATE INDEX IF NOT EXISTS idx_file_entries_backup_set ON file_entries(backup_set_id);

            CREATE TABLE IF NOT EXISTS file_versions (
                id TEXT PRIMARY KEY,
                file_entry_id TEXT NOT NULL,
                snapshot_id TEXT NOT NULL,
                path TEXT NOT NULL,
                size_bytes INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                modified_at TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                attributes INTEGER NOT NULL,
                FOREIGN KEY(file_entry_id) REFERENCES file_entries(id) ON DELETE CASCADE,
                FOREIGN KEY(snapshot_id) REFERENCES snapshots(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_file_versions_snapshot ON file_versions(snapshot_id, path);
            CREATE INDEX IF NOT EXISTS idx_file_versions_hash ON file_versions(content_hash);

            CREATE TABLE IF NOT EXISTS chunks (
                id TEXT PRIMARY KEY,
                plaintext_size_bytes INTEGER NOT NULL,
                plaintext_sha256 TEXT NOT NULL,
                encrypted_size_bytes INTEGER NOT NULL,
                encrypted_sha256 TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS file_version_chunks (
                file_version_id TEXT NOT NULL,
                chunk_id TEXT NOT NULL,
                position INTEGER NOT NULL,
                PRIMARY KEY(file_version_id, position),
                FOREIGN KEY(file_version_id) REFERENCES file_versions(id) ON DELETE CASCADE,
                FOREIGN KEY(chunk_id) REFERENCES chunks(id)
            );

            CREATE TABLE IF NOT EXISTS remote_object_refs (
                object_id TEXT NOT NULL,
                provider_id TEXT NOT NULL,
                remote_identifier TEXT NOT NULL,
                uploaded_at TEXT NOT NULL,
                is_verified INTEGER NOT NULL,
                PRIMARY KEY(object_id, provider_id)
            );

            CREATE INDEX IF NOT EXISTS idx_remote_object_refs_provider ON remote_object_refs(provider_id, is_verified);

            CREATE TABLE IF NOT EXISTS backup_jobs (
                id TEXT PRIMARY KEY,
                snapshot_id TEXT NOT NULL,
                type INTEGER NOT NULL,
                status INTEGER NOT NULL,
                started_at TEXT NOT NULL,
                completed_at TEXT,
                total_files INTEGER NOT NULL,
                processed_files INTEGER NOT NULL,
                total_bytes INTEGER NOT NULL,
                processed_bytes INTEGER NOT NULL,
                error_summary TEXT,
                FOREIGN KEY(snapshot_id) REFERENCES snapshots(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS restore_jobs (
                id TEXT PRIMARY KEY,
                snapshot_id TEXT NOT NULL,
                destination_root TEXT NOT NULL,
                conflict_policy INTEGER NOT NULL,
                status INTEGER NOT NULL,
                started_at TEXT NOT NULL,
                completed_at TEXT,
                total_files INTEGER NOT NULL,
                restored_files INTEGER NOT NULL,
                failed_files INTEGER NOT NULL,
                error_summary TEXT,
                FOREIGN KEY(snapshot_id) REFERENCES snapshots(id) ON DELETE CASCADE
            );
        """;

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
