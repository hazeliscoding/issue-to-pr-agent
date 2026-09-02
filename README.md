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

## Repository abstraction

Phase 1 gives the agent its read-only senses over a repository — six deterministic tools
(`ReadFile`, `SearchCode`, `FindSymbol`, `GetGitDiff`, `GetGitHistory`, `ListChangedFiles`),
no LLM yet. The security-critical piece is **path containment**: every path routes through a
`RepositoryWorkspace` that resolves it *inside* the repo root or refuses — absolute paths and
`..` escapes are rejected, not clamped. Every tool returns **bounded** output (byte budgets,
match caps, line ranges) and flags truncation; search and symbol lookup skip build output,
dependencies, and binaries. Git is read in-process via LibGit2Sharp. See
[ADR 0001](docs/adr/0001-repository-abstraction.md).

## Execution sandbox

Phase 2 lets the agent run commands — but only vetted ones. A default-deny `CommandAllowlist`
permits just build/test/format tooling and read-only git; everything else (curl, ssh, a shell,
`git push`, `dotnet nuget push`) is refused, matched on the bare executable name so a full path
can't disguise a blocked tool. Commands run with **no shell** (`UseShellExecute=false`), so
`;`/`&&`/`|` are never interpreted — shell injection is structurally impossible. Every execution
is bounded: a timeout kills the whole process tree, output is capped, and the result carries
`ExitCode`, `Stdout`, `Stderr`, `Duration`, and a `CommandId`. See
[ADR 0002](docs/adr/0002-execution-sandbox.md).

## Issue analysis

Phase 3 introduces the first LLM: an issue becomes a **validated** structured analysis —
`{problem, suspectedAreas, reproductionPlan, risk, unknowns}`. The model reasons, but
deterministic code owns the contract: JSON is extracted (even from prose), every field is
validated, lists are cleaned and capped, and `risk` maps to a typed `RiskLevel`; malformed
output degrades safely. Before the model runs, the issue is **grounded** — code-shaped terms
are searched (Phase 1's `SearchCode`) and the most-relevant files are handed over as facts, so
suspected areas point at real code. The model port has no tools — it can only produce text.
Default model is `claude-sonnet-5` (configurable). See
[ADR 0003](docs/adr/0003-issue-analysis.md).

## Test-first fix

Phase 4 is where the agent writes code — under a deterministic loop that reproduces the bug
*before* touching implementation. The model only proposes content (a reproduction test, then a
fix) as typed file operations; the workflow owns the sequence and every decision. Reproduction is
confirmed by `build` (must succeed) then `test` (must fail), so a non-compiling test isn't
mistaken for a repro — and if the bug can't be reproduced, it **stops and explains** rather than
guessing. Two structural guards make "reproduce before you change code" unbreakable: the repro
step may only touch test files, and the fix step may not modify the reproduction test. Changes are
anchored edits validated before applying. See [ADR 0004](docs/adr/0004-test-first-fix.md).

## Status 🚧

In progress — see [docs/PLAN.md](docs/PLAN.md) for the phased build plan.

- [x] Phase 1 — Repository abstraction tools (path-contained, bounded, LibGit2Sharp)
- [x] Phase 2 — Execution sandbox (default-deny allowlist, no shell, timeout + bounded output)
- [x] Phase 3 — Issue analysis (grounded, typed + validated structured output)
- [x] Phase 4 — Test-first fix loop (reproduce-before-fix, deterministic gates)
- [ ] Phase 5 — Diff reviewer
- [ ] Phase 6 — PR creation (approval-gated)
- [ ] Phase 7 — CI feedback loop

## Running Locally

Phase 1 is a deterministic library layer — no service to host yet. Build and run the tests:

```bash
dotnet test
```

`docker compose up` arrives with the first hostable surface (an API/worker in a later phase).
