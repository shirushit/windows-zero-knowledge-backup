# Contributing Guidelines

Thank you for contributing to the Zero-Knowledge Windows Backup project.

## Development Environment
- Windows 10/11 (x64)
- .NET 8 SDK (8.0.400 or later)
- Git (configured with LF or CRLF as defined in `.editorconfig`)

## Rules of the Repository
1. **Contract First**: Read and adhere strictly to `AGENTS.md`, `PRODUCT.md`, `ARCHITECTURE.md`, `SECURITY.md`, and `CRYPTOGRAPHY.md`.
2. **No Secret Commits**: Never commit tokens, credentials, keys, or plaintext backup payloads.
3. **Plan State**: Before implementing, ensure your item is tracked in `PLAN.md` with status `[~]`.
4. **Research Log**: Upon completing a milestone or checkpoint, append an entry to `RESEARCH_LOG.md`.

## Branching & Commit Workflow
- Create branches off `main` using standard prefixes:
  - `feat/<task>-description`
  - `fix/<issue>-description`
  - `security/<issue>-description`
  - `docs/<task>-description`
  - `chore/<task>-description`
- Follow Conventional Commits: `feat:`, `fix:`, `test:`, `docs:`, `chore:`, `security:`.
- Ensure all builds and tests succeed before submitting a Pull Request:
  ```powershell
  dotnet build BackupApp.sln -c Release
  dotnet test BackupApp.sln -c Release
  ```
