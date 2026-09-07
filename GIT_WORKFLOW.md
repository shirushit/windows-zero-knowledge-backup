# Git Workflow

## Branches
Protected `main`; short-lived branches: `feat/<issue>-name`, `fix/<issue>-name`, `security/<issue>-name`, `docs/<issue>-name`, `chore/<issue>-name`.

## Commits
Small, coherent, imperative messages, preferably Conventional Commits (`feat:`, `fix:`, `test:`, `docs:`, `refactor:`, `chore:`, `security:`). Never mix broad formatting/refactor with functional changes unnecessarily.

## PR requirements
PR links plan/issue, states what/why, tests, security impact, migration/data-format impact, screenshots for UI changes, rollback considerations. CI green; review required; security/crypto changes get explicit specialist/human review. Squash/rebase policy should be chosen once and recorded.

## Protected branch
No direct pushes, no force pushes, required status checks, stale approvals dismissed after material changes, signed commits/tags where practical.

### Enforced Protection Rules
- **Branch**: `main`
- **Required Status Checks**: `Build, Lint & Test` (from `.github/workflows/ci.yml`) must pass before merge.
- **Pull Request Required**: Direct pushes to `main` are strictly blocked.
- **Code Reviews**: Minimum 1 approving review required; stale reviews dismissed on new pushes.
- **Merge Strategy**: Squash & merge with Conventional Commit title or rebase merge (no merge commits in linear history).
- **Local Enforcement**: A pre-commit hook in `.githooks/pre-commit` (configured via `git config core.hooksPath .githooks`) blocks direct commits to `main` and scans staged changes for secret patterns before any commit.

## Agent behavior
Before branch creation sync base. Before commit inspect diff and secret scan. After commit update `PLAN.md`/`RESEARCH_LOG.md` in the same logical workstream. Never claim a PR is merged unless repository state proves it.

