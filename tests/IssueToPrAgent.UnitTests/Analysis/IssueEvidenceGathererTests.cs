using IssueToPrAgent.Application.Analysis;
using IssueToPrAgent.Application.Repository;
using IssueToPrAgent.Domain.Analysis;
using IssueToPrAgent.Domain.Repository;

namespace IssueToPrAgent.UnitTests.Analysis;

public class IssueEvidenceGathererTests
{
    private sealed class StubSearch(Dictionary<string, List<SearchMatch>> byTerm) : ICodeSearch
    {
        public List<string> Queried { get; } = [];

        public Task<CodeSearchResult> SearchAsync(CodeSearchQuery query, CancellationToken cancellationToken)
        {
            Queried.Add(query.Pattern);
            var matches = byTerm.TryGetValue(query.Pattern, out var found) ? found : [];
            return Task.FromResult(new CodeSearchResult(matches, Truncated: false));
        }
    }

    [Fact]
    public void ExtractTerms_keeps_code_shaped_tokens_and_drops_prose()
    {
        var issue = IssueContext.Create(1,
            "OrderService throws NullReferenceException on checkout",
            "The bug happens in `Checkout.Process` when the cart is empty.");

        var terms = IssueEvidenceGatherer.ExtractTerms(issue, max: 10);

        Assert.Contains("OrderService", terms);
        Assert.Contains("NullReferenceException", terms);
        Assert.Contains("Checkout.Process", terms); // dotted → code-shaped
        Assert.DoesNotContain("throws", terms);
        Assert.DoesNotContain("checkout", terms); // plain prose word
    }

    [Fact]
    public async Task GatherAsync_ranks_files_by_how_many_terms_they_mention()
    {
        var byTerm = new Dictionary<string, List<SearchMatch>>
        {
            ["OrderService"] =
            [
                new SearchMatch("src/OrderService.cs", 10, "class OrderService"),
                new SearchMatch("src/Checkout.cs", 5, "new OrderService()"),
            ],
            ["NullReferenceException"] =
            [
                new SearchMatch("src/OrderService.cs", 20, "throw new NullReferenceException()"),
            ],
        };
        var search = new StubSearch(byTerm);
        var gatherer = new IssueEvidenceGatherer(search, new IssueAnalysisOptions());

        var issue = IssueContext.Create(1, "OrderService throws NullReferenceException", "See above.");
        var evidence = await gatherer.GatherAsync(issue, CancellationToken.None);

        Assert.Equal("src/OrderService.cs", evidence.Files[0].Path); // mentioned by both terms
        Assert.Equal(2, evidence.Files[0].Matches);
        Assert.Contains("src/Checkout.cs", evidence.Files.Select(f => f.Path));
        Assert.NotEmpty(evidence.Files[0].Snippets);
    }

    [Fact]
    public async Task GatherAsync_returns_empty_when_the_issue_has_no_code_terms()
    {
        var search = new StubSearch([]);
        var gatherer = new IssueEvidenceGatherer(search, new IssueAnalysisOptions());

        var issue = IssueContext.Create(1, "the app is slow", "it feels sluggish when I use it");
        var evidence = await gatherer.GatherAsync(issue, CancellationToken.None);

        Assert.Empty(evidence.Files);
        Assert.Empty(search.Queried); // no terms → no searches run
    }
}
