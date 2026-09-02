using IssueToPrAgent.Application.Sandbox;
using IssueToPrAgent.Domain;
using IssueToPrAgent.Domain.Repository;
using IssueToPrAgent.Domain.Sandbox;

namespace IssueToPrAgent.UnitTests.Sandbox;

public class SandboxToolsTests
{
    private static readonly string Root = OperatingSystem.IsWindows() ? @"C:\repo" : "/repo";

    private sealed class SpyRunner : ISandboxCommandRunner
    {
        public SandboxCommand? Last { get; private set; }
        public bool WasCalled { get; private set; }

        public Task<CommandResult> RunAsync(SandboxCommand command, SandboxRunOptions options, CancellationToken cancellationToken)
        {
            WasCalled = true;
            Last = command;
            return Task.FromResult(new CommandResult(Guid.NewGuid(), 0, string.Empty, string.Empty, TimeSpan.Zero, false, false));
        }
    }

    private static (SandboxTools Tools, SpyRunner Runner) Build()
    {
        var runner = new SpyRunner();
        var tools = new SandboxTools(runner, new CommandAllowlist(), new RepositoryWorkspace(Root));
        return (tools, runner);
    }

    [Fact]
    public async Task RunBuild_issues_an_allowlisted_dotnet_build()
    {
        var (tools, runner) = Build();

        await tools.RunBuildAsync(project: null, CancellationToken.None);

        Assert.Equal("dotnet", runner.Last!.Executable);
        Assert.Equal(["build"], runner.Last.Arguments);
    }

    [Fact]
    public async Task RunTests_scopes_to_a_project_path()
    {
        var (tools, runner) = Build();

        await tools.RunTestsAsync("tests/App.Tests", CancellationToken.None);

        Assert.Equal(["test", "tests/App.Tests"], runner.Last!.Arguments);
    }

    [Fact]
    public async Task RunStaticAnalysis_verifies_formatting_without_writing()
    {
        var (tools, runner) = Build();

        await tools.RunStaticAnalysisAsync(project: null, CancellationToken.None);

        Assert.Equal(["format", "--verify-no-changes"], runner.Last!.Arguments);
    }

    [Fact]
    public async Task An_unsupported_command_is_denied_and_never_reaches_the_runner()
    {
        var (tools, runner) = Build();

        await Assert.ThrowsAsync<CommandDeniedException>(() =>
            tools.RunAsync(SandboxCommand.Create("curl", "https://evil.example"), null, CancellationToken.None));

        Assert.False(runner.WasCalled);
    }

    [Fact]
    public async Task A_project_scope_that_escapes_the_repo_is_rejected()
    {
        var (tools, runner) = Build();

        await Assert.ThrowsAsync<PathEscapeException>(() =>
            tools.RunBuildAsync("../../elsewhere", CancellationToken.None));

        Assert.False(runner.WasCalled);
    }
}
