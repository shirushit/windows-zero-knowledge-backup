# Performance

## Principles
Background work yields to the user. Bound memory, CPU, disk I/O and network concurrency. Stream large files; never load entire backups into RAM.

## Budgets
Establish measured budgets during Phase 0/1 for idle memory/CPU, active scan/hash overhead, UI responsiveness, DB query latency, and concurrency. Do not invent “zero memory usage” claims.

### 1. Idle Background Budget (Minimized / System Tray)
- **Working Set RAM**: < 50 MB
- **CPU Utilization**: < 0.2% on modern multi-core Windows machine
- **Disk I/O**: 0 B/s (no disk polling; filesystem watchers use push events where applicable)
- **Network I/O**: 0 B/s

### 2. Active Scan & Hashing Overhead
- **Working Set RAM**: <= 150 MB (streaming hashing via 64 KB – 1 MB buffer windows; no in-memory whole-file buffers)
- **CPU Utilization**: Bounded by background throttling policy (default maximum: 25% total system CPU / equivalent of 1 core)
- **Disk Read Throughput**: Throttled to <= 25 MB/s during user-active background mode, unthrottled during explicit on-demand user backup

### 3. Active Encryption & Remote Upload Overhead
- **Working Set RAM**: <= 200 MB (pipeline queue bounded to 4-8 chunk buffers in flight)
- **Network Concurrency**: Maximum 2 parallel upload streams to respect Telegram API rate limiting and avoid user bandwidth starvation
- **Local Spill/Temp Disk Space**: Cleaned up immediately after upload verification; bounded to configured chunk buffer size

### 4. UI Responsiveness & Frame Rate
- **Dispatcher/UI Latency**: UI interaction to response < 50 ms
- **Frame Rate**: Continuous 60 fps without jitter during active backup/restore (zero file I/O or crypto executed on UI thread)
- **Local Search Latency**: < 100 ms for up to 100,000 indexed files using SQLite FTS/indexed queries

### 5. Restore Performance
- **Peak RAM during Restore**: <= 150 MB (streaming decryption directly to temporary scratch file)
- **Verification Overhead**: Single-pass integrity check before atomic rename/move into target path

## Techniques
Streaming I/O, bounded queues, cancellation tokens, backpressure, batched DB operations, indexed queries, incremental scanning where reliable, adaptive throttling. Profile before optimizing.

