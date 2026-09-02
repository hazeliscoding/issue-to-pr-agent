# ADR 0003 — Issue Analysis

## Status

Accepted — Phase 3.

## Context

Phase 3 introduces the first LLM: turning a GitHub issue into a structured plan —
`{problem, suspectedAreas, reproductionPlan, risk, unknowns}` — that later phases act on. Two
principles govern it: the model does the reasoning, but deterministic code owns the contract and
treats model output as untrusted; and the provider stays replaceable.

## Decision

**Typed, validated contract — never raw model JSON.** `IssueAnalysis` (Domain) is only ever
constructed by the validator, never deserialized directly from the model. The analyzer extracts
the first JSON object from the reply (models sometimes wrap it in prose), parses it, and checks
every field: a required `Problem` with a safe fallback, lists trimmed / de-duplicated / capped,
and the free-text `risk` mapped to a `RiskLevel` enum (`low`/`medium`/`high`, else `Unknown`).
Malformed output degrades to a safe analysis rather than throwing, so a bad reply can't crash the
pipeline or smuggle an unexpected shape downstream.

**Deterministic grounding before the model runs.** `IssueEvidenceGatherer` extracts *code-shaped*
terms from the issue (CamelCase, snake_case, dotted/file names — prose words are filtered out),
runs a few bounded `SearchCode` queries (the Phase 1 tool), and ranks the files that mention the
most terms. That evidence is handed to the model as facts, so `suspectedAreas` point at real code
instead of hallucinated paths. Grounding is deliberately shallow — a handful of searches — and
distinct from Phase 4's active locate/reproduce/test loop; it makes the analysis concrete without
becoming an exploration agent.

**Reused, tool-less model port.** `ILanguageModel` is the same lean text-completion port as the
sibling repos: a single completion, no tools, no way to act. The model can only produce text, so
it has no surface to read outside the repo or change anything — a guarantee of the interface's
shape, not a prompt. `AnthropicLanguageModel` implements it over the official SDK; the provider is
replaceable. The default model is `claude-sonnet-5` — issue analysis is a heavier reasoning task
than the sibling's light summarization (which used Haiku), and Sonnet balances quality against
Opus-tier cost; it's configurable via `IssueAnalysisOptions`.

**The issue is a typed input.** `IssueContext` (number, title, body, labels) is supplied by the
caller. Fetching it from GitHub (Octokit + token auth) is deferred to Phase 6, which already needs
the GitHub client for PR creation — so network and credentials land in one place, not here.

## Consequences

- Downstream phases can trust `IssueAnalysis`'s shape and its `RiskLevel` (usable for diff-review
  and PR gating later).
- The one deterministic component (term extraction + evidence ranking) is unit-tested without any
  model or network; the analyzer's validation is tested with scripted replies (valid, malformed,
  prose-wrapped, risk mapping, list cleaning/caps) and a recording model proving it only ever asks
  for text; a key-gated live test covers the real API end-to-end.
- 79 tests. Adds the `Anthropic` dependency.
- Deferred: GitHub issue fetching (Phase 6), and cross-checking suspected areas against files that
  actually exist (grounding already biases the model toward real paths).
