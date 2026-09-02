using IssueToPrAgent.Domain.Repository;
using IssueToPrAgent.Domain.Sandbox;

namespace IssueToPrAgent.Application.Sandbox;

/// <summary>
/// The guarded entry point for command execution — the <c>RunBuild</c>, <c>RunTests</c>, and
/// <c>RunStaticAnalysis</c> tools, plus a generic escape hatch. Every path here runs the command
/// through the <see cref="CommandAllowlist"/> before the runner ever sees it, so an unsupported
/// command can't execute regardless of how it was constructed. An optional project scope is
/// containment-checked against the workspace so it can't point the build outside the repo.
/// </summary>
public sealed class SandboxTools(
    ISandboxCommandRunner runner,
    CommandAllowlist allowlist,
    RepositoryWorkspace workspace)
{
    /// <summary>Runs an arbitrary command — after the allowlist approves it.</summary>
    public Task<CommandResult> RunAsync(
        SandboxCommand command, SandboxRunOptions? options, CancellationToken cancellationToken)
    {
        allowlist.EnsureAllowed(command); // throws CommandDeniedException before anything runs
        return runner.RunAsync(command, options ?? new SandboxRunOptions(), cancellationToken);
    }

    public Task<CommandResult> RunBuildAsync(string? project, CancellationToken cancellationToken) =>
        RunAsync(Dotnet("build", project), null, cancellationToken);

    public Task<CommandResult> RunTestsAsync(string? project, CancellationToken cancellationToken) =>
        RunAsync(Dotnet("test", project), null, cancellationToken);

    public Task<CommandResult> RunStaticAnalysisAsync(string? project, CancellationToken cancellationToken) =>
        RunAsync(Dotnet("format", project, "--verify-no-changes"), null, cancellationToken);

    private SandboxCommand Dotnet(string subcommand, string? project, params string[] trailing)
    {
        var arguments = new List<string> { subcommand };
        if (project is not null)
        {
            workspace.Resolve(project); // containment guard — throws if it escapes the root
            arguments.Add(project);
        }

        arguments.AddRange(trailing);
        return new SandboxCommand("dotnet", arguments);
    }
}
