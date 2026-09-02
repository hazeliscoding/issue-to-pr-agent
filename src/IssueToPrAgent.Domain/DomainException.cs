namespace IssueToPrAgent.Domain;

/// <summary>Base type for errors raised by the deterministic domain rules.</summary>
public abstract class DomainException(string message) : Exception(message);

/// <summary>A caller broke a domain rule (e.g. an invalid request or a missing file).</summary>
public sealed class DomainRuleException(string message) : DomainException(message);

/// <summary>
/// A requested path resolved outside the repository root. Its own type because it is a
/// security boundary — the agent must never read or touch anything outside the workspace, and
/// this is the deterministic guard that enforces it.
/// </summary>
public sealed class PathEscapeException(string message) : DomainException(message);
