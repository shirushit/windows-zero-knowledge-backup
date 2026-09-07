# Product Definition

## Mission
Provide a simple Windows backup and disaster-recovery application that encrypts data locally before remote storage and lets a user recover files onto a clean computer.

## Product principles
- Zero-knowledge by design: storage providers must not receive usable plaintext file content.
- Restore is the product: a backup that cannot be reliably restored is a failure.
- Local-first metadata and encryption.
- Low background resource usage and graceful throttling.
- Hebrew-first RTL UX, while architecture remains localization-ready.
- Storage-provider abstraction: Telegram is the first MVP backend, not a hard-coded core dependency.
- No claim of unlimited/free storage is a technical invariant; provider limits and policies can change.

## MVP
Select important folders; initial and incremental backup; background scheduling; encrypted remote upload; local catalog; backup browser/search; restore single file/folder/all; preserve hierarchy and relevant timestamps; clear status/errors; installer.

## Explicit non-goals for MVP
Full disk imaging, bootable bare-metal OS recovery, application-state restoration, Windows registry cloning, cross-platform clients, enterprise fleet management, public file sharing, collaboration. “Restore the computer exactly as it was” in MVP means backed-up user data/structure/metadata within supported scope—not an OS image.

## Success metrics
Correctness and recoverability > speed > storage efficiency > feature count. A release must pass deterministic restore drills and integrity verification.
