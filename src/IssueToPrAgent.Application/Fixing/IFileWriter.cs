using IssueToPrAgent.Domain.Fixing;

namespace IssueToPrAgent.Application.Fixing;

/// <summary>
/// Applies proposed file changes to the working tree — the agent's only write capability, and
/// like every path it routes through the workspace containment guard. The batch is validated
/// before anything is written (every path contained, every edit anchor present and unambiguous),
/// so a bad operation fails the whole batch instead of leaving a half-applied change.
/// </summary>
public interface IFileWriter
{
    Task ApplyAsync(IReadOnlyList<FileOperation> operations, CancellationToken cancellationToken);
}
