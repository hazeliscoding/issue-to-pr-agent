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

