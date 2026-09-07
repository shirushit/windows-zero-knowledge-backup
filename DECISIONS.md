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

