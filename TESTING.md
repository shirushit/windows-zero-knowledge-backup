# Testing Strategy

## Layers
Unit tests for pure policies/models; property/fuzz-style tests for parsers/path/envelope boundaries where practical; integration tests for DB/crypto/provider adapter; end-to-end backup→remote fixture→clean restore; installer/update smoke tests.

## Mandatory fixtures
Empty files, Unicode/Hebrew names, long names/paths, large files, duplicate content, rapid modification, rename/delete, locked files, interrupted upload/download, corrupted/truncated ciphertext, wrong password, provider rate limit, DB migration, low disk space, network loss.

## Release invariant
Every release candidate runs a deterministic disaster-recovery test and verifies restored file hashes plus supported metadata. A backup-only test is insufficient.

## CI
Tests must be deterministic; external Telegram tests should use a controlled integration environment/secret and not be required for every untrusted PR if secrets cannot be safely exposed. Provider-neutral tests use fakes.
