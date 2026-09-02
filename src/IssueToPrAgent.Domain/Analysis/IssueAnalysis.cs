namespace IssueToPrAgent.Domain.Analysis;

/// <summary>A GitHub issue as the analyzer sees it — the typed input, independent of how it was fetched.</summary>
public sealed record IssueContext(int Number, string Title, string Body, IReadOnlyList<string> Labels)
{
    public static IssueContext Create(int number, string title, string body, params string[] labels) =>
        new(number, title, body, labels);
}

/// <summary>How risky a fix for the issue is expected to be. Parsed from the model's answer,
/// defaulting to <see cref="Unknown"/> when it says something unrecognized.</summary>
public enum RiskLevel
{
    Unknown,
    Low,
    Medium,
    High,
}

/// <summary>
/// The structured, validated result of analyzing an issue. Produced by the model but only ever
/// constructed from output the deterministic layer has checked, so downstream phases can trust
/// its shape: a stated problem, hypothesized areas, a reproduction plan, a risk level, and the
/// open unknowns that might stop a fix.
/// </summary>
public sealed record IssueAnalysis(
    string Problem,
    IReadOnlyList<string> SuspectedAreas,
    IReadOnlyList<string> ReproductionPlan,
    RiskLevel Risk,
    IReadOnlyList<string> Unknowns);
