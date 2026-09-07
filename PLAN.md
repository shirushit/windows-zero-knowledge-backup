# MASTER BUILD PLAN

**Rule:** This file is executable project state, not a retrospective. Do not change goals to match accidental implementation. Hard direction changes require `[?]` + `DECISIONS.md` + human approval.

Current phase: 6
Current milestone: Windows UI / UX
Overall status: IN DEVELOPMENT
Last verified commit: feat/5.1-5.15-restore-engine (Gate 5 Passed)
Last updated: 2026-09-08 01:25 +03:00

## Global completion protocol
For every numbered item: set `[~]` before implementation; implement; run acceptance tests; security-check; document; commit; then set `[x]` and record commit/checkpoint. If blocked use `[!]` with reason. Continue automatically to the next unblocked item.

# PHASE 0 — FOUNDATION
- [x] **0.1 Repository bootstrap** — directories, `.gitignore`, editor config, license choice, contribution/PR templates. **Accept:** clean clone has expected structure; no secrets.
- [x] **0.2 Select Windows implementation stack** — evaluate native UX, background/service support, installer, crypto/library maturity, maintainability. **Accept:** decision recorded. If choice fundamentally conflicts with approved architecture, `[?]`.
- [x] **0.3 Application skeleton** — modules/boundaries from `ARCHITECTURE.md`. **Accept:** clean build.
- [x] **0.4 Dependency governance** — lockfiles, update policy, license/vulnerability review. **Accept:** reproducible dependency restore.
- [x] **0.5 CI bootstrap** — build/lint/unit/secret/dependency checks. **Accept:** PR CI passes on clean branch and fails a deliberate test fixture.
- [x] **0.6 Git protections/workflow** — protected main, PR template/checks. **Accept:** direct unsafe merge path prevented where hosting supports it.
- [x] **0.7 Baseline performance budgets** — measure/define realistic idle and active budgets. **Accept:** budgets recorded in `PERFORMANCE.md`.

**GATE 0:** clean clone → dependency restore → build → tests/CI succeeds.

# PHASE 1 — DOMAIN + LOCAL CATALOG
- [x] **1.1 Domain models and invariants**
- [x] **1.2 SQLite/catalog schema + migrations**
- [x] **1.3 Canonical internal path model**
- [x] **1.4 File discovery with exclusions**
- [x] **1.5 Metadata capture and stable-read detection**
- [x] **1.6 Content hashing streaming implementation**
- [x] **1.7 Change detection: new/changed/unchanged/deleted/renamed policy**
- [x] **1.8 Snapshot/version state machine**
- [x] **1.9 Persistent resumable job model**
- [x] **1.10 Local search indexes**

**GATE 1:** deterministic fixture scan twice yields correct incremental delta; crash/restart leaves catalog consistent.

# PHASE 2 — CRYPTOGRAPHIC FOUNDATION
- [x] **2.1 Select maintained crypto library/primitives** — document exact choices and rationale.
- [x] **2.2 Password KDF benchmark + versioned parameters**
- [x] **2.3 Master key generation and key hierarchy**
- [x] **2.4 Wrapped key storage format**
- [x] **2.5 Versioned AEAD object envelope**
- [x] **2.6 Encrypted/authenticated manifest/catalog export format**
- [x] **2.7 Recovery-key flow**
- [x] **2.8 Secure credential/key persistence using supported Windows facilities where appropriate**
- [x] **2.9 Crypto negative tests: wrong key, tamper, truncation, wrong AAD/version**
- [x] **2.10 Threat-model review**

**GATE 2:** plaintext fixture never appears in remote-ready payload; tampering is reliably detected; key/recovery tests pass. No production remote upload before this gate.

# PHASE 3 — STORAGE ABSTRACTION + TELEGRAM MVP
- [x] **3.1 Provider-neutral `StorageProvider` interface**
- [x] **3.2 In-memory/fake provider for deterministic tests**
- [x] **3.3 Telegram adapter authentication/configuration**
- [x] **3.4 Provider capability/limit discovery and validation**
- [x] **3.5 Streaming upload/download**
- [x] **3.6 Persistent upload queue**
- [x] **3.7 Retry/backoff/jitter + rate-limit handling**
- [x] **3.8 Chunk/object sizing strategy based on current provider constraints**
- [x] **3.9 Remote-reference mapping isolated from domain**
- [x] **3.10 Remote acknowledgement/verification semantics**
- [x] **3.11 Lost/revoked provider credential behavior**
- [x] **3.12 Integration test environment without exposing secrets to untrusted PRs**

**GATE 3:** encrypted test objects survive upload/download/interruption/retry and verify byte-for-byte after decrypt.

# PHASE 4 — BACKUP ENGINE
- [x] **4.1 Initial backup orchestration**
- [x] **4.2 Incremental backup orchestration**
- [x] **4.3 Streaming encryption/upload pipeline with backpressure**
- [x] **4.4 Atomic snapshot commit only after required remote success**
- [x] **4.5 Pause/resume/cancel**
- [x] **4.6 Network-loss recovery**
- [x] **4.7 Process/power-interruption recovery**
- [x] **4.8 File changes during backup**
- [x] **4.9 Locked/unreadable file policy**
- [x] **4.10 Rename/delete/history behavior**
- [x] **4.11 Conservative dedupe decision/implementation**
- [x] **4.12 Scheduler and startup behavior**
- [x] **4.13 CPU/disk/network throttling**

