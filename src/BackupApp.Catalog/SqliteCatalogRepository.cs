using System.Globalization;
using System.Text.Json;
using BackupApp.Catalog.Migrations;
using BackupApp.Domain;
using Microsoft.Data.Sqlite;

namespace BackupApp.Catalog;

public sealed class SqliteCatalogRepository : ICatalogRepository
{
    private readonly string _connectionString;
    private readonly SchemaMigrator _migrator;

    public SqliteCatalogRepository(string databasePath, SchemaMigrator? migrator = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var dir = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };

        _connectionString = builder.ToString();
        _migrator = migrator ?? new SchemaMigrator();
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = 5000;
        """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        return connection;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await _migrator.MigrateAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveBackupSetAsync(BackupSet backupSet, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO backup_sets (id, name, included_roots_json, excluded_patterns_json, created_at)
            VALUES (@id, @name, @roots, @exclusions, @created)
            ON CONFLICT(id) DO UPDATE SET
                name = excluded.name,
                included_roots_json = excluded.included_roots_json,
                excluded_patterns_json = excluded.excluded_patterns_json;
        """;
        command.Parameters.AddWithValue("@id", backupSet.Id.ToString());
        command.Parameters.AddWithValue("@name", backupSet.Name);
        command.Parameters.AddWithValue("@roots", JsonSerializer.Serialize(backupSet.IncludedRoots));
        command.Parameters.AddWithValue("@exclusions", JsonSerializer.Serialize(backupSet.ExcludedPatterns));
        command.Parameters.AddWithValue("@created", backupSet.CreatedAtUtc.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<BackupSet?> GetBackupSetAsync(BackupSetId id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, included_roots_json, excluded_patterns_json, created_at FROM backup_sets WHERE id = @id;";
        command.Parameters.AddWithValue("@id", id.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var setId = new BackupSetId(Guid.Parse(reader.GetString(0)));
        var name = reader.GetString(1);
        var roots = JsonSerializer.Deserialize<List<string>>(reader.GetString(2)) ?? [];
        var exclusions = JsonSerializer.Deserialize<List<string>>(reader.GetString(3)) ?? [];
        var createdAt = DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture);

        return new BackupSet(setId, name, roots, exclusions, createdAt);
    }

    public async Task<IReadOnlyList<BackupSet>> ListBackupSetsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, included_roots_json, excluded_patterns_json, created_at FROM backup_sets ORDER BY created_at ASC;";

        var list = new List<BackupSet>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var setId = new BackupSetId(Guid.Parse(reader.GetString(0)));
            var name = reader.GetString(1);
            var roots = JsonSerializer.Deserialize<List<string>>(reader.GetString(2)) ?? [];
            var exclusions = JsonSerializer.Deserialize<List<string>>(reader.GetString(3)) ?? [];
            var createdAt = DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture);
            list.Add(new BackupSet(setId, name, roots, exclusions, createdAt));
        }

        return list;
    }

    public async Task CreateSnapshotAsync(Snapshot snapshot, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO snapshots (id, backup_set_id, snapshot_number, created_at, status, total_files, total_bytes, error_message)
            VALUES (@id, @setId, @num, @created, @status, @files, @bytes, @err);
        """;
        command.Parameters.AddWithValue("@id", snapshot.Id.ToString());
        command.Parameters.AddWithValue("@setId", snapshot.BackupSetId.ToString());
        command.Parameters.AddWithValue("@num", snapshot.SnapshotNumber);
        command.Parameters.AddWithValue("@created", snapshot.CreatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("@status", (int)snapshot.Status);
        command.Parameters.AddWithValue("@files", snapshot.TotalFiles);
        command.Parameters.AddWithValue("@bytes", snapshot.TotalBytes);
        command.Parameters.AddWithValue("@err", (object?)snapshot.ErrorMessage ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Snapshot?> GetSnapshotAsync(SnapshotId id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, backup_set_id, snapshot_number, created_at, status, total_files, total_bytes, error_message FROM snapshots WHERE id = @id;";
        command.Parameters.AddWithValue("@id", id.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return ReadSnapshot(reader);
    }

    public async Task<Snapshot?> GetLatestCommittedSnapshotAsync(BackupSetId backupSetId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, backup_set_id, snapshot_number, created_at, status, total_files, total_bytes, error_message
            FROM snapshots
            WHERE backup_set_id = @setId AND status = @committed
            ORDER BY snapshot_number DESC
            LIMIT 1;
        """;
        command.Parameters.AddWithValue("@setId", backupSetId.ToString());
        command.Parameters.AddWithValue("@committed", (int)SnapshotStatus.Committed);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return ReadSnapshot(reader);
    }

    public async Task<IReadOnlyList<Snapshot>> ListSnapshotsAsync(BackupSetId backupSetId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, backup_set_id, snapshot_number, created_at, status, total_files, total_bytes, error_message
            FROM snapshots
            WHERE backup_set_id = @setId
            ORDER BY snapshot_number DESC;
        """;
        command.Parameters.AddWithValue("@setId", backupSetId.ToString());

        var list = new List<Snapshot>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(ReadSnapshot(reader));
        }

        return list;
    }

    private static Snapshot ReadSnapshot(SqliteDataReader reader)
    {
        var id = new SnapshotId(Guid.Parse(reader.GetString(0)));
        var setId = new BackupSetId(Guid.Parse(reader.GetString(1)));
        var number = reader.GetInt64(2);
        var created = DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture);
        var status = (SnapshotStatus)reader.GetInt32(4);
        var files = reader.GetInt32(5);
        var bytes = reader.GetInt64(6);
        var err = reader.IsDBNull(7) ? null : reader.GetString(7);

        return new Snapshot(id, setId, number, created, status, files, bytes, err);
    }

    public async Task CommitSnapshotAsync(SnapshotId snapshotId, int totalFiles, long totalBytes, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE snapshots
            SET status = @committed, total_files = @files, total_bytes = @bytes, error_message = NULL
            WHERE id = @id;
        """;
        command.Parameters.AddWithValue("@committed", (int)SnapshotStatus.Committed);
        command.Parameters.AddWithValue("@files", totalFiles);
        command.Parameters.AddWithValue("@bytes", totalBytes);
        command.Parameters.AddWithValue("@id", snapshotId.ToString());

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkSnapshotFailedAsync(SnapshotId snapshotId, string errorMessage, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE snapshots
            SET status = @failed, error_message = @err
            WHERE id = @id;
        """;
        command.Parameters.AddWithValue("@failed", (int)SnapshotStatus.Failed);
        command.Parameters.AddWithValue("@err", errorMessage);
        command.Parameters.AddWithValue("@id", snapshotId.ToString());

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveFileEntriesAndVersionsAsync(
        IEnumerable<FileEntry> entries,
        IEnumerable<FileVersion> versions,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction();

        try
        {
            foreach (var entry in entries)
            {
                using var cmd = connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = """
                    INSERT INTO file_entries (id, backup_set_id, first_seen_snapshot_id, created_at)
                    VALUES (@id, @setId, @snapId, @created)
                    ON CONFLICT(id) DO NOTHING;
                """;
                cmd.Parameters.AddWithValue("@id", entry.Id.ToString());
                cmd.Parameters.AddWithValue("@setId", entry.BackupSetId.ToString());
                cmd.Parameters.AddWithValue("@snapId", entry.FirstSeenSnapshotId.ToString());
                cmd.Parameters.AddWithValue("@created", entry.CreatedAtUtc.ToString("O"));
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            foreach (var ver in versions)
            {
                using var cmd = connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = """
                    INSERT INTO file_versions (id, file_entry_id, snapshot_id, path, size_bytes, created_at, modified_at, content_hash, attributes)
                    VALUES (@id, @entryId, @snapId, @path, @size, @created, @modified, @hash, @attr);
                """;
                cmd.Parameters.AddWithValue("@id", ver.Id.ToString());
                cmd.Parameters.AddWithValue("@entryId", ver.FileEntryId.ToString());
                cmd.Parameters.AddWithValue("@snapId", ver.SnapshotId.ToString());
                cmd.Parameters.AddWithValue("@path", ver.Path.Value);
                cmd.Parameters.AddWithValue("@size", ver.SizeBytes);
                cmd.Parameters.AddWithValue("@created", ver.CreatedAtUtc.ToString("O"));
                cmd.Parameters.AddWithValue("@modified", ver.ModifiedUtc.ToString("O"));
                cmd.Parameters.AddWithValue("@hash", ver.ContentHashSha256);
                cmd.Parameters.AddWithValue("@attr", (int)ver.Attributes);
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                // Save chunk references
                for (var i = 0; i < ver.ChunkRefs.Count; i++)
                {
                    using var chunkRefCmd = connection.CreateCommand();
                    chunkRefCmd.Transaction = transaction;
                    chunkRefCmd.CommandText = """
                        INSERT INTO file_version_chunks (file_version_id, chunk_id, position)
                        VALUES (@verId, @chunkId, @pos);
                    """;
                    chunkRefCmd.Parameters.AddWithValue("@verId", ver.Id.ToString());
                    chunkRefCmd.Parameters.AddWithValue("@chunkId", ver.ChunkRefs[i].ToString());
                    chunkRefCmd.Parameters.AddWithValue("@pos", i);
                    await chunkRefCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<IReadOnlyList<FileVersion>> GetSnapshotFilesAsync(SnapshotId snapshotId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, file_entry_id, snapshot_id, path, size_bytes, created_at, modified_at, content_hash, attributes
            FROM file_versions
            WHERE snapshot_id = @snapId
            ORDER BY path ASC;
        """;
        command.Parameters.AddWithValue("@snapId", snapshotId.ToString());

        var list = new List<FileVersion>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = new FileVersionId(Guid.Parse(reader.GetString(0)));
            var entryId = new FileEntryId(Guid.Parse(reader.GetString(1)));
            var snapId = new SnapshotId(Guid.Parse(reader.GetString(2)));
            var path = CanonicalPath.From(reader.GetString(3));
            var size = reader.GetInt64(4);
            var created = DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture);
            var modified = DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture);
            var hash = reader.GetString(7);
            var attr = (FileEntryAttributes)reader.GetInt32(8);

            list.Add(new FileVersion(id, entryId, snapId, path, size, created, modified, hash, attr, []));
        }

        return list;
    }

    public async Task<FileVersion?> GetLatestFileVersionAsync(BackupSetId backupSetId, CanonicalPath path, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT v.id, v.file_entry_id, v.snapshot_id, v.path, v.size_bytes, v.created_at, v.modified_at, v.content_hash, v.attributes
            FROM file_versions v
            INNER JOIN snapshots s ON v.snapshot_id = s.id
            WHERE s.backup_set_id = @setId AND s.status = @committed AND v.path = @path
            ORDER BY s.snapshot_number DESC
            LIMIT 1;
        """;
        command.Parameters.AddWithValue("@setId", backupSetId.ToString());
        command.Parameters.AddWithValue("@committed", (int)SnapshotStatus.Committed);
        command.Parameters.AddWithValue("@path", path.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var id = new FileVersionId(Guid.Parse(reader.GetString(0)));
        var entryId = new FileEntryId(Guid.Parse(reader.GetString(1)));
        var snapId = new SnapshotId(Guid.Parse(reader.GetString(2)));
        var canonicalPath = CanonicalPath.From(reader.GetString(3));
        var size = reader.GetInt64(4);
        var created = DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture);
        var modified = DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture);
        var hash = reader.GetString(7);
        var attr = (FileEntryAttributes)reader.GetInt32(8);

        return new FileVersion(id, entryId, snapId, canonicalPath, size, created, modified, hash, attr, []);
    }

    public async Task<IReadOnlyList<FileVersion>> SearchFilesAsync(
        BackupSetId backupSetId,
        string searchTerm,
        SnapshotId? snapshotId = null,
        int maxResults = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(searchTerm);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();

        var sql = """
            SELECT v.id, v.file_entry_id, v.snapshot_id, v.path, v.size_bytes, v.created_at, v.modified_at, v.content_hash, v.attributes
            FROM file_versions v
            INNER JOIN snapshots s ON v.snapshot_id = s.id
            WHERE s.backup_set_id = @setId AND s.status = @committed
              AND v.path LIKE @term ESCAPE '\'
        """;

        if (snapshotId.HasValue)
        {
            sql += " AND v.snapshot_id = @snapId";
            command.Parameters.AddWithValue("@snapId", snapshotId.Value.ToString());
        }

        sql += " ORDER BY v.path ASC LIMIT @limit;";

        command.CommandText = sql;
        command.Parameters.AddWithValue("@setId", backupSetId.ToString());
        command.Parameters.AddWithValue("@committed", (int)SnapshotStatus.Committed);

        var escaped = searchTerm.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
        command.Parameters.AddWithValue("@term", $"%{escaped}%");
        command.Parameters.AddWithValue("@limit", maxResults);

        var list = new List<FileVersion>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = new FileVersionId(Guid.Parse(reader.GetString(0)));
            var entryId = new FileEntryId(Guid.Parse(reader.GetString(1)));
            var snapId = new SnapshotId(Guid.Parse(reader.GetString(2)));
            var canonicalPath = CanonicalPath.From(reader.GetString(3));
            var size = reader.GetInt64(4);
            var created = DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture);
            var modified = DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture);
            var hash = reader.GetString(7);
            var attr = (FileEntryAttributes)reader.GetInt32(8);

            list.Add(new FileVersion(id, entryId, snapId, canonicalPath, size, created, modified, hash, attr, []));
        }

        return list;
    }

    public async Task SaveChunksAsync(IEnumerable<StoredChunk> chunks, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction();

        try
        {
            foreach (var chunk in chunks)
            {
                using var cmd = connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = """
                    INSERT INTO chunks (id, plaintext_size_bytes, plaintext_sha256, encrypted_size_bytes, encrypted_sha256)
                    VALUES (@id, @pSize, @pHash, @eSize, @eHash)
                    ON CONFLICT(id) DO NOTHING;
                """;
                cmd.Parameters.AddWithValue("@id", chunk.Id.ToString());
                cmd.Parameters.AddWithValue("@pSize", chunk.PlaintextSizeBytes);
                cmd.Parameters.AddWithValue("@pHash", chunk.PlaintextSha256);
                cmd.Parameters.AddWithValue("@eSize", chunk.EncryptedSizeBytes);
                cmd.Parameters.AddWithValue("@eHash", chunk.EncryptedSha256);
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<StoredChunk?> GetChunkAsync(ObjectId id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, plaintext_size_bytes, plaintext_sha256, encrypted_size_bytes, encrypted_sha256 FROM chunks WHERE id = @id;";
        command.Parameters.AddWithValue("@id", id.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new StoredChunk(
            ObjectId.FromHex(reader.GetString(0)),
            reader.GetInt64(1),
            reader.GetString(2),
            reader.GetInt64(3),
            reader.GetString(4)
        );
    }

    public async Task SaveRemoteObjectRefAsync(RemoteObjectRef remoteRef, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO remote_object_refs (object_id, provider_id, remote_identifier, uploaded_at, is_verified)
            VALUES (@objId, @provId, @remId, @at, @ver)
            ON CONFLICT(object_id, provider_id) DO UPDATE SET
                remote_identifier = excluded.remote_identifier,
                uploaded_at = excluded.uploaded_at,
                is_verified = excluded.is_verified;
        """;
        command.Parameters.AddWithValue("@objId", remoteRef.ObjectId.ToString());
        command.Parameters.AddWithValue("@provId", remoteRef.ProviderId);
        command.Parameters.AddWithValue("@remId", remoteRef.RemoteIdentifier);
        command.Parameters.AddWithValue("@at", remoteRef.UploadedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("@ver", remoteRef.IsVerified ? 1 : 0);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<RemoteObjectRef?> GetRemoteObjectRefAsync(ObjectId objectId, string providerId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT object_id, provider_id, remote_identifier, uploaded_at, is_verified FROM remote_object_refs WHERE object_id = @objId AND provider_id = @provId;";
        command.Parameters.AddWithValue("@objId", objectId.ToString());
        command.Parameters.AddWithValue("@provId", providerId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new RemoteObjectRef(
            ObjectId.FromHex(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
            reader.GetInt32(4) == 1
        );
    }

    public async Task SaveBackupJobAsync(BackupJob job, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO backup_jobs (id, snapshot_id, type, status, started_at, completed_at, total_files, processed_files, total_bytes, processed_bytes, error_summary)
            VALUES (@id, @snapId, @type, @status, @started, @completed, @tFiles, @pFiles, @tBytes, @pBytes, @err);
        """;
        command.Parameters.AddWithValue("@id", job.Id.ToString());
        command.Parameters.AddWithValue("@snapId", job.SnapshotId.ToString());
        command.Parameters.AddWithValue("@type", (int)job.Type);
        command.Parameters.AddWithValue("@status", (int)job.Status);
        command.Parameters.AddWithValue("@started", job.StartedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("@completed", job.CompletedAtUtc.HasValue ? (object)job.CompletedAtUtc.Value.ToString("O") : DBNull.Value);
        command.Parameters.AddWithValue("@tFiles", job.TotalFiles);
        command.Parameters.AddWithValue("@pFiles", job.ProcessedFiles);
        command.Parameters.AddWithValue("@tBytes", job.TotalBytes);
        command.Parameters.AddWithValue("@pBytes", job.ProcessedBytes);
        command.Parameters.AddWithValue("@err", (object?)job.ErrorSummary ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<BackupJob?> GetBackupJobAsync(JobId id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, snapshot_id, type, status, started_at, completed_at, total_files, processed_files, total_bytes, processed_bytes, error_summary FROM backup_jobs WHERE id = @id;";
        command.Parameters.AddWithValue("@id", id.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new BackupJob(
            new JobId(Guid.Parse(reader.GetString(0))),
            new SnapshotId(Guid.Parse(reader.GetString(1))),
            (JobType)reader.GetInt32(2),
            (JobStatus)reader.GetInt32(3),
            DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
            reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
            reader.GetInt32(6),
            reader.GetInt32(7),
            reader.GetInt64(8),
            reader.GetInt64(9),
            reader.IsDBNull(10) ? null : reader.GetString(10)
        );
    }

    public async Task UpdateBackupJobProgressAsync(JobId id, int processedFiles, long processedBytes, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE backup_jobs SET processed_files = @pFiles, processed_bytes = @pBytes WHERE id = @id;";
        command.Parameters.AddWithValue("@pFiles", processedFiles);
        command.Parameters.AddWithValue("@pBytes", processedBytes);
        command.Parameters.AddWithValue("@id", id.ToString());

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task CompleteBackupJobAsync(JobId id, JobStatus status, string? errorSummary = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE backup_jobs
            SET status = @status, completed_at = @at, error_summary = @err
            WHERE id = @id;
        """;
        command.Parameters.AddWithValue("@status", (int)status);
        command.Parameters.AddWithValue("@at", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("@err", (object?)errorSummary ?? DBNull.Value);
        command.Parameters.AddWithValue("@id", id.ToString());

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
