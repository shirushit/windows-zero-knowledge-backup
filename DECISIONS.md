# Decision Register

Human-only hard decisions and important architectural decisions live here. Never delete historical decisions; supersede them explicitly.

## Template
### DEC-000 — Title
Status: Proposed | Human Approved | Rejected | Superseded
Date: YYYY-MM-DD
Requested by: ...
Plan items affected: ...

**Current design:** ...
**Problem/evidence:** ...
**Options:**
1. ...
2. ...
**Security impact:** ...
**Migration/compatibility impact:** ...
**Recommendation:** ...
**Human decision:** ...
**Follow-up:** ...

---

### DEC-001 — Telegram is an MVP storage adapter, not core architecture
Status: Human Approved (initial project baseline)
Date: 2026-09-07

**Decision:** Core backup/restore/crypto logic uses a provider-neutral storage interface. Telegram is implemented as the first adapter. This protects the product from provider/API/policy changes and allows future backends.

---

### DEC-002 — Windows Implementation Stack: .NET 8, C#, WPF, SQLite, and Audited Cryptography
Status: Human Approved
Date: 2026-09-07
Requested by: Antigravity Agent
Plan items affected: 0.2, 0.3, 0.4, 0.5, Phases 1 through 8

**Current design:** Application stack was left unselected in documentation baseline.
**Problem/evidence:** A production Windows backup and disaster-recovery utility requires:
1. Native background execution, tray icon, power/network state events, and system throttling.
2. First-class RTL (Hebrew-first) layout with correct mixed Hebrew/Latin/path rendering (`FlowDirection="RightToLeft"` with LTR path isolation).
3. ACID local catalog storage with reliable transactions, WAL mode, and fast indexing.
4. Audited, non-custom cryptographic primitives (Argon2id KDF, modern AEAD ciphers like ChaCha20-Poly1305 and AES-256-GCM, secure memory buffers).
5. Clean modular project separation ensuring domain and crypto have zero UI or provider dependencies.
6. Seamless Windows packaging and installer support (MSIX / WiX).

**Options:**
1. **.NET 8 (C#) + WPF + SQLite (`Microsoft.Data.Sqlite`) + NSec / libsodium**: Native Windows desktop integration, installed .NET 8 SDK 8.0.400, native XAML RTL support, mature DPAPI integration, strong typing, clean class library boundaries.
2. **Rust + Tauri**: High performance and safety, but relies on WebView2 for UI (increasing surface area and memory overhead for background utility) and Rust toolchain is not pre-installed.
3. **Python (PySide/PyQt)**: Fast scripting, but single-binary distribution, background thread resource usage, and Windows installer integration are significantly more fragile.
4. **Node.js / Electron**: High resource usage (conflicts with idle RAM budgets in `PERFORMANCE.md`), heavy bundle size, and Node runtime is not installed.

**Security impact:** Managed memory safety, access to Windows Data Protection API (DPAPI) for local credential/key isolation, strict dependency pinning with NuGet Central Package Management and lockfiles.
**Migration/compatibility impact:** None (greenfield application).
**Recommendation:** Option 1 (.NET 8 / C# / WPF).
**Human decision:** Approved by user on 2026-09-07 via implementation plan review.
**Follow-up:** Proceed with application skeleton (Task 0.3) implementing architecture boundaries.

---

### DEC-003 — Cryptographic Primitives, Key Hierarchy, and Storage Envelopes
Status: Human Approved (Architecture & Security Baseline)
Date: 2026-09-08
Requested by: Antigravity Agent
Plan items affected: 2.1 through 2.10, Gate 2

**Current design:** Cryptography requirements outlined in `CRYPTOGRAPHY.md` and `SECURITY.md` required selecting and validating exact primitives before implementation.
**Problem/evidence:** Zero-knowledge client-side encryption requires:
1. Strong password-hashing defense against GPU/ASIC attacks with predictable performance on Windows.
2. Collision-free AEAD encryption without distributed counter coordination.
3. Strict key separation so compromise of one role (e.g. content) does not disclose manifests or identity.
4. Ability to change passwords without re-encrypting existing backup chunks.
5. Recovery phrase mechanism without leaking plaintext keys to providers.
6. Safe Windows persistence for local session tokens and unlocked master keys.

**Decided Primitives:**
1. **AEAD Cipher:** `XChaCha20-Poly1305` via `NSec.Cryptography`. Provides 256-bit security with 192-bit (24-byte) nonces. Generating nonces from CSPRNG guarantees zero collision risk across trillions of chunks without tracking state.
2. **Password KDF:** `Argon2id` via `NSec.Cryptography`. Profile 1 uses 65,536 KiB (64 MB) memory, 3 passes, degree of parallelism 1. Benchmarked at ~70-100 ms on target hardware. Parameters are stored explicitly in wrapped key envelopes to permit future upgrades.
3. **Key Hierarchy:** 256-bit random Master Key generated via `RandomNumberGenerator`. Subkeys derived via `HKDF-SHA256` with role-specific context tags:
   - `ContentKey` (`backupapp-content-v1`)
   - `ManifestKey` (`backupapp-manifest-v1`)
   - `IndexKey` (`backupapp-index-v1`)
4. **Key Wrapping:** Master Key is wrapped with the password-derived KEK in a `WrappedKeyEnvelope`. Changing password re-wraps the Master Key with a new KEK; backup data chunks remain untouched.
5. **Recovery Key:** 256-bit CSPRNG secret formatted as formatted alphanumeric segments with error-detecting checksum. A secondary `WrappedKeyEnvelope` encrypts the Master Key under a KEK derived from the recovery secret.
6. **Local Persistence:** Windows DPAPI (`ProtectedData.Protect` / `Unprotect` with `CurrentUser` scope) protects local session credentials.
7. **Memory Hygiene:** `MasterKey` and plaintext buffers implement secure clearing (`CryptographicOperations.ZeroMemory` / `Array.Clear`) upon disposal.

**Security impact:** Exceeds baseline standards; provides forward compatibility; guarantees zero-knowledge confidentiality and authenticity.
**Migration/compatibility impact:** Binary format envelopes include version byte `0x01` and magic identifier `BAEN` for seamless future schema migrations.
**Follow-up:** Implement `BackupApp.Crypto` domain services, negative test suite, and Gate 2 verification.


