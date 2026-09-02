using IssueToPrAgent.Application.Analysis;
using IssueToPrAgent.Application.Fixing;
using IssueToPrAgent.Domain.Analysis;
using IssueToPrAgent.Domain.Fixing;

namespace IssueToPrAgent.UnitTests.Fixing;

public class FixPlannerTests
{
    private static readonly IssueContext Issue = IssueContext.Create(1, "bug", "it breaks");
    private static readonly IssueAnalysis Analysis = new("problem", ["src/A.cs"], [], RiskLevel.Low, []);

    private static FixPlanner Planner(string reply) =>
        new(new ScriptedModel(reply), new FixOptions { Model = "test-model" });

    [Fact]
    public async Task Parses_create_and_edit_operations()
    {
        var reply = """
            {
              "notes": "adds a failing test",
              "operations": [
                {"kind": "create", "path": "tests/T.cs", "contents": "// test"},
                {"kind": "edit", "path": "src/A.cs", "find": "a - b", "replace": "a + b"}
              ]
            }
            """;

        var proposal = await Planner(reply).ProposeReproductionAsync(Issue, Analysis, "context", null, CancellationToken.None);

        Assert.Equal("adds a failing test", proposal.Notes);
        Assert.Equal(2, proposal.Operations.Count);
        Assert.Equal(FileOperationKind.Create, proposal.Operations[0].Kind);
        Assert.Equal("tests/T.cs", proposal.Operations[0].Path);
        Assert.Equal(FileOperationKind.Edit, proposal.Operations[1].Kind);
        Assert.Equal("a - b", proposal.Operations[1].Find);
    }

    [Fact]
    public async Task Drops_operations_that_are_missing_required_fields()
    {
        var reply = """
            {
              "operations": [
                {"kind": "create", "path": "tests/T.cs"},
                {"kind": "edit", "path": "src/A.cs", "find": "", "replace": "x"},
                {"kind": "banana", "path": "src/A.cs"},
                {"kind": "edit", "path": "src/A.cs", "find": "old", "replace": "new"}
              ]
            }
            """;

        var proposal = await Planner(reply).ProposeFixAsync(Issue, Analysis, "ctx", "failing", null, CancellationToken.None);

        var op = Assert.Single(proposal.Operations); // only the well-formed edit survives
        Assert.Equal("old", op.Find);
    }

    [Fact]
    public async Task Extracts_json_wrapped_in_prose()
    {
        var reply = """Here you go: {"operations":[{"kind":"create","path":"tests/T.cs","contents":"x"}]} done!""";

        var proposal = await Planner(reply).ProposeReproductionAsync(Issue, Analysis, "ctx", null, CancellationToken.None);

        Assert.Single(proposal.Operations);
    }

    [Fact]
    public async Task Returns_no_operations_on_malformed_output()
    {
        var proposal = await Planner("sorry, no").ProposeReproductionAsync(Issue, Analysis, "ctx", null, CancellationToken.None);

        Assert.Empty(proposal.Operations);
    }

    private sealed class ScriptedModel(string reply) : ILanguageModel
    {
        public Task<ModelReply> CompleteAsync(ModelRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new ModelReply(reply, 10, 10));
    }
}
