# ADR 0001 — Repository Abstraction

## Status

Accepted — Phase 1.

## Context

The agent's first need is to *read* a repository — files, code, symbols, and git state — so it
can investigate an issue. These reads are the foundation everything else builds on, and they
are also the first place "controlled autonomy" is won or lost: a reading tool that can wander
outside the repo, or return an unbounded blob, is a liability before the agent has written a
single line. Phase 1 is deterministic infrastructure only — no LLM, no agent, no mutation.

## Decision

**Path containment is a domain rule, enforced once.** `RepositoryWorkspace` owns a single
`Resolve(relativePath)` that returns an absolute path *proven* to sit inside the root, or
throws `PathEscapeException`. Absolute paths and `..` escapes are rejected outright, never
silently clamped, and a sibling like `repo-evil` can't slip past a naive prefix check (the
comparison requires the separator boundary). Every reading tool routes through it, so
containment can't be forgotten in one tool and remembered in another. Comparison is
case-insensitive on Windows/macOS and ordinal on Linux, matching each file system so it neither
wrongly rejects a valid path nor accepts a crafted-case escape.

**Tools are typed ports with bounded output.** Four Application ports — `IFileReader`,
`ICodeSearch`, `ISymbolFinder`, `IGitInspector` — cover the six tools, each with typed
inputs/results and a hard output ceiling (byte budgets, match caps, line ranges) that flags
when it truncated. The agent (later phases) wraps these; Phase 1 exercises them directly.

**Git is read in-process via LibGit2Sharp.** Diff, history, and changed-files use managed git
bindings rather than shelling out. This keeps Phase 1 hermetic and entirely separate from the
Phase 2 execution sandbox (which is where allowlisted `git`/`dotnet` *commands* run), and
avoids a process-spawn surface in the reading layer. The trade-off: diff text is libgit2's
patch format (very close to `git diff`, not byte-identical), and the working-tree *diff* covers
tracked changes — brand-new untracked files surface through `ListChangedFiles` (which includes
untracked) rather than the patch.

**Symbol search is regex-based and multi-language.** `FindSymbol` matches declaration syntax
(C#, TypeScript/JavaScript, Python) with language-aware regexes over normally-formatted code —
no compilation. It is deliberately broad-but-approximate: accurate enough to point the agent at
the right file and line (which is all it needs), across languages, with zero build dependency.
A Roslyn-based finder was considered and rejected as more than Phase 1 needs.

## Consequences

- The security boundary is small, central, and unit-tested independent of any I/O — escape
  attempts are provably rejected.
- Search and symbol lookup skip build output, dependencies, VCS internals, and binaries, so the
  agent sees only source it can reason about.
- One third-party dependency (LibGit2Sharp); Roslyn avoided.
- 31 tests: pure containment tests, plus filesystem/git-backed tests over throwaway temp
  repositories (the "external system" in Phase 1 is the filesystem and git — no Postgres yet).
- Deferred: honoring `.gitignore` in search (currently a fixed skip-list), untracked-file diffs
  in the working-tree patch, and richer symbol kinds. `docker compose` arrives with the first
  hostable surface (an API/worker in a later phase) — there is nothing to run yet.
