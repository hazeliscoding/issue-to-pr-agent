# Issue-to-PR Agent 🔧

A software engineering agent that takes a GitHub issue from investigation through a reviewed draft pull request — with controlled autonomy, not unrestricted code generation.

## Why

Coding agents are only trustworthy when their reach is bounded. This agent works test-first (reproduce the bug before touching implementation code), runs inside a command-allowlisted sandbox, self-reviews its diff, and requires explicit approval before creating a PR.

## Workflow

```text
Issue → Repo Analysis → Reproduction → Failing Test → Plan
      → Code Change → Tests + Static Analysis → Diff Review → Draft PR
```

If the issue can't be reproduced, the agent stops and explains why instead of guessing.

## Stack

.NET 10 · ASP.NET Core · Microsoft Agent Framework · PostgreSQL · OpenTelemetry · xUnit · GitHub API

## Status 🚧

Planning — see [docs/PLAN.md](docs/PLAN.md) for the phased build plan.

- [ ] Phase 1 — Repository abstraction tools
- [ ] Phase 2 — Execution sandbox (allowlisted commands)
- [ ] Phase 3 — Issue analysis
- [ ] Phase 4 — Test-first fix loop
- [ ] Phase 5 — Diff reviewer
- [ ] Phase 6 — PR creation (approval-gated)
- [ ] Phase 7 — CI feedback loop

## Running Locally

```bash
docker compose up
```

(Coming with Phase 1.)
