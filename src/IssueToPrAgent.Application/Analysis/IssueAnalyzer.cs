using System.Text;
using System.Text.Json;
using IssueToPrAgent.Domain.Analysis;

namespace IssueToPrAgent.Application.Analysis;

/// <summary>
/// Analyzes an issue into a structured, validated <see cref="IssueAnalysis"/>. The model does the
/// reasoning, but its output is untrusted: the JSON is extracted (even if wrapped in prose),
/// parsed, and every field is validated — a required problem statement with a safe fallback,
/// cleaned and capped lists, and a risk string mapped to the <see cref="RiskLevel"/> enum
/// (unrecognized becomes <see cref="RiskLevel.Unknown"/>). Malformed output degrades to a safe
/// analysis rather than throwing. The issue is first grounded with deterministic repository
/// evidence so the suspected areas point at real code.
/// </summary>
public sealed class IssueAnalyzer(
    ILanguageModel model,
    IssueEvidenceGatherer evidenceGatherer,
    IssueAnalysisOptions options)
{
    private const string SystemPrompt =
        "You are a software engineering agent analyzing a bug or feature issue to plan a fix. " +
        "You are given the issue and, as grounding, repository files that mention terms from it. " +
        "Reply with JSON only, no prose, matching exactly: " +
        "{\"problem\": string, \"suspectedAreas\": [string], \"reproductionPlan\": [string], " +
        "\"risk\": \"low\"|\"medium\"|\"high\", \"unknowns\": [string]}. " +
        "problem: one or two sentences stating the core problem. " +
        "suspectedAreas: files or components likely involved — prefer the grounding files when relevant. " +
        "reproductionPlan: ordered steps to reproduce the issue. " +
        "risk: how risky a fix is likely to be. " +
        "unknowns: open questions that could block a fix. Use empty lists when nothing applies.";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public async Task<IssueAnalysis> AnalyzeAsync(IssueContext issue, CancellationToken cancellationToken)
    {
        var evidence = await evidenceGatherer.GatherAsync(issue, cancellationToken);
        var reply = await model.CompleteAsync(
            new ModelRequest(options.Model, SystemPrompt, BuildUserPrompt(issue, evidence)), cancellationToken);

        return Validate(TryParse(reply.Text ?? string.Empty));
    }

    private static string BuildUserPrompt(IssueContext issue, IssueEvidence evidence)
    {
        var builder = new StringBuilder();
        builder.Append("Issue #").Append(issue.Number).Append(": ").AppendLine(issue.Title);
        if (issue.Labels.Count > 0)
        {
            builder.Append("Labels: ").AppendLine(string.Join(", ", issue.Labels));
        }

        builder.AppendLine().AppendLine(issue.Body);

        if (evidence.Files.Count > 0)
        {
            builder.AppendLine().AppendLine("Repository grounding (files mentioning terms from the issue):");
            foreach (var file in evidence.Files)
            {
                builder.Append("- ").Append(file.Path).Append(" (").Append(file.Matches).AppendLine(" matches)");
                foreach (var snippet in file.Snippets)
                {
                    builder.Append("    ").AppendLine(snippet);
                }
            }
        }

        return builder.ToString();
    }

    private IssueAnalysis Validate(AnalysisDto? dto) => new(
        Problem: dto?.Problem?.Trim() is { Length: > 0 } problem ? problem : "Not determined from the issue.",
        SuspectedAreas: Clean(dto?.SuspectedAreas),
        ReproductionPlan: Clean(dto?.ReproductionPlan),
        Risk: ParseRisk(dto?.Risk),
        Unknowns: Clean(dto?.Unknowns));

    private IReadOnlyList<string> Clean(List<string>? items) =>
        (items ?? [])
        .Where(item => !string.IsNullOrWhiteSpace(item))
        .Select(item => item.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(options.MaxItems)
        .ToList();

    private static RiskLevel ParseRisk(string? risk) => risk?.Trim().ToLowerInvariant() switch
    {
        "low" => RiskLevel.Low,
        "medium" or "moderate" => RiskLevel.Medium,
        "high" => RiskLevel.High,
        _ => RiskLevel.Unknown,
    };

    /// <summary>Extracts the first JSON object from a reply (models sometimes wrap it in prose) and parses it.</summary>
    private static AnalysisDto? TryParse(string reply)
    {
        var start = reply.IndexOf('{');
        var end = reply.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AnalysisDto>(reply[start..(end + 1)], Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record AnalysisDto(
        string? Problem,
        List<string>? SuspectedAreas,
        List<string>? ReproductionPlan,
        string? Risk,
        List<string>? Unknowns);
}
