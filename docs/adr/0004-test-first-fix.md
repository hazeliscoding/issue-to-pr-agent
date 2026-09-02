# ADR 0004 — Test-First Fix

## Status

Accepted — Phase 4.

## Context

Phase 4 is the first phase where the agent *writes* code and drives a loop: reproduce the bug,
then fix it. The spec is emphatic that reproduction comes first — "the agent must attempt to
reproduce the issue before changing implementation code" and "if reproduction cannot be achieved,
the agent should stop and explain why." That ordering is a business rule, and business rules
belong to deterministic code, not a prompt.

## Decision

**Deterministic orchestration; the model only proposes content.** `TestFirstFixWorkflow` owns the
sequence, the gates, file writes (through the contained writer), builds and tests (through the
sandbox), and every pass/fail decision. The model is asked for exactly two things — a reproduction
test, then a fix — as typed `FileOperation`s. It never runs a command, reads pass/fail, or decides
what happens next. This keeps the "controlled autonomy" boundary structural.

**Reproduction is confirmed by build-then-test, in that order.** A single `dotnet test` can't tell
a genuine reproduction from a test that doesn't compile — both exit non-zero. So the workflow runs
`dotnet build` (must succeed) *then* `dotnet test` (must fail). Only "builds and fails" counts as
reproduced. If that can't be reached within a retry cap, the outcome is `CannotReproduce` with the
reason, and **no implementation code is ever touched** — the central invariant, enforced by control
flow rather than hope.

**Two structural guards make the invariant unbreakable:**
- Reproduction-phase operations may only create or edit **test files** (path mentions "test"), so
  the model can't sneak an implementation change into the reproduce step.
- Fix-phase operations may **not** modify the reproduction test, so a fix can't "pass" by
  weakening the test that proves the bug.
A proposal that violates either guard is rejected before anything is written, and the loop retries
or stops.

**Anchored edits, validated before applying.** Changes are `Create` (full contents for new files)
or `Edit` (an exact find anchor → replacement). `FileSystemFileWriter` validates the whole batch
first — every path contained, every edit anchor present exactly once (missing or ambiguous is
refused, not guessed) — then applies it, so a bad operation never leaves a half-written change.
Anchored edits keep diffs small, which Phase 5's diff review rewards.

**Bounded and evidenced.** Retry caps (reproduction and fix) are deterministic options. Every run
returns a `FixOutcome` — status, explanation, the reproduction test path, every command with its
output, and the applied changes — which is the evidence Phase 5 (review) and Phase 6 (PR body)
build on.

## Consequences

- Implementation code cannot change without a failing test first — verified by tests, not
  convention.
- Model output is untrusted end to end: parsed and shape-checked by the planner, guarded and
  containment-checked by the workflow and writer.
- 94 tests: planner JSON parsing, file-writer validation/atomicity/containment, and the full
  workflow gates driven by a scripted model and a scripted sandbox runner over real file writes.
- A real end-to-end run (actual `dotnet` build/test on a live project) is the manual Portfolio
  Demo; it is not automated here because it needs network restore and is slow — the workflow logic
  is fully covered with fakes, and real process execution is already proven in Phase 2.
- Deferred: multiple edits to the same file in one batch aren't guaranteed independent; richer
  patch formats; and undo/rollback of applied changes (the working tree is the unit of work,
  reverted by git if needed).
