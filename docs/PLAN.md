# PROJECT 2 — Issue-to-PR Engineering Agent

## Goal

Build a software engineering agent that takes a GitHub issue from investigation through a reviewed draft pull request.

The project should demonstrate controlled autonomous software modification rather than unrestricted code generation.

## Workflow

```text
GitHub Issue
     |
     v
Repository Analysis
     |
     v
Reproduction
     |
     v
Failing Test
     |
     v
Implementation Plan
     |
     v
Code Modification
     |
     v
Tests + Static Analysis
     |
     v
Diff Review
     |
     v
Draft Pull Request
```

## Capabilities

Agent should be able to:

- Read repository files.
- Search symbols.
- Inspect git history.
- Read issue metadata.
- Execute safe build commands.
- Execute tests.
- Modify files.
- Inspect diffs.
- Create commits.
- Create draft PRs.

Do not provide unrestricted shell access.

## Sandbox Design

Commands should be allowlisted.

Examples:

```text
dotnet build
dotnet test
dotnet format
npm test
pnpm test
git diff
git status
```

Block:

```text
curl
wget
ssh
arbitrary PowerShell
arbitrary bash
credential access
```

## Phase 1 — Repository Abstraction

Implement tools:

```text
ReadFile
SearchCode
FindSymbol
GetGitDiff
GetGitHistory
ListChangedFiles
```

**Implementation notes (delivered):**

- `RepositoryWorkspace` (Domain) owns the security boundary: `Resolve(relativePath)` returns a
  path proven to sit inside the root or throws `PathEscapeException`; absolute paths and `..`
  escapes are rejected, and the separator-boundary check stops `repo-evil`-style prefix tricks.
  Every reading tool routes through it. Case sensitivity matches the host file system. See
  ADR 0001.
- Four typed Application ports cover the six tools — `IFileReader`, `ICodeSearch`,
  `ISymbolFinder`, `IGitInspector` — each with bounded output (byte budgets, match caps, line
  ranges) that flags truncation. Infrastructure implements them over the filesystem and, for
  git, LibGit2Sharp in-process (no child git process — kept separate from the Phase 2 sandbox).
- Search and symbol lookup skip build output, dependencies, VCS internals, and binaries.
  `FindSymbol` is regex-based and multi-language (C#/TS/JS/Python declarations) — broad but
  build-free.
- Scaffolding established here: `.sln`, `Directory.Build.props` (net10, nullable,
  warnings-as-errors), nuget.org-only `nuget.config`, and the Domain/Application/Infrastructure
  + UnitTests/IntegrationTests layout.
- 31 tests: pure containment tests plus filesystem/git-backed tests over temp repositories. No
  LLM/agent/API yet (Phase 3+); `docker compose` arrives with the first hostable surface.

## Phase 2 — Execution Sandbox

Implement:

```text
RunBuild
RunTests
RunStaticAnalysis
```

Every execution should return:

```text
ExitCode
Stdout
Stderr
Duration
CommandId
```

Acceptance criteria:

- Unsupported commands cannot execute.
- Timeouts terminate execution.
- Output size is bounded.

**Implementation notes (delivered):**

- `CommandAllowlist` (Domain) is a default-deny policy: an executable is permitted only if listed
  and only with a listed sub-command (`dotnet` build/test/format/restore, `npm`/`pnpm` test,
  read-only `git` diff/status). Matched on the bare executable name, so a full path or `.exe`
  can't disguise a blocked tool. `SandboxTools` (the agent's single entry point) enforces it
  before the runner runs — so "unsupported commands cannot execute" holds by construction. See
  ADR 0002.
- No shell: commands are an `(executable, argument-list)` pair run with `UseShellExecute=false`,
  so `;`/`&&`/`|` are never interpreted — shell injection is structurally impossible.
- `ProcessSandboxCommandRunner` (Infrastructure) provides the mechanics, kept separate from the
  policy: a timeout that kills the whole process tree, bounded stdout/stderr buffers, and a
  result carrying `ExitCode`, `Stdout`, `Stderr`, `Duration`, `CommandId`, plus `TimedOut` /
  `OutputTruncated` flags.
- 64 tests: allowlist policy (allowed/blocked/disguised/unlisted), tool mapping + denial via a
  spy runner, and real-process mechanics (exit codes, timeout kill, output bounding).
