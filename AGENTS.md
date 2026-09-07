# LLM / AGENT EXECUTION CONTRACT

This repository may be developed by different LLMs and humans across independent sessions. Conversation memory is never a source of truth. The repository is.

## Mandatory startup
Before changing code, read: `AGENTS.md`, `PRODUCT.md`, `ARCHITECTURE.md`, `SECURITY.md`, `CRYPTOGRAPHY.md`, `PLAN.md`, `DECISIONS.md`, and the latest entries of `RESEARCH_LOG.md`. Then inspect `git status`, current branch, recent commits, open work, and CI state.

## Execution loop
For each executable task: **read plan → inspect code → implement smallest coherent change → test → security check → build → document → commit → update plan → continue**. Do not stop merely because one task passed if another unblocked task exists.

## Plan states
- `[ ]` not started
- `[~]` in progress
- `[x]` complete and verified
- `[!]` blocked by technical/external issue
- `[?]` human decision required

A task becomes `[x]` only when its acceptance criteria and required tests pass. Never rewrite the plan to disguise implementation drift.

## Human-only hard decisions
Only the human developer may approve a **hard decision**: a decision that changes the product/technical direction substantially from the approved plan. Examples: replacing the primary stack; abandoning the planned MVP Telegram backend; changing the cryptographic trust model; adding a mandatory central server; changing zero-knowledge guarantees; removing a major capability; fundamentally replacing the backup/restore architecture.

If a hard decision appears necessary: stop only dependent work; mark `[?]`; add a `DECISIONS.md` entry containing current design, discovered problem, alternatives, pros/cons, security and migration impact, and recommendation; continue unrelated work; wait for explicit human approval.

Normal implementation decisions that preserve architecture may be made by the agent and logged.

## Mandatory checkpoint logging
After every meaningful checkpoint append to `RESEARCH_LOG.md` (never rewrite history):
```
### YYYY-MM-DD HH:MM TZ — checkpoint title
Agent/model: ...
Branch: ...
Commit: ...
Plan item: ...
Completed: ...
Changed: ...
Tests/build: ...
Security review: ...
Problems: ...
Decisions: ...
Next: ...
```
Use real timestamps.

## Security supremacy
Security requirements are non-optional. Never commit secrets; log passwords, keys, tokens, plaintext filenames/content where avoidable; upload plaintext backup payloads; disable TLS verification; invent cryptography; silently ignore integrity failures; weaken security to make tests pass. `SECURITY.md` and `CRYPTOGRAPHY.md` override convenience.

## Git rules
Never develop directly on a protected production branch. Use issue/task → branch → implementation → tests → commit → push → PR → CI → review → merge. Keep commits reviewable and scoped. Never force-push protected branches. Never merge red CI. Security-sensitive changes require explicit review.

## Scope discipline
Do not opportunistically redesign unrelated modules. Avoid speculative features. Do not silently add telemetry, analytics, cloud dependencies, accounts, ads, or servers. New dependencies require justification and security/license review.

## Definition of done
The product is not production-ready until all mandatory `PLAN.md` gates pass, disaster recovery is proven on a clean environment, documentation matches implementation, and no unresolved critical security findings remain.
