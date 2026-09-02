namespace IssueToPrAgent.Application.Analysis;

/// <summary>Configuration for issue analysis.</summary>
public sealed class IssueAnalysisOptions
{
    public const string SectionName = "Analysis";

    /// <summary>Issue analysis is a reasoning task, so a capable model is the default.</summary>
    public string Model { get; set; } = "claude-sonnet-5";

    /// <summary>Cap on items kept per list field (suspected areas, steps, unknowns).</summary>
    public int MaxItems { get; set; } = 12;

    /// <summary>How many search terms to extract from the issue for grounding.</summary>
    public int MaxTerms { get; set; } = 8;

    /// <summary>How many candidate files to include as grounding evidence.</summary>
    public int MaxEvidenceFiles { get; set; } = 8;

    /// <summary>How many matched lines to show per evidence file.</summary>
    public int MaxSnippetsPerFile { get; set; } = 3;
}
