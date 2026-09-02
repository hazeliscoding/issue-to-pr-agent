using System.Text.RegularExpressions;
using IssueToPrAgent.Application.Repository;
using IssueToPrAgent.Domain.Analysis;

namespace IssueToPrAgent.Application.Analysis;

/// <summary>
/// Turns an issue into grounding evidence, deterministically: it extracts code-like terms from
/// the issue text (identifiers, dotted names, file names — not prose), runs bounded
/// <see cref="ICodeSearch"/> queries for them, and ranks the files that mention the most terms.
/// No LLM — this is the deterministic retrieval that keeps the model's suspected areas honest.
/// </summary>
public sealed class IssueEvidenceGatherer(ICodeSearch search, IssueAnalysisOptions options)
{
    private static readonly Regex Quoted = new("[\"'`]([^\"'`\\n]{2,80})[\"'`]", RegexOptions.Compiled);
    private static readonly Regex Identifier = new(@"[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z0-9_]+)*", RegexOptions.Compiled);

    // Common words that survive the code-shape filter but aren't useful search terms.
    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "TODO", "FIXME", "NOTE", "http", "https",
    };

    public async Task<IssueEvidence> GatherAsync(IssueContext issue, CancellationToken cancellationToken)
    {
        var terms = ExtractTerms(issue, options.MaxTerms);
        if (terms.Count == 0)
        {
            return IssueEvidence.Empty;
        }

        var byFile = new Dictionary<string, Accumulator>(StringComparer.OrdinalIgnoreCase);
        foreach (var term in terms)
        {
            var result = await search.SearchAsync(new CodeSearchQuery(term, MaxMatches: 20), cancellationToken);
            foreach (var match in result.Matches)
            {
                if (!byFile.TryGetValue(match.Path, out var accumulator))
                {
                    accumulator = new Accumulator();
                    byFile[match.Path] = accumulator;
                }

                accumulator.Matches++;
                if (accumulator.Snippets.Count < options.MaxSnippetsPerFile)
                {
                    accumulator.Snippets.Add($"{match.Line}: {match.Text.Trim()}");
                }
            }
        }

        var files = byFile
            .OrderByDescending(entry => entry.Value.Matches)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Take(options.MaxEvidenceFiles)
            .Select(entry => new EvidenceHit(entry.Key, entry.Value.Matches, entry.Value.Snippets))
            .ToList();

        return new IssueEvidence(files);
    }

    /// <summary>Extracts distinct code-like terms from the issue, most-repeated first.</summary>
    public static IReadOnlyList<string> ExtractTerms(IssueContext issue, int max)
    {
        var text = $"{issue.Title}\n{issue.Body}";
        var candidates = new List<string>();

        // Identifiers inside quotes/backticks are especially likely to be code.
        foreach (Match quoted in Quoted.Matches(text))
        {
            foreach (Match token in Identifier.Matches(quoted.Groups[1].Value))
            {
                if (IsCodeish(token.Value))
                {
                    candidates.Add(token.Value);
                }
            }
        }

        foreach (Match token in Identifier.Matches(text))
        {
            if (IsCodeish(token.Value))
            {
                candidates.Add(token.Value);
            }
        }

        return candidates
            .GroupBy(term => term, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Key.Length)
            .Select(group => group.First())
            .Take(max)
            .ToList();
    }

    // Code-shaped means CamelCase, snake_case, or a dotted/file name — i.e. not a prose word.
    private static bool IsCodeish(string token)
    {
        if (token.Length < 3 || Stopwords.Contains(token))
        {
            return false;
        }

        var hasInnerUppercase = token.Skip(1).Any(char.IsUpper);
        var hasUnderscore = token.Contains('_');
        var isDotted = token.Contains('.');
        return hasInnerUppercase || hasUnderscore || isDotted;
    }

    private sealed class Accumulator
    {
        public int Matches { get; set; }
        public List<string> Snippets { get; } = [];
    }
}
