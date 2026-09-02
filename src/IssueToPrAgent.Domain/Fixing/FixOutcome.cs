using IssueToPrAgent.Domain.Sandbox;

namespace IssueToPrAgent.Domain.Fixing;

/// <summary>How the test-first fix attempt ended.</summary>
public enum FixOutcomeStatus
{
    /// <summary>A failing reproduction test was written and now passes after the fix.</summary>
    Fixed,

    /// <summary>No failing reproduction could be established, so no implementation code was touched.</summary>
    CannotReproduce,

    /// <summary>The bug was reproduced, but the fix attempts did not make the test pass.</summary>
    FixFailed,
}

/// <summary>
/// The result of a test-first fix run, with the evidence a reviewer or PR needs: what happened
/// and why, the reproduction test that was written, every command that ran (with its output), and
/// the changes that were applied. Produced only by the deterministic workflow.
/// </summary>
public sealed record FixOutcome(
    FixOutcomeStatus Status,
    string Explanation,
    string? ReproductionTestPath,
    IReadOnlyList<CommandResult> Commands,
    IReadOnlyList<FileOperation> AppliedChanges);
