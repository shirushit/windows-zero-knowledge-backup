# Observability

Local-first diagnostics. No telemetry is enabled silently. Any future remote telemetry is opt-in/clearly disclosed and requires a product/privacy decision.

## Logs
Structured levels, timestamps, component, correlation/job ID, error class. Redact secrets and unnecessary personal file paths. Support bounded rotation.

## Metrics (local)
Queue depth, scan duration, bytes processed/uploaded, retry counts, provider throttling, restore verification failures, DB latency, memory/CPU samples. Metrics must not expose plaintext user data.
