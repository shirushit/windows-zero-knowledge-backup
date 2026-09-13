# Windows Zero-Knowledge Encrypted Backup Application (BackupApp)

An enterprise-grade, zero-knowledge, end-to-end encrypted desktop backup solution for Windows (.NET 8 WPF).
All sensitive data, file names, paths, and content chunks are encrypted locally using **XChaCha20-Poly1305** and **Argon2id** key derivation before leaving the machine. Remote storage providers (such as Telegram Cloud or any pluggable backend) see only encrypted opaque envelopes with zero access to keys, plaintext, or directory structures.

---

## Key Features & Architectural Capabilities

### 1. Zero-Knowledge Cryptography & Memory Hygiene
- **XChaCha20-Poly1305 (AEAD):** 256-bit symmetric encryption with 192-bit random nonce safety (negligible collision probability across exabytes).
- **Argon2id KDF:** High-memory, CPU-hardened key derivation protecting master keys from brute force attacks.
- **DPAPI Credential Storage:** API tokens and session credentials stored locally via Windows Data Protection API (DPAPI - CurrentUser scope).
- **Memory Zeroization:** All plaintext buffers, unsealed chunk arrays, and intermediate cryptographic secrets are zeroized in memory via `CryptographicOperations.ZeroMemory` inside deterministic `finally` / `IDisposable` scopes.

### 2. High-Performance FastCDC & Global Deduplication
- **FastCDC (Content-Defined Chunking):** Variable-sized chunking utilizing 64-bit Gear Matrix rolling hashing with dual-mask normal/small boundary detection (min: 256 KB, avg: 1 MB, max: 4 MB). Eliminates shift-boundary re-uploads when edits occur inside files.
- **Global SQLite Content Catalog:** Chunks are indexed by SHA-256 digests. Duplicate chunks across files, snapshots, or revisions are uploaded exactly once.
- **Point-in-Time Restore:** Full snapshot history tracking every point-in-time backup run. Users can select any historical snapshot and restore the file tree as it existed at that exact moment.

### 3. Asynchronous Producer-Consumer Pipeline
- **System.Threading.Channels Pipeline:** Multi-stage producer-consumer architecture (`ReadChannel -> EncryptChannel -> UploadChannel`) decoupling disk I/O, CPU cryptographic transformation, and network bandwidth.
- **Resumable Uploads & Deduplication Skip:** Chunks already confirmed in remote storage are verified and skipped without redundant transfer.

### 4. Resilient Network & Chaos Hardening
- **Telegram Bot API Transport:** Automatic Semaphore-based rate limiting (20-25 req/min) preventing API bans.
- **Exponential Backoff & 429 Retry-After:** Built-in handling of Telegram HTTP 429 with dynamic wait parsed directly from response headers.
- **Chaos Test Suite (`BackupApp.StressTests`):** Full end-to-end resilience validation simulating network TCP resets, randomized latency, HTTP 500 server crashes, rate limits, Hebrew and deeply nested unicode paths, locked active files, and bit-for-bit SHA-256 roundtrip restore verification.

### 5. Windows 11 Fluent UI & Instant Previews
- **Modern Fluent Design:** Segoe UI Variable typography, soft card containers, responsive progress tracking, and live speed meter (`KB/s`, `MB/s`, remaining ETA).
- **Integrated File Previewer:** Instant preview panel in the file browser tab showing image thumbnails, text/source code snippets, and metadata badges (size, modification date, chunk count, SHA-256 fingerprint).
- **Direct System Decryption & Launch:** Double-click or click "Open File" to decrypt on-the-fly to a secure sandbox and launch the native Windows default application.
- **Background Tray Minimization & Scheduling:** System tray icon with context menu ("Open", "Run Backup Now", "Exit"), customizable schedules (Hourly, Daily, or FileSystemWatcher real-time change trigger with 10s debounce).

### 6. One-Click Instant Desktop Launcher
- Dedicated launcher at `C:\Users\owner\Desktop\BackupApp-Launcher` with:
  - `BackupApp.exe` (Self-contained, single-file win-x64 executable)
  - `Start-BackupApp.bat` (Clean environment launcher)
  - Desktop Shortcut `BackupApp.lnk`

---

## Quickstart & Execution

### Running the Application
Double-click the desktop shortcut **BackupApp** or run the launcher:
```cmd
"C:\Users\owner\Desktop\BackupApp-Launcher\Start-BackupApp.bat"
```

Or run via .NET CLI:
```powershell
dotnet run --project src/BackupApp.UI
```

### Running the Test Suites
Run all 159 unit, crypto, and chaos stress tests:
```powershell
dotnet test BackupApp.sln
```

### Building Release Packages
To compile a single-file, self-contained executable with SHA-256 checksums and SBOM:
```powershell
powershell.exe -ExecutionPolicy Bypass -File .\scripts\build-release.ps1
```
Output binaries and archives are placed in `artifacts/release/`:
- `BackupApp.UI.exe` (Single-file self-contained win-x64 executable)
- `BackupApp-v1.0.0-win-x64.zip` (Distribution release ZIP)
- `SHA256SUMS.txt` (SHA-256 cryptographic verification file)
- `SBOM.txt` (Software Bill of Materials)

---

## Repository Governance & Architecture Docs
- `AGENTS.md` — Autonomous agent rules & execution contract.
- `PRODUCT.md` — Product vision, constraints, and operational requirements.
- `SECURITY.md` — Threat model and Zero-Trust architecture.
- `CRYPTOGRAPHY.md` — Cryptographic specifications, algorithms, and key management.
- `ARCHITECTURE.md` — Component boundaries and data flow.
- `PLAN.md` — Progress tracker and milestone checklists.
- `DECISIONS.md` — Architectural Decision Records (ADRs).
- `RESEARCH_LOG.md` — Chronological engineering log.
