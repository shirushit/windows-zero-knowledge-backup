# CI / CD

## Pull-request CI
Formatting/lint → compile/build → unit tests → integration tests not requiring protected secrets → dependency/license checks → secret scan → static/security analysis → artifact smoke checks. Fail closed on required checks.

## Main branch
Repeat required checks, produce versioned unsigned/signed candidate as configured, retain test reports/SBOM/checksums. Do not auto-release security-sensitive binaries solely because code merged.

## Release pipeline
Tag/version validation → clean build → full test suite → disaster-recovery E2E → vulnerability/secret scan → SBOM → sign installer/binaries when signing is configured → checksum → publish release → release notes.

## Secrets
CI secrets are least-privilege, environment-scoped, rotated, never printed, unavailable to untrusted fork PRs. Telegram integration credentials belong only in protected integration/release contexts.

## Supply chain
Pin actions/tool versions where possible, lock dependencies, review automated dependency updates, generate SBOM, avoid executing arbitrary downloaded scripts.
