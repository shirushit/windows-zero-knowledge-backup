# Error Handling

Classify errors as transient/retryable, user-action-required, permanent item failure, provider/auth failure, integrity/security failure, local resource failure, internal bug.

Retries are bounded with backoff/jitter and must not hide persistent failures. Integrity/authentication failures are never auto-ignored. Persist enough job state to resume safely. UI messages avoid raw stack traces and secrets but logs retain redacted diagnostic context/correlation IDs.

Partial success must be represented honestly: a snapshot is not “protected” if required objects failed.
