# Release & Versioning

Use semantic versioning once public compatibility matters. Pre-1.0 versions may change formats but migrations must be deliberate. Every backup/envelope/schema format is independently versioned where needed.

## Release checklist
Green CI; no critical vulnerabilities; migration tests; clean install/uninstall; upgrade from supported prior version; backup + incremental + clean restore; wrong-password/tamper behavior; installer signature/checksum; release notes; rollback path.

## Compatibility
Never ship a change that makes existing backups unreadable without a tested migration/compatibility strategy and explicit human approval.
