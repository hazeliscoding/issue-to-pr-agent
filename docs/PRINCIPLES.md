# Agentic Automation Portfolio — 2026 Project Plans

## General Engineering Principles

These projects should demonstrate production-grade agentic systems rather than simple chatbot applications.

Across all projects:

- Prefer deterministic application logic for business rules.
- Use LLMs only where reasoning, classification, summarization, interpretation, or planning adds value.
- Never allow a model to directly bypass authorization or domain rules.
- Treat tool output as untrusted input.
- Make side effects idempotent.
- Persist workflow state.
- Support retries and recovery.
- Instrument agent and tool execution with OpenTelemetry.
- Record enough information to replay failed workflows.
- Build evaluation scenarios alongside features.
- Keep model providers replaceable.
- Prefer typed contracts over free-form JSON.
- Make local development reproducible through Docker Compose and/or LocalStack.

Suggested baseline stack:

- .NET 10
- ASP.NET Core
- Microsoft Agent Framework
- PostgreSQL
- Entity Framework Core
- AWS SNS
- AWS SQS
- AWS EventBridge
- AWS Lambda or ECS
- OpenTelemetry
- Angular
- Docker Compose
- LocalStack
- xUnit
- Testcontainers
- GitHub Actions

Repository conventions:

```text
/src
  /Application
  /Domain
  /Infrastructure
  /WebApi
  /Worker
  /Mcp
  /Observability

/tests
  /UnitTests
  /IntegrationTests
  /AgentEvals

/ui
  /dashboard

/docs
  /architecture
  /adr
  /runbooks
  /threat-models
```

Do not implement every feature at once.

For every project:

1. Establish the deterministic domain model.
2. Implement infrastructure and APIs.
3. Add tools.
4. Introduce the agent.
5. Add durable execution.
6. Add observability.
7. Add evaluations.
8. Add security hardening.
9. Polish the demo and documentation.

# README Portfolio Requirements

Every repository should include:

## Problem

Explain the real engineering problem.

## Architecture

Include diagrams.

## Agent Responsibilities

Clearly state what the model controls.

## Deterministic Responsibilities

Clearly state what normal application code controls.

## Security Model

Explain:

- Authentication.
- Authorization.
- Tool permissions.
- Human approval.
- Credential isolation.

## Reliability

Explain:

- Retry behavior.
- Idempotency.
- Durable execution.
- Recovery.

## Evaluation

Show quantitative results.

Example:

```text
Scenario success:          94%
Unauthorized actions:       0%
Average tool calls:       4.2
Average workflow latency: 8.4s
Average token usage:     3,240
```

## Observability

Include screenshots showing:

- Distributed traces.
- Tool executions.
- Model calls.
- Workflow transitions.

## Failure Demo

Show at least one intentional failure and how the system recovers.

## Threat Model

Document:

- Prompt injection.
- Tool misuse.
- Privilege escalation.
- Secret leakage.
- Cross-tenant access.
- Replay attacks.
- Duplicate execution.

## Running Locally

A reviewer should be able to clone the repository and run:

```bash
docker compose up
```

and then follow a short demo guide.