**GATE 4:** large deterministic dataset backs up, modifies incrementally, survives forced termination, resumes, and reports truthful protected/failed state. [x]

# PHASE 5 — RESTORE ENGINE
- [x] **5.1 Unlock/authentication flow**
- [x] **5.2 Remote catalog/manifest discovery and retrieval**
- [x] **5.3 Manifest authenticity/integrity verification**
- [x] **5.4 Backup tree browsing API**
- [x] **5.5 Search API**
- [x] **5.6 Single-file restore**
- [x] **5.7 Folder restore**
- [x] **5.8 Full supported-data restore**
- [x] **5.9 Alternate restore root**
- [x] **5.10 Path traversal/device-path defense**
- [x] **5.11 Conflict policy: overwrite/rename/skip**
- [x] **5.12 Temp-write → verify → atomic placement**
- [x] **5.13 Supported timestamp/metadata restoration**
- [x] **5.14 Interrupted restore resume/retry**
- [x] **5.15 Corruption/missing-object reporting**

**GATE 5 — DISASTER RECOVERY:** on a clean environment, install app, unlock using only intended recovery material, discover backup, restore fixture, verify hashes and supported metadata. [x]

# PHASE 6 — WINDOWS UI / UX
- [ ] **6.1 Design tokens/theme light+dark**
- [ ] **6.2 RTL/localization infrastructure**
- [ ] **6.3 Onboarding and recovery-warning UX**
- [ ] **6.4 Folder selection/exclusions**
- [ ] **6.5 Home/status screen with truthful state model**
- [ ] **6.6 First-backup progress**
- [ ] **6.7 System tray/background controls**
- [ ] **6.8 Backup browser/tree/list virtualization**
- [ ] **6.9 Fast local search UI**
- [ ] **6.10 Restore selection/destination/conflicts**
- [ ] **6.11 Settings: schedule, resource/network controls, provider account**
- [ ] **6.12 Errors/retry/needs-attention flows**
- [ ] **6.13 Notifications**
- [ ] **6.14 Keyboard/accessibility/screen-reader/reduced-motion audit**
- [ ] **6.15 Mixed RTL/LTR path rendering audit**

**GATE 6:** core backup/restore can be completed without CLI and without ambiguous status; accessibility/RTL checks pass.

# PHASE 7 — HARDENING
- [ ] **7.1 Full unit suite and coverage review**
- [ ] **7.2 Integration suite**
- [ ] **7.3 E2E backup/restore suite**
- [ ] **7.4 Unicode/Hebrew/long-path fixture suite**
- [ ] **7.5 Large-file/large-tree tests**
- [ ] **7.6 Low-disk/memory/network-failure tests**
- [ ] **7.7 Corrupt DB/catalog/remote-object scenarios**
- [ ] **7.8 Wrong-password/tamper/replay scenarios**
- [ ] **7.9 Dependency/static/secret security review**
- [ ] **7.10 Manual threat-model review**
- [ ] **7.11 Performance profiling against budgets**
- [ ] **7.12 Logging/privacy audit**

**GATE 7:** no unresolved critical/high security defect accepted for release; recovery suite repeatedly passes.

# PHASE 8 — INSTALLATION, UPDATE, RELEASE
- [ ] **8.1 Windows installer**
- [ ] **8.2 Clean install/uninstall smoke tests**
- [ ] **8.3 Upgrade/migration test strategy**
- [ ] **8.4 Code-signing configuration**
- [ ] **8.5 Secure update strategy** — hard decision if it introduces a new mandatory server/service.
- [ ] **8.6 Versioning + build metadata**
- [ ] **8.7 SBOM + checksums**
- [ ] **8.8 Release CI pipeline**
- [ ] **8.9 Rollback procedure**
- [ ] **8.10 User-facing recovery/security documentation**

# PHASE 9 — RELEASE GATE
- [ ] Clean installation succeeds.
- [ ] First backup succeeds.
- [ ] Incremental backup succeeds without unnecessary re-upload.
- [ ] Forced interruption resumes safely.
- [ ] Full clean-machine disaster recovery succeeds.
- [ ] Restored hashes match source fixture.
- [ ] Supported timestamps/metadata match expected values.
- [ ] Wrong password fails safely.
- [ ] Ciphertext/manifest tampering is detected.
- [ ] CI/release pipeline green.
- [ ] No secrets in tracked repository/history scan.
- [ ] No unresolved critical/high security issue.
- [ ] Dependency/license review complete.
- [ ] Installer/signature/checksum verified.
- [ ] Documentation matches implementation.
- [ ] `DECISIONS.md` has no unresolved release-blocking `[?]` decisions.

**Only after every mandatory release item is `[x]` may the human developer approve production release.**
