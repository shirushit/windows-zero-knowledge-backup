# Security Policy & Threat Model

## Assets
User file content, filenames/path structure, metadata, encryption keys, credentials/tokens, backup catalog, recovery material.

## Threats considered
Compromised/curious storage provider; intercepted traffic; stolen remote credentials; corrupted/tampered remote objects; lost/stolen PC; malicious backup payload/path traversal during restore; dependency compromise; accidental secret logging; rollback/replay of manifests.

## Mandatory controls
- Encrypt authenticated payloads locally before upload.
- Use modern authenticated encryption; never unauthenticated AES modes.
- Password-derived secrets use a memory-hard KDF with versioned parameters.
- Separate key roles via a documented key hierarchy.
- TLS validation stays enabled.
- Secrets never enter source control or ordinary logs.
- Restore paths are canonicalized and constrained to the chosen restore root.
- Treat remote metadata/content as untrusted input.
- Verify integrity/authenticity before materializing restored files.
- Sensitive temporary plaintext is minimized and deleted best-effort.
- Dependency pinning/lockfiles, vulnerability scanning, secret scanning, signed/reproducible release practices where practical.

## Metadata privacy
Do not assume encrypted file bytes alone provide zero knowledge. Plaintext filenames, paths, manifests, hashes, sizes, and timestamps may leak information. Encrypt sensitive catalog/manifests and document unavoidable leakage. Avoid deterministic content identifiers exposed remotely unless the leakage is explicitly accepted.

## Logging
Structured logs with redaction. Default logs must not contain file content, passwords, raw keys/tokens, recovery secrets, or unnecessary full paths. Debug logging must not weaken this rule.

## Security review triggers
Any crypto change, authentication/recovery change, provider credential handling, manifest format change, update mechanism, installer privilege change, or new network service.

## Incident posture
Fail closed on authentication/integrity failures. Surface actionable errors; never silently substitute corrupted data.
