# Cryptography Design Requirements

This document specifies constraints, not permission to invent a protocol. Prefer mature, audited libraries and standard primitives. Exact primitive/library selection must be recorded before implementation and reviewed.

## Required properties
Confidentiality + integrity/authenticity; unique nonces where required; cryptographic versioning; key separation; password change without re-encrypting all content when feasible; recovery design that does not give the provider plaintext keys.

## Key hierarchy
Use a randomly generated master/data-root secret protected by a key-encryption key derived from the user's password. Derive separate subkeys for content, manifests/catalog authentication/encryption, and other roles using a standard KDF. Store salts, non-secret KDF parameters, format versions, and nonces as needed. Never store the password.

## Password KDF
Use a memory-hard password KDF such as Argon2id through a maintained library. Parameters must be benchmarked on supported hardware, versioned, and upgradeable. Do not hard-code “AES-256” as the whole security design.

## AEAD
Use a standard AEAD construction exposed safely by the chosen library (for example AES-256-GCM or XChaCha20-Poly1305 depending on platform/library review). Nonce strategy must be explicit and tested. Authentication failure is fatal for that object.

## Recovery
A recovery key/phrase, if implemented, must be generated with cryptographic randomness, shown deliberately, never logged, and wrapped/handled so the storage provider cannot decrypt user data. Loss of both password/recovery material may mean permanent data loss; UX must state this clearly.

## Tests
Known-answer/library vectors where applicable; round trips; wrong key/password; modified ciphertext/tag/AAD; nonce uniqueness strategy; version migration; corrupted/truncated objects; KDF parameter upgrade.
