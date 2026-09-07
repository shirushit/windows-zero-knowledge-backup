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







