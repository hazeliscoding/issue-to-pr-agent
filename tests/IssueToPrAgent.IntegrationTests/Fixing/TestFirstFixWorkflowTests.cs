using IssueToPrAgent.Application.Analysis;
using IssueToPrAgent.Application.Fixing;
using IssueToPrAgent.Application.Sandbox;
using IssueToPrAgent.Domain.Analysis;
using IssueToPrAgent.Domain.Fixing;
using IssueToPrAgent.Domain.Sandbox;
using IssueToPrAgent.Infrastructure.Fixing;
using IssueToPrAgent.Infrastructure.Repository;

namespace IssueToPrAgent.IntegrationTests.Fixing;

/// <summary>
/// Drives the deterministic fix workflow with a scripted model and a scripted sandbox runner (so
/// build/test outcomes are controlled) over a real workspace with real file writes. Proves the
/// gates: reproduce-before-fix, the two structural guards, and stop-and-explain.
/// </summary>
public class TestFirstFixWorkflowTests
{
    private static readonly IssueContext Issue = IssueContext.Create(1, "Add is wrong", "Add returns a - b");

    private const string ReproCreate =
        """{"notes":"repro","operations":[{"kind":"create","path":"tests/CalcTests.cs","contents":"// failing test"}]}""";

    private static IssueAnalysis Analysis(params string[] areas) =>
        new("Add subtracts instead of adding", areas, ["Call Add(2,3)"], RiskLevel.Low, []);

    private static TestFirstFixWorkflow Workflow(TempWorkspace ws, ILanguageModel model, ISandboxCommandRunner runner, FixOptions options) =>
        new(
            new FileSystemFileReader(ws.Workspace),
            new FileSystemFileWriter(ws.Workspace),
            new SandboxTools(runner, new CommandAllowlist(), ws.Workspace),
            new FixPlanner(model, options),
            options);

    [Fact]
    public async Task Reproduces_then_fixes_and_reports_fixed()
    {
        using var ws = new TempWorkspace();
        ws.Write("src/Calc.cs", "public static class Calc { public static int Add(int a, int b) => a - b; }");

        var model = new QueuedModel(
            ReproCreate,
            """{"notes":"fix","operations":[{"kind":"edit","path":"src/Calc.cs","find":"a - b","replace":"a + b"}]}""");
        // repro: build ok, test fails; fix: build ok, test passes.
        var runner = new QueuedRunner(Ok(), Fail(), Ok(), Ok());

        var outcome = await Workflow(ws, model, runner, new FixOptions()).RunAsync(Issue, Analysis("src/Calc.cs"), CancellationToken.None);

        Assert.Equal(FixOutcomeStatus.Fixed, outcome.Status);
        Assert.Equal("tests/CalcTests.cs", outcome.ReproductionTestPath);
        Assert.Equal(2, outcome.AppliedChanges.Count);
        Assert.Equal(4, outcome.Commands.Count);
        Assert.Contains("a + b", File.ReadAllText(Path.Combine(ws.Root, "src", "Calc.cs")));
    }

    [Fact]
    public async Task Stops_with_cannot_reproduce_when_the_test_never_fails()
    {
        using var ws = new TempWorkspace();
        var model = new QueuedModel(ReproCreate, ReproCreate);
        // Both attempts: build ok, test passes → never reproduced.
        var runner = new QueuedRunner(Ok(), Ok(), Ok(), Ok());

        var outcome = await Workflow(ws, model, runner, new FixOptions { MaxReproductionAttempts = 2 })
            .RunAsync(Issue, Analysis(), CancellationToken.None);

        Assert.Equal(FixOutcomeStatus.CannotReproduce, outcome.Status);
        Assert.Null(outcome.ReproductionTestPath);
        Assert.Equal(2, model.Calls); // the fix step was never reached
    }

    [Fact]
    public async Task Rejects_a_reproduction_that_touches_implementation_code()
    {
        using var ws = new TempWorkspace();
        ws.Write("src/Calc.cs", "// impl");
        // The "reproduction" tries to edit implementation code — must be refused before running anything.
        var model = new QueuedModel(
            """{"operations":[{"kind":"edit","path":"src/Calc.cs","find":"// impl","replace":"// hacked"}]}""");
        var runner = new QueuedRunner();

        var outcome = await Workflow(ws, model, runner, new FixOptions { MaxReproductionAttempts = 1 })
            .RunAsync(Issue, Analysis("src/Calc.cs"), CancellationToken.None);

        Assert.Equal(FixOutcomeStatus.CannotReproduce, outcome.Status);
        Assert.Empty(outcome.Commands);       // never built or tested
        Assert.Empty(runner.Commands);
        Assert.Contains("test files", outcome.Explanation);
        Assert.Equal("// impl", File.ReadAllText(Path.Combine(ws.Root, "src", "Calc.cs"))); // untouched
    }

    [Fact]
    public async Task Rejects_a_fix_that_modifies_the_reproduction_test()
    {
        using var ws = new TempWorkspace();
        var model = new QueuedModel(
            ReproCreate,
            """{"operations":[{"kind":"edit","path":"tests/CalcTests.cs","find":"// failing test","replace":"// weakened"}]}""");
        // repro: build ok, test fails → reproduced; then the fix is rejected by the guard (no more runs).
        var runner = new QueuedRunner(Ok(), Fail());

        var outcome = await Workflow(ws, model, runner, new FixOptions { MaxFixAttempts = 1 })
            .RunAsync(Issue, Analysis(), CancellationToken.None);

        Assert.Equal(FixOutcomeStatus.FixFailed, outcome.Status);
        Assert.Contains("reproduction test", outcome.Explanation);
        Assert.Equal(2, runner.Commands.Count); // only the reproduction's build+test ran
    }

    [Fact]
    public async Task A_reproduction_that_escapes_the_workspace_is_refused()
    {
        using var ws = new TempWorkspace();
        // Path contains "test" so it passes the test-file guard, but containment must still block it.
        var model = new QueuedModel(
            """{"operations":[{"kind":"create","path":"../evil/Test.cs","contents":"x"}]}""");
        var runner = new QueuedRunner();

        var outcome = await Workflow(ws, model, runner, new FixOptions { MaxReproductionAttempts = 1 })
            .RunAsync(Issue, Analysis(), CancellationToken.None);

        Assert.Equal(FixOutcomeStatus.CannotReproduce, outcome.Status);
        Assert.Empty(runner.Commands);
        Assert.False(Directory.Exists(Path.Combine(ws.Root, "..", "evil")));
    }

    private static CommandResult Ok() => new(Guid.NewGuid(), 0, string.Empty, string.Empty, TimeSpan.Zero, false, false);

    private static CommandResult Fail() => new(Guid.NewGuid(), 1, "test failed", string.Empty, TimeSpan.Zero, false, false);

    private sealed class QueuedModel(params string[] replies) : ILanguageModel
    {
        private readonly Queue<string> _replies = new(replies);
        public int Calls { get; private set; }

        public Task<ModelReply> CompleteAsync(ModelRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            var text = _replies.Count > 0 ? _replies.Dequeue() : "{}";
            return Task.FromResult(new ModelReply(text, 1, 1));
        }
    }

    private sealed class QueuedRunner(params CommandResult[] results) : ISandboxCommandRunner
    {
        private readonly Queue<CommandResult> _results = new(results);
        public List<SandboxCommand> Commands { get; } = [];

        public Task<CommandResult> RunAsync(SandboxCommand command, SandboxRunOptions options, CancellationToken cancellationToken)
        {
            Commands.Add(command);
            return Task.FromResult(_results.Dequeue());
        }
    }
}
