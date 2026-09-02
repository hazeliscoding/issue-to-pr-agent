# ADR 0002 — Execution Sandbox

## Status

Accepted — Phase 2.

## Context

To reproduce a bug and verify a fix, the agent must *run* things — build, test, format-check.
That is the most dangerous capability it has: unrestricted command execution is arbitrary code
execution. Phase 2's job is to give the agent exactly the commands it needs and nothing else,
with executions that can't hang or flood memory. The acceptance criteria are explicit:
unsupported commands cannot execute, timeouts terminate execution, and output is bounded.

## Decision

**Default-deny allowlist, enforced at one choke point.** `CommandAllowlist` (Domain) permits an
executable only if it is listed, and then only with a listed sub-command — `dotnet`
build/test/format/restore, `npm`/`pnpm` test, read-only `git` diff/status. Everything else is
refused: `curl`, `wget`, `ssh`, any shell, and unlisted sub-commands like `dotnet nuget push` or
`git push`. The executable is matched on its bare name, so `C:\tools\curl.exe` or `/usr/bin/git`
can't disguise or smuggle a tool. `SandboxTools` is the single entry point the agent uses and it
calls `EnsureAllowed` before the runner ever sees a command, so "unsupported commands cannot
execute" holds regardless of how the command was built.

**No shell, ever.** Commands are a `(executable, argument-list)` pair run with
`UseShellExecute = false`. The OS receives the arguments verbatim — nothing parses `;`, `&&`, or
`|` — so shell injection and command chaining are structurally impossible, not merely filtered.
This is also *why* blocking shells in the allowlist is sufficient: there is no shell in the loop
to smuggle one through.

**Policy and mechanics are separated.** `CommandAllowlist` is pure policy (Domain, exhaustively
unit-tested). `ISandboxCommandRunner` / `ProcessSandboxCommandRunner` is pure mechanics: it runs
an already-vetted command with a timeout that kills the whole process tree and bounded output
buffers, and reports `TimedOut` / `OutputTruncated` so a caller knows the result is partial. The
split lets each be tested in isolation — the allowlist without spawning anything, the runner
against real processes without depending on the policy.

**Output is bounded by construction.** Stdout and stderr each accumulate into a
`BoundedTextBuffer` capped at a character budget; past the cap it stops appending and sets a
flag. Leading output (where the first error almost always is) is preserved. Every run also
carries a `CommandId` and `Duration` for correlation and replay.

## Consequences

- The two security boundaries are now small, central, and independently tested: path containment
  (ADR 0001) and command allowlisting here.
- Adding a capability is a one-line allowlist edit; the default stance stays deny.
- The three tools (`RunBuild`, `RunTests`, `RunStaticAnalysis`) map to `dotnet`
  build/test/format on an optional, containment-checked project scope; static analysis is
  `dotnet format --verify-no-changes` (a check, not a rewrite).
- 64 tests: allowlist policy (allowed pass, blocked/disguised/unlisted denied), tool mapping and
  denial via a spy runner, and real-process mechanics (exit codes, timeout kill, output
  bounding).
- Deferred to the security-hardening phase: environment/secret scrubbing for child processes and
  per-argument validation beyond the sub-command. Non-`dotnet` stacks (npm/pnpm) are allowlisted
  but the high-level tools currently target `dotnet`.
