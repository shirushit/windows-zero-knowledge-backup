# Architecture

## Style
Modular desktop application with clean dependency boundaries. Core backup/crypto/domain code must not depend on Telegram UI/API details.

## Suggested logical modules
- `domain`: immutable models, policies, use cases.
- `scanner`: file discovery, exclusions, metadata.
- `catalog`: local DB, manifests, versions, indexes.
- `crypto`: KDF/key hierarchy, AEAD encryption, secure buffers.
- `backup`: change detection, chunking, dedupe policy, jobs.
- `restore`: browse, fetch, verify, decrypt, materialize.
- `storage`: provider interface and provider-neutral retry semantics.
- `storage-telegram`: Telegram adapter.
- `scheduler`: background jobs/pause/resume/throttling.
- `app-ui`: RTL desktop UI/tray/settings.
- `platform-windows`: filesystem, credentials, startup, installer integration.

## Dependency direction
UI/provider/platform adapters depend inward on domain interfaces. Domain never imports UI/provider SDKs. Crypto primitives are wrapped behind a narrow reviewed interface.

## StorageProvider contract
Must support capability discovery and provider identifiers rather than leaking Telegram message IDs into domain models. Operations should include put object, get object, verify/existence where feasible, delete only if product policy permits, enumerate/recover catalog anchors where required, and report retryable vs permanent failures.

## Backup pipeline
Discover → normalize metadata → compare catalog → stable-read strategy → hash/chunk if needed → encrypt locally → upload encrypted objects → verify remote acknowledgement/integrity metadata → atomically commit manifest/catalog state. Never mark a backup complete before durable catalog state exists.

## Restore pipeline
Authenticate/unlock → obtain catalog/manifests → verify authenticity → browse/select → fetch encrypted objects → integrity check/decrypt → safe path validation → write temp file → verify → atomically place → restore supported metadata.

## Crash safety
Jobs are idempotent or resumable. DB writes use transactions. Partial uploads are never presented as completed versions. Recovery after process/power/network interruption is a first-class requirement.
