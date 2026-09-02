using IssueToPrAgent.Application.Analysis;
using IssueToPrAgent.Application.Repository;
using IssueToPrAgent.Domain.Analysis;
using IssueToPrAgent.Domain.Repository;

namespace IssueToPrAgent.UnitTests.Analysis;

public class IssueAnalyzerTests
{
    private static readonly IssueContext Issue =
        IssueContext.Create(42, "Something breaks", "Steps to reproduce are unclear.");

    private static IssueAnalyzer Analyzer(ILanguageModel model, IssueAnalysisOptions? options = null)
    {
        var resolved = options ?? new IssueAnalysisOptions { Model = "test-model" };
        return new IssueAnalyzer(model, new IssueEvidenceGatherer(new NoSearch(), resolved), resolved);
    }

    [Fact]
    public async Task Parses_a_valid_structured_analysis()
    {
        var model = new ScriptedModel("""
            {
              "problem": "Null reference when the cart is empty",
              "suspectedAreas": ["src/OrderService.cs", "src/Checkout.cs"],
              "reproductionPlan": ["Open an empty cart", "Click checkout"],
              "risk": "high",
              "unknowns": ["Is the cart ever null upstream?"]
            }
            """);

        var analysis = await Analyzer(model).AnalyzeAsync(Issue, CancellationToken.None);

        Assert.Equal("Null reference when the cart is empty", analysis.Problem);
        Assert.Equal(2, analysis.SuspectedAreas.Count);
        Assert.Equal(2, analysis.ReproductionPlan.Count);
        Assert.Equal(RiskLevel.High, analysis.Risk);
        Assert.Single(analysis.Unknowns);
    }

    [Fact]
    public async Task Degrades_safely_on_malformed_output()
    {
        var analysis = await Analyzer(new ScriptedModel("sorry, I can't help with that")).AnalyzeAsync(Issue, CancellationToken.None);

        Assert.Equal("Not determined from the issue.", analysis.Problem);
        Assert.Empty(analysis.SuspectedAreas);
        Assert.Empty(analysis.ReproductionPlan);
        Assert.Equal(RiskLevel.Unknown, analysis.Risk);
    }

    [Fact]
    public async Task Extracts_json_even_when_wrapped_in_prose()
    {
        var model = new ScriptedModel("""Sure! {"problem": "p", "risk": "low"} hope that helps""");

        var analysis = await Analyzer(model).AnalyzeAsync(Issue, CancellationToken.None);

        Assert.Equal("p", analysis.Problem);
        Assert.Equal(RiskLevel.Low, analysis.Risk);
    }

    [Theory]
    [InlineData("low", RiskLevel.Low)]
    [InlineData("Medium", RiskLevel.Medium)]
    [InlineData("moderate", RiskLevel.Medium)]
    [InlineData("HIGH", RiskLevel.High)]
    [InlineData("catastrophic", RiskLevel.Unknown)]
    [InlineData(null, RiskLevel.Unknown)]
    public async Task Maps_the_risk_string_to_the_enum(string? risk, RiskLevel expected)
    {
        var json = risk is null
            ? """{"problem": "p"}"""
            : $$"""{"problem": "p", "risk": "{{risk}}"}""";

        var analysis = await Analyzer(new ScriptedModel(json)).AnalyzeAsync(Issue, CancellationToken.None);

        Assert.Equal(expected, analysis.Risk);
    }

    [Fact]
    public async Task Cleans_and_caps_list_fields()
    {
        var model = new ScriptedModel("""
            {
              "problem": "p",
              "suspectedAreas": ["  a.cs  ", "a.cs", "", "   ", "b.cs", "c.cs"]
            }
            """);
        var options = new IssueAnalysisOptions { Model = "test-model", MaxItems = 2 };

        var analysis = await Analyzer(model, options).AnalyzeAsync(Issue, CancellationToken.None);

        Assert.Equal(["a.cs", "b.cs"], analysis.SuspectedAreas); // trimmed, de-duped, capped to 2
    }

    [Fact]
    public async Task The_analyzer_only_ever_asks_for_text()
    {
        // Proves the model's sole interaction is text completion — no tool, no way to act.
        var recording = new RecordingModel();

        await Analyzer(recording).AnalyzeAsync(Issue, CancellationToken.None);

        Assert.Equal(1, recording.Calls);
        Assert.Equal("test-model", recording.LastRequest!.Model);
    }

    private sealed class NoSearch : ICodeSearch
    {
        public Task<CodeSearchResult> SearchAsync(CodeSearchQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(new CodeSearchResult([], Truncated: false));
    }

    private sealed class ScriptedModel(string reply) : ILanguageModel
    {
        public Task<ModelReply> CompleteAsync(ModelRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new ModelReply(reply, 100, 40));
    }

    private sealed class RecordingModel : ILanguageModel
    {
        public int Calls { get; private set; }
        public ModelRequest? LastRequest { get; private set; }

        public Task<ModelReply> CompleteAsync(ModelRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            LastRequest = request;
            return Task.FromResult(new ModelReply("{\"problem\": \"p\"}", 1, 1));
        }
    }
}
