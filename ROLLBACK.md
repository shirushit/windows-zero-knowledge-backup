# Release Rollback & Disaster Recovery Procedure

This document specifies the mandatory rollback, downgrade, and disaster recovery procedures for the Windows Zero-Knowledge Backup Application.

---

## 1. Principles of Rollback & Zero-Knowledge Invariants

1. **Storage Immutability**:
   * Chunks and manifests stored in remote Telegram storage are immutable and authenticated via AEAD tags (`XChaCha20-Poly1305`).
   * A client rollback or downgrade to an earlier release binary must **never** corrupt, re-encrypt, or delete existing remote storage chunks.
2. **Local Catalog Isolation**:
   * The local SQLite catalog database (`%LOCALAPPDATA%\BackupApp\catalog.sqlite`) maintains local state.
   * If an upgrade introduced a schema migration, downgrading the executable requires catalog safety checks or restoration from the automatic pre-migration backup.
3. **Secret Protection**:
   * Rollback actions must never log, output, or expose user credentials, master passwords, BIP-39 recovery phrases, or Telegram Bot Tokens.

---

## 2. Emergency Rollback Procedures

### Scenario A: Rollback After Defective Release Upgrade (Single-Machine)

If a newly deployed version (e.g., `v1.0.1`) encounters runtime instability or regressions:

1. **Terminate Running Instances**:
   ```powershell
   Stop-Process -Name "BackupApp.UI" -Force -ErrorAction SilentlyContinue
   ```
2. **Backup Local Catalog & Settings**:
   ```powershell
   $AppData = "$env:LOCALAPPDATA\BackupApp"
   $RollbackBackup = "$env:LOCALAPPDATA\BackupApp_pre_rollback_$(Get-Date -Format 'yyyyMMddHHmmss')"
   Copy-Item -Path $AppData -Destination $RollbackBackup -Recurse -Force
   ```
3. **Restore Prior Working Executable**:
   * Copy the known-good release binary (e.g., `v1.0.0`) back to `%LOCALAPPDATA%\Programs\BackupApp\BackupApp.UI.exe`.
4. **Catalog Schema Verification**:
   * If the defective version migrated the catalog schema, revert `catalog.sqlite` from the pre-upgrade snapshot (`catalog.sqlite.bak`) created automatically before migrations.
5. **Verify Clean Startup**:
   * Launch `BackupApp.UI.exe`.
   * Verify unlocked status and ensure existing snapshots and files are visible in the browsing tree.

---

### Scenario B: Catastrophic Machine Failure (Disaster Recovery from Scratch)

If the local machine was lost, formatted, or suffered hardware failure:

1. **Deploy Fresh Release Binary**:
   * Download `BackupApp.UI.exe` to the replacement computer.
2. **Launch Clean-Room Disaster Recovery Mode**:
   * Select **שחזור מאסון (Disaster Recovery)** on the UI welcome screen.
   * Provide the remote storage credentials:
     - `Bot Token`
     - `Chat ID`
   * Provide the master password **OR** the 12/24-word BIP-39 recovery phrase.
3. **Automated Discovery**:
   * The application downloads `manifest-*.bin` objects (`BMAM` authenticated envelope).
   * Verifies AEAD authentication tag and decrypts the latest snapshot catalog.
4. **Target Restoration**:
   * Select restore directory.
   * Chunks are downloaded, verified against SHA-256 and Poly1305 MAC, and restored byte-for-byte with original timestamps.

---

### Scenario C: Compromised Storage Token or Infrastructure Rotation

If a Telegram Bot Token is compromised or revoked:

1. Create a new Bot and private channel in Telegram.
2. In the application settings, update to the new Bot Token and Chat ID.
3. Trigger a fresh full backup into the new storage location.
4. Old remote storage objects can be purged by deleting the old Telegram channel.

---

## 3. Rollback Checklist

- [ ] Target processes stopped before modifying binaries.
- [ ] Local `%LOCALAPPDATA%\BackupApp` backed up.
- [ ] Prior binary verified with `SHA256SUMS.txt`.
- [ ] Catalog opened and verified integrity (`PRAGMA integrity_check`).
- [ ] Test restore of 1 sample file performed before resuming scheduled operations.
