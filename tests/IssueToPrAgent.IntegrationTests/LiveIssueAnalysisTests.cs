using IssueToPrAgent.Application.Analysis;
using IssueToPrAgent.Domain.Analysis;
using IssueToPrAgent.Infrastructure.Analysis;
using IssueToPrAgent.Infrastructure.Repository;

namespace IssueToPrAgent.IntegrationTests;

/// <summary>
/// Exercises issue analysis against the real Anthropic API, grounded in a real repository on
/// disk. Gated — runs only when ANTHROPIC_API_KEY is set, so CI without a key stays green.
/// </summary>
public class LiveIssueAnalysisTests
{
    [Fact]
    public async Task Analyzes_an_issue_into_a_validated_structure()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")))
        {
            return; // No key: this live test is a no-op, mirroring the portfolio's gating.
        }

        using var ws = new TempWorkspace();
        ws.Write("src/OrderService.cs",
            "public class OrderService\n{\n    public decimal Total(Cart cart) => cart.Items.Sum(i => i.Price);\n}");
        ws.Write("src/Cart.cs", "public class Cart { public List<Item> Items { get; set; } }");

        var options = new IssueAnalysisOptions { Model = "claude-sonnet-5" };
        var analyzer = new IssueAnalyzer(
            new AnthropicLanguageModel(),
            new IssueEvidenceGatherer(new FileSystemCodeSearch(ws.Workspace), options),
            options);

        var issue = IssueContext.Create(101,
            "OrderService.Total throws NullReferenceException when Cart.Items is null",
            "Calling Total on an order whose Cart has a null Items list crashes instead of returning 0.");

        var analysis = await analyzer.AnalyzeAsync(issue, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(analysis.Problem));
        Assert.NotEqual(RiskLevel.Unknown, analysis.Risk); // the model should commit to a level
        Assert.NotEmpty(analysis.ReproductionPlan);
    }
}
