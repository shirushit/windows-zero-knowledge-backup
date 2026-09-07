# Data Model

Use schema migrations from day one. Suggested entities: `BackupSet`, `Snapshot`, `FileEntry`, `FileVersion`, `Chunk/Object`, `RemoteObjectRef`, `Job`, `UploadAttempt`, `RestoreJob`, `ProviderAccount`, `SchemaVersion`.

## Invariants
Remote refs never imply successful backup until snapshot commit. File identity and path are separate concepts. Paths use a canonical internal representation. Cryptographic format version is stored per relevant envelope. Database constraints protect impossible states.

## Database
SQLite is suitable for a local MVP catalog if configured with transactions, migrations, indexes, corruption handling, and backup/rebuild strategy. Sensitive catalog fields must follow `SECURITY.md`.

## Search
Index normalized display names/path components locally. Search must remain responsive without leaking the searchable catalog remotely in plaintext.
