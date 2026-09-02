using IssueToPrAgent.Domain.Sandbox;

namespace IssueToPrAgent.Application.Sandbox;

/// <summary>Bounds a single execution: how long it may run and how much output it may return.</summary>
/// <param name="Timeout">Wall-clock ceiling; null uses <see cref="EffectiveTimeout"/>'s default.</param>
/// <param name="MaxOutputChars">Ceiling on captured stdout and stderr each.</param>
public sealed record SandboxRunOptions(TimeSpan? Timeout = null, int MaxOutputChars = 200_000)
{
    public TimeSpan EffectiveTimeout => Timeout ?? TimeSpan.FromMinutes(5);
}

/// <summary>
/// Low-level executor: runs a (already-vetted) command with a timeout and bounded output. It
/// does not consult the allowlist — that gate lives in <see cref="SandboxTools"/>, the single
/// entry point the agent uses — so this stays a pure process-mechanics port, testable on its own.
/// </summary>
public interface ISandboxCommandRunner
{
    Task<CommandResult> RunAsync(SandboxCommand command, SandboxRunOptions options, CancellationToken cancellationToken);
}