- Deferred to security hardening: environment/secret scrubbing and per-argument validation; the
  high-level tools currently target the `dotnet` stack.

## Phase 3 — Issue Analysis

Produce structured output:

```json
{
  "problem": "",
  "suspectedAreas": [],
  "reproductionPlan": [],
  "risk": "",
  "unknowns": []
}
```

**Implementation notes (delivered):**

- `IssueAnalysis` (Domain) is a typed contract, only ever built by the analyzer's validator —
  never deserialized straight from the model. The analyzer extracts the JSON (even if prose-
  wrapped), then validates: required `Problem` with a fallback, lists trimmed/de-duped/capped,
  and free-text `risk` mapped to a `RiskLevel` enum (else `Unknown`). Malformed output degrades
  safely. See ADR 0003.
- Deterministic grounding: `IssueEvidenceGatherer` extracts code-shaped terms from the issue
  (CamelCase/snake_case/dotted — prose filtered), runs bounded `SearchCode` queries (Phase 1),
  and ranks the files mentioning the most terms. The model gets that evidence as facts so
  suspected areas point at real code — shallow by design, distinct from Phase 4's locate loop.
- The model port `ILanguageModel` is reused (single completion, no tools — no way to act);
  `AnthropicLanguageModel` implements it, provider-replaceable. Default model `claude-sonnet-5`
  (analysis is heavier than the sibling's Haiku summarization), configurable.
- The issue is a typed `IssueContext` input; GitHub fetching (Octokit) is deferred to Phase 6,
  which needs the GitHub client anyway. 79 tests; adds the `Anthropic` dependency.

## Phase 4 — Test-First Fix

The agent must attempt to reproduce the issue before changing implementation code.

Workflow:

```text
Read Issue
Locate Code
Create Reproduction
Run Test
Confirm Failure
Modify Code
Run Test
```

If reproduction cannot be achieved, agent should stop and explain why.

**Implementation notes (delivered):**

- `TestFirstFixWorkflow` (Application) is deterministic and owns the sequence and every gate; the
  model only proposes content — a reproduction test, then a fix — as typed `FileOperation`s, and
  never runs a command or decides pass/fail. See ADR 0004.
- Reproduction is confirmed by `dotnet build` (must succeed) then `dotnet test` (must fail), so a
  non-compiling test isn't mistaken for a reproduction. If it can't be established within a retry
  cap, the outcome is `CannotReproduce` and **no implementation code is touched**.
- Two structural guards enforce the invariant: reproduction-phase changes may only touch test
  files, and fix-phase changes may not modify the reproduction test. Violations are rejected
  before any write.
- Changes are anchored edits (exact find → replace) or file creates; `FileSystemFileWriter`
  validates the whole batch (containment + anchor present exactly once) before applying, so a bad
  op leaves no partial write. Every run returns a `FixOutcome` with the commands, applied changes,
  and reproduction test path — the evidence for Phase 5/6.
- 94 tests: planner JSON parsing, writer validation/atomicity/containment, and the full workflow
  gates via a scripted model + scripted sandbox runner over real file writes. A real `dotnet`
  end-to-end run is the manual Portfolio Demo (network-restore + slow); real process execution is
  already proven in Phase 2.

## Phase 5 — Diff Reviewer

Create a separate review step.

Review for:

- Unrelated changes.
- Missing tests.
- Security concerns.
- Changed public contracts.
- Excessive diff size.
- Suspicious generated code.
- Broken formatting.

## Phase 6 — PR Creation

Generate:

- PR title.
- Summary.
- Root cause.
- Fix.
- Testing evidence.
- Risks.

PR creation must require explicit approval.

## Phase 7 — CI Feedback Loop

When CI fails:

```text
Receive Failure
Inspect Logs
Classify Failure
Determine Whether Related
Apply Fix
Rerun Tests
```

Limit retries.

Example:

```text
Maximum automatic repair attempts: 3
```

## Evaluations

Build a benchmark from historical issues.

Score:

- Correct files selected.
- Reproduction success.
- Fix correctness.
- Regression rate.
- Number of tool calls.
- Unnecessary edits.
- CI pass rate.

## Portfolio Demo

Pick a real bug from one of your own repositories.

Show:

1. Issue ingestion.
2. Repository investigation.
3. Failure reproduction.
4. Test creation.
5. Patch.
6. Self-review.
7. Test evidence.
8. Draft PR.

