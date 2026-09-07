# Performance

## Principles
Background work yields to the user. Bound memory, CPU, disk I/O and network concurrency. Stream large files; never load entire backups into RAM.

## Budgets
Establish measured budgets during Phase 0/1 for idle memory/CPU, active scan/hash overhead, UI responsiveness, DB query latency, and concurrency. Do not invent “zero memory usage” claims.

## Techniques
Streaming I/O, bounded queues, cancellation tokens, backpressure, batched DB operations, indexed queries, incremental scanning where reliable, adaptive throttling. Profile before optimizing.
