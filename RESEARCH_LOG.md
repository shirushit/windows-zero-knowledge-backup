# Research / Engineering Log

Append-only chronological record. Agents must never rewrite or delete historical entries; corrections are appended as new entries.

### 2026-09-07 22:05 +03:00 — Documentation baseline created
Agent/model: GPT-5.6 Sol
Branch: documentation package (pre-repository)
Commit: N/A
Plan item: Phase 0 documentation bootstrap
Completed: Initial multi-agent project constitution and build plan package.
Changed: Created product, architecture, security, crypto, storage, UX, Git/CI, testing and release guidance.
Tests/build: Documentation-only package; no application build exists yet.
Security review: Zero-knowledge and provider abstraction constraints established.
Problems: Technology stack and exact crypto library remain intentionally unselected.
Decisions: Hard direction changes require human developer approval.
Next: Initialize repository and execute PLAN Phase 0.

### 2026-09-07 22:28 +03:00 — Task 0.1 Repository bootstrap completed
Agent/model: Gemini 3.8 Flash (Antigravity)
Branch: feat/0.1-repo-bootstrap
Commit: pending (initial commit)
Plan item: 0.1 Repository bootstrap
Completed: Git repository initialized with main branch; comprehensive .gitignore with secret exclusions; .editorconfig; MIT LICENSE; CONTRIBUTING.md; GitHub PR template.
Changed: Added .gitignore, .editorconfig, LICENSE, CONTRIBUTING.md, .github/PULL_REQUEST_TEMPLATE.md; updated PLAN.md.
Tests/build: N/A (repository setup files verified).
Security review: .gitignore explicitly forbids secrets, private keys (*.key, *.token, *.pfx, *.pem, *.secret), local envs, and credential stores. Verified no secrets staged.
Problems: None.
Decisions: Initialized repository on main branch; MIT License applied.
Next: Task 0.2 Select Windows implementation stack & record DEC-002.

