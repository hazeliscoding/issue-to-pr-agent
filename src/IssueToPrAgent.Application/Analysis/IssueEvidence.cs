namespace IssueToPrAgent.Application.Analysis;

/// <summary>A repository file that mentions terms from the issue, with a few matched lines.</summary>
public sealed record EvidenceHit(string Path, int Matches, IReadOnlyList<string> Snippets);

/// <summary>
/// Deterministic grounding for the analysis: the repository files most associated with the
/// issue's terms, ranked by how many of those terms they mention. Handed to the model as facts
/// so its suspected areas point at real code instead of guesses.
/// </summary>
public sealed record IssueEvidence(IReadOnlyList<EvidenceHit> Files)
{
    public static IssueEvidence Empty { get; } = new([]);
}
