namespace IssueToPrAgent.Domain.Sandbox;

/// <summary>
/// The outcome of running a sandboxed command. <see cref="CommandId"/> correlates the run in
/// logs and traces; <see cref="TimedOut"/> and <see cref="OutputTruncated"/> tell a caller the
/// result is partial (the process was killed, or the output hit its budget) rather than clean.
/// </summary>
public sealed record CommandResult(
    Guid CommandId,
    int ExitCode,
    string Stdout,
    string Stderr,
    TimeSpan Duration,
    bool TimedOut,
    bool OutputTruncated);