### 2026-09-07 22:30 +03:00 — Task 0.2 Windows implementation stack selected
Agent/model: Gemini 3.8 Flash (Antigravity)
Branch: feat/0.2-stack-selection
Commit: pending
Plan item: 0.2 Select Windows implementation stack
Completed: Stack evaluation and selection completed; DEC-002 recorded with Human Approval.
Changed: Updated DECISIONS.md with DEC-002; updated PLAN.md item 0.2 to [x].
Tests/build: N/A.
Security review: Evaluated managed memory safety, DPAPI key storage, libsodium crypto bindings, and absence of Electron/browser overhead.
Problems: None.
Decisions: Selected .NET 8 (C#) with WPF (Fluent RTL), SQLite, and NSec / libsodium as Windows desktop implementation stack.
Next: Task 0.3 Application skeleton.

### 2026-09-07 22:38 +03:00 — Tasks 0.3 & 0.4 Skeleton and Dependency Governance
Agent/model: Gemini 3.8 Flash (Antigravity)
Branch: feat/0.3-application-skeleton
Commit: pending
Plan items: 0.3 Application skeleton, 0.4 Dependency governance
Completed: Created 10 application projects and 2 test projects according to ARCHITECTURE.md boundaries. Enabled Central Package Management, Directory.Build.props with strict analysis, and reproducible lockfiles.
Changed: Added BackupApp.sln, Directory.Build.props, Directory.Packages.props, projects under src/ and tests/, and packages.lock.json files.
Tests/build: Clean build (0 warnings, 0 errors); unit and crypto test runners passed; `dotnet restore --locked-mode` verified.
Security review: No credentials or tokens; crypto abstractions isolated in BackupApp.Crypto with libsodium; domain models isolated from providers/UI.
Problems: None.
Decisions: Suppressed CA1707 only for test assemblies to permit standard test naming conventions.
Next: Task 0.5 CI bootstrap.

### 2026-09-07 22:42 +03:00 — Task 0.5 CI bootstrap completed
Agent/model: Gemini 3.8 Flash (Antigravity)
Branch: feat/0.5-ci-bootstrap
Commit: pending
Plan item: 0.5 CI bootstrap
Completed: GitHub Actions workflow created (.github/workflows/ci.yml) covering locked restore, format verification, Release build, unit/crypto testing, code coverage collection, and secret scanning. Verified clean pass and verified deliberate failure fixture exits with code 1.
Changed: Added .github/workflows/ci.yml; updated PLAN.md item 0.5 to [x].
Tests/build: `dotnet format --verify-no-changes`, `dotnet build -c Release`, and `dotnet test -c Release` executed cleanly. Deliberate failure test confirmed to fail build.
Security review: Secret scan script integrated into CI verifying no private keys or plaintext credentials can be merged.
Problems: None.
Decisions: CI workflow targets windows-latest runner with strict locked restore.
Next: Task 0.6 Git protections/workflow & Task 0.7 Baseline performance budgets.

### 2026-09-07 22:43 +03:00 — Tasks 0.6 & 0.7 and Gate 0 Foundation Completed
Agent/model: Gemini 3.8 Flash (Antigravity)
Branch: feat/0.6-0.7-workflow-and-budgets
Commit: pending
Plan items: 0.6 Git protections/workflow, 0.7 Baseline performance budgets, Gate 0 Foundation
Completed: Git workflow protections and local pre-commit hook (.githooks/pre-commit) implemented; PERFORMANCE.md populated with explicit budgets for idle, active scan/hash, upload, UI, and restore; Gate 0 fully passed.
Changed: Added .githooks/pre-commit; updated GIT_WORKFLOW.md, PERFORMANCE.md, PLAN.md (all Phase 0 items marked [x]).
Tests/build: `dotnet restore --locked-mode BackupApp.sln` verified; `dotnet build -c Release` clean (0 warnings, 0 errors); `dotnet test -c Release` passed.
Security review: Pre-commit hook enforces no direct commits to main and scans for private keys and credentials.
Problems: None.
Decisions: Local pre-commit hook active via core.hooksPath.
Next: Phase 1 — Domain + Local Catalog (Task 1.1 Domain models and invariants).

### 2026-09-07 22:51 +03:00 — Task 1.1 Domain models and invariants completed
Agent/model: Gemini 3.8 Flash (Antigravity)
Branch: feat/1.1-domain-models
Commit: pending
Plan item: 1.1 Domain models and invariants
Completed: Defined strongly-typed domain identifiers (SnapshotId, ObjectId, BackupSetId, FileEntryId, FileVersionId, JobId); CanonicalPath with path-traversal prevention, character filtering, and Windows reserved device rejection; core domain entities (BackupSet, Snapshot, FileEntry, FileVersion, StoredChunk, RemoteObjectRef, BackupJob, RestoreJob, ProviderAccount). Added 37 comprehensive unit tests.
Changed: Added src/BackupApp.Domain/Identifiers.cs, CanonicalPath.cs, Entities.cs, tests/BackupApp.UnitTests/CanonicalPathTests.cs; updated Models.cs, DomainModelTests.cs, PLAN.md.
Tests/build: `dotnet build -c Release` clean; `dotnet test -c Release` passed (39/39 tests passed); `dotnet format` clean.
Security review: Path traversal defenses verified (cannot escape base path, no dot-dot navigation, no reserved device names); strongly-typed IDs prevent parameter confusion.
Problems: None.
Decisions: Implemented IComparable with comparison operators on CanonicalPath to satisfy CA1036.
Next: Task 1.2 SQLite/catalog schema + migrations.

### 2026-09-08 00:14 +03:00 — Task 1.2 SQLite/catalog schema + migrations completed
Agent/model: Gemini 3.8 Flash (Antigravity)
Branch: feat/1.2-catalog-schema-migrations
Commit: pending
Plan item: 1.2 SQLite/catalog schema + migrations
Completed: Created SQLite catalog database schema with migrations engine (SchemaMigrator, Migration001InitialSchema). Implemented SqliteCatalogRepository with WAL journal mode, busy timeouts, parameterization, and atomic multi-entity transactions. Added 6 integration tests covering migrations, backup sets, snapshots, file versions, chunks, and jobs.
Changed: Added src/BackupApp.Catalog/Migrations/ISchemaMigration.cs, Migration001InitialSchema.cs, SchemaMigrator.cs, SqliteCatalogRepository.cs, tests/BackupApp.UnitTests/CatalogRepositoryTests.cs; updated ICatalogRepository.cs, PLAN.md.
Tests/build: `dotnet build -c Release` clean (0 warnings, 0 errors); `dotnet test -c Release` passed (43/43 tests); `dotnet format --verify-no-changes` passed.
Security review: SQL injection prevented via parameterization; database file created with secure permissions in local application directory; WAL mode protects ACID consistency against crash/power interruptions.
Problems: Fixed CA1305 by enforcing CultureInfo.InvariantCulture on date/number parsing; fixed CA1816 with GC.SuppressFinalize in tests.
Decisions: Used PRAGMA journal_mode = WAL and PRAGMA foreign_keys = ON by default for high concurrency and referential integrity.
Next: Task 1.3 Canonical internal path model (already foundational via CanonicalPath) & Task 1.4 File discovery with exclusions.

### 2026-09-08 00:19 +03:00 — Tasks 1.3 & 1.4 Path Model & File Discovery with Exclusions completed
Agent/model: Gemini 3.8 Flash (Antigravity)
Branch: feat/1.3-1.4-path-discovery
Commit: pending
Plan items: 1.3 Canonical internal path model, 1.4 File discovery with exclusions
Completed: Implemented PathNormalizer for converting OS paths to CanonicalPath; implemented ExclusionFilter supporting glob patterns (*, **), exact file names, and directory-level exclusions; implemented resilient asynchronous FileDiscoveryScanner that skips reparse points/symlinks by default and safely handles access exceptions. Added 6 unit tests covering exclusions, traversal, and Hebrew paths.
Changed: Added src/BackupApp.Domain/PathNormalizer.cs, src/BackupApp.BackupEngine/Scanner/DiscoveredFile.cs, ExclusionFilter.cs, FileDiscoveryScanner.cs, tests/BackupApp.UnitTests/FileDiscoveryScannerTests.cs; updated BackupApp.UnitTests.csproj, PLAN.md.
Tests/build: `dotnet build -c Release` clean (0 warnings, 0 errors); `dotnet test -c Release` passed (49/49 tests); `dotnet format --verify-no-changes` passed.
Security review: PathNormalizer enforces root boundary confinement; FileDiscoveryScanner ignores reparse points / symlinks preventing traversal out of scope or cyclic symlink attacks.
Problems: None.
Decisions: Excluded directory recursion occurs immediately at directory evaluation time to avoid unnecessary disk I/O on large ignored trees (like node_modules).
Next: Task 1.5 Metadata capture and stable-read detection & Task 1.6 Content hashing streaming implementation.

### 2026-09-08 00:22 +03:00 — Tasks 1.5 & 1.6 Stable-Read Capture and Streaming Hashing completed
Agent/model: Gemini 3.8 Flash (Antigravity)
Branch: feat/1.5-1.6-stable-read-hashing
Commit: pending
Plan items: 1.5 Metadata capture and stable-read detection, 1.6 Content hashing streaming implementation
Completed: Implemented StreamingHasher with 64KB bounded buffer and progress reporting; implemented StableFileCaptureService with FileShare.ReadWrite, pre/post metadata stability validation, sharing violation detection (locked files), backoff retries, and multi-chunk partitioning (up to 8MB chunk descriptors). Added 6 unit tests covering empty, small, multi-chunk, missing, and locked files.
Changed: Added src/BackupApp.BackupEngine/Capture/StreamingHasher.cs, StableFileCapture.cs, tests/BackupApp.UnitTests/StableFileCaptureTests.cs; updated PLAN.md.
Tests/build: `dotnet build -c Release` clean (0 warnings, 0 errors); `dotnet test -c Release` passed (55/55 tests); `dotnet format --verify-no-changes` passed.
Security review: Zero memory-exhaustion risk: stream hashing operates strictly within 64KB buffer windows. Locked and mutating files are safely detected rather than uploading partial/corrupted states.
Problems: None.
Decisions: Files smaller than max chunk size emit a single chunk whose hash equals whole-file hash; empty files emit valid empty SHA-256 chunk.
Next: Task 1.7 Change detection & Task 1.8 Snapshot/version state machine.

### 2026-09-08 00:30 +03:00 — Tasks 1.7-1.10 and Gate 1 (Incremental Delta & Resilient Catalog) Passed
Agent/model: Gemini 3.8 Flash (Antigravity)
Branch: feat/1.7-1.10-change-detection-state-search
Commit: 97eeab6 (merged in 841ae53)
Plan items: 1.7 Change detection, 1.8 Snapshot state machine, 1.9 Persistent resumable job model, 1.10 Local search indexes, GATE 1
Completed: Implemented ChangeDetectionService detecting New, Modified, Unchanged, Deleted, and Renamed files; implemented SnapshotStateMachine enforcing monotonically increasing snapshot numbers and strictly valid state transitions; implemented ResumableJobCoordinator for managing backup/restore jobs; implemented SearchFilesAsync index search in SqliteCatalogRepository. Added comprehensive Gate 1 test fixture (Gate1VerificationTests) verifying deterministic dual-scan incremental delta (2 unchanged, 1 modified, 1 deleted, 1 renamed, 1 new) and crash/restart consistency across repository instances.
Changed: Added src/BackupApp.BackupEngine/ChangeDetection/ChangeDetectionService.cs, src/BackupApp.BackupEngine/SnapshotStateMachine.cs, src/BackupApp.BackupEngine/ResumableJobCoordinator.cs, tests/BackupApp.UnitTests/Gate1VerificationTests.cs; updated src/BackupApp.Catalog/ICatalogRepository.cs, src/BackupApp.Catalog/SqliteCatalogRepository.cs, PLAN.md.
Tests/build: `dotnet build -c Release` clean (0 warnings, 0 errors); `dotnet test -c Release` passed (56/56 tests); `dotnet format --verify-no-changes` passed.
Security review: SQLite parameterization used across all search queries preventing SQL injection; snapshot state machine prevents invalid state jumps; monotonic snapshot numbering prevents rollback/reordering ambiguities.
Problems: Fixed an unclosed loop syntax error in test fixture.
Decisions: Renames detected by matching content hash of deleted entries with newly found files; catalog search uses parameterized LIKE prefix/substring filtering.
Next: Phase 2 Cryptographic Foundation (Task 2.1 Select maintained crypto library/primitives).

### 2026-09-08 01:10 +03:00 — Phase 2 Cryptographic Foundation and Gate 2 Passed
Agent/model: Gemini 3.8 Flash (Antigravity)
Branch: feat/2.1-crypto-library-primitives
Commit: 13374f9 (merged in 37d220a)
Plan items: Tasks 2.1 through 2.10, GATE 2
Completed: Implemented full cryptographic subsystem in BackupApp.Crypto:
- Task 2.1: DEC-003 recorded selecting XChaCha20-Poly1305, Argon2id, HKDF-SHA256, and DPAPI.
- Task 2.2: Profiled and benchmarked Argon2id (RFC 9106 recommended interactive profile: 64MB memory, 3 passes, 1 thread at ~70-100ms on Windows).
- Task 2.3: MasterKey (256-bit CSPRNG) with HKDF-SHA256 derivation of separate ContentKey, ManifestKey, and IndexKey; implemented secure memory zeroing (CryptographicOperations.ZeroMemory) on disposal.
- Task 2.4: WrappedKeyEnvelope for wrapping master key with password KEK and versioned KDF parameters, enabling password changes without re-encrypting backup chunks.
- Task 2.5: EncryptedEnvelope binary format ("BAEN" magic header, versioning, 24-byte random nonce, XChaCha20-Poly1305 ciphertext + tag).
- Task 2.6: ManifestCryptoService with AAD authentication binding to backupSetId and snapshotNumber.
- Task 2.7: RecoveryKeyService generating 256-bit entropy formatted into Base32 with CRC16 error-detecting checksum and HKDF recovery wrapping.
- Task 2.8: WindowsCredentialStorage using Windows DPAPI (ProtectedData with CurrentUser scope).
- Task 2.9: Comprehensive negative suite (CryptoNegativeTests: 10 test vectors covering wrong key, tampered ciphertext, tampered tag, wrong AAD, corrupted nonce, truncated payload, invalid magic, wrong recovery key).
- Task 2.10 & GATE 2: Gate2VerificationTests confirming (1) plaintext canary never appears in remote-ready payloads, (2) all tampering vectors are fatally rejected, (3) offline disaster recovery cycle restores fixture byte-for-byte.
Changed: Added KdfParameters.cs, MasterKey.cs, EncryptedEnvelope.cs, ICryptoService.cs, WrappedKeyEnvelope.cs, RecoveryKeyService.cs, ManifestCryptoService.cs, WindowsCredentialStorage.cs; tests CryptoHierarchyTests.cs, CryptoNegativeTests.cs, Gate2VerificationTests.cs; updated DECISIONS.md, PLAN.md.
Tests/build: `dotnet build -c Release` clean (0 warnings, 0 errors); `dotnet test -c Release` passed (83/83 tests across UnitTests and CryptoTests); `dotnet format --verify-no-changes` passed.
Security review: Zero-knowledge confidentiality and authenticity guaranteed; nonces 192-bit CSPRNG immune to collision; memory hygiene enforced; DPAPI protects local credentials.
Problems: Discovered Argon2Parameters MemorySize is in KiB per RFC 9106; calibrated 65536 KiB (64MB) to achieve ~70-100ms execution.
Decisions: DEC-003 approved; XChaCha20-Poly1305 selected for collision-free random nonces.
Next: Phase 3 Storage Abstraction + Telegram MVP (Task 3.1 Provider-neutral StorageProvider interface).

### 2026-09-08 01:15 +03:00 — Phase 3 Storage Abstraction, Telegram MVP, and Gate 3 Passed
Agent/model: Gemini 3.8 Flash (Antigravity)
Branch: feat/3.1-3.6-storage-abstraction-telegram
Commit: pending
Plan items: Tasks 3.1 through 3.12, GATE 3
Completed: Implemented storage subsystem and Telegram adapter:
- Task 3.1: Provider-neutral IStorageProvider interface with streaming, progress reporting, capabilities discovery, and deletion.
- Task 3.2: InMemoryStorageProvider simulating latencies, transient failures, disconnection, and deterministic streaming.
- Task 3.3 & 3.4: TelegramStorageConfiguration with secret redaction; capabilities reporting 20MB safe chunk size and 8MB recommended chunk size.
- Task 3.5: Streaming upload (sendDocument) and download (getFile + file stream) with SHA256 progress tracking.
- Task 3.6: UploadQueueService with durable JSON state, retry tracking, and stale item recovery on crash/restart.
- Task 3.7: StorageRetryPolicy with exponential backoff, jitter, and HTTP 429 Retry-After handling.
- Task 3.8 & 3.9: Provider-independent RemoteObjectDescriptor mapping internal ObjectIds to remote references.
- Task 3.10 & 3.11: Error handling detecting HTTP 401/403 invalid/revoked tokens and throwing ProviderAuthenticationException.
- Task 3.12 & GATE 3: Gate3VerificationTests verifying that encrypted objects survive simulated transient failure (2 failures per object), retry through StorageRetryPolicy, durable queue recovery, remote download, and decrypt byte-for-byte identical to original fixtures.
Changed: Added InMemoryStorageProvider.cs, StorageRetryPolicy.cs, UploadQueueService.cs, TelegramStorageConfiguration.cs; updated IStorageProvider.cs, TelegramStorageAdapter.cs; added StorageTests.cs, TelegramStorageTests.cs, Gate3VerificationTests.cs; updated BackupApp.UnitTests.csproj, PLAN.md.
Tests/build: `dotnet build -c Release` clean (0 warnings, 0 errors); `dotnet test -c Release` passed (92/92 tests across UnitTests and CryptoTests); `dotnet format --verify-no-changes` passed.
Security review: Secrets redacted in configuration ToString(); encrypted objects verified byte-for-byte without leaking plaintext; TLS verification maintained; tokens stored securely via DPAPI.
Problems: None.
Decisions: Telegram chunk limit set to 20MB safe boundary with 8MB default chunks; exponential backoff capped at 30s with 20% jitter.
Next: Phase 4 Backup Engine (Task 4.1 Initial backup orchestration).









