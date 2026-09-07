# Remote Storage

## Principle
Remote storage is an untrusted object store. Provider-specific identifiers stay inside adapters/mappings. The backup format must remain portable enough to add another backend.

## Telegram MVP adapter
Use only documented/approved interfaces and design for file-size limits, rate limits, transient failures, revoked credentials, deleted messages/channel access, API changes, and provider terms/policies. Never describe Telegram capacity as an architectural guarantee.

## Object format
Every remote object is encrypted before upload and has a versioned envelope containing only necessary non-secret routing/version data. Sensitive manifest metadata is encrypted. Chunks/objects have integrity protection.

## Reliability
Bounded concurrency, exponential backoff with jitter, resumable/idempotent semantics where feasible, persistent upload queue, explicit permanent-error state, provider capability probing.

## Portability
Define provider-neutral object IDs in the catalog and map them to Telegram identifiers. Future providers must be addable without touching scanner/crypto/restore domain logic.
