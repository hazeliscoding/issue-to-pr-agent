using System.Text.Json;
using IssueToPrAgent.Application.Analysis;
using IssueToPrAgent.Domain.Analysis;
using IssueToPrAgent.Domain.Fixing;

namespace IssueToPrAgent.Application.Fixing;

/// <summary>A set of proposed file changes from the model, with a short note explaining them.</summary>
public sealed record ChangeProposal(IReadOnlyList<FileOperation> Operations, string Notes);

/// <summary>
/// Asks the model to <em>propose</em> content — a reproduction test, then a fix — as typed file
/// operations. It only parses and shape-validates the JSON (well-formed create/edit ops); the
/// business rules (repro touches only tests, fix leaves the test alone, the test must actually
/// fail) are the deterministic workflow's job. The model never runs anything.
/// </summary>
public sealed class FixPlanner(ILanguageModel model, FixOptions options)
{
    private const string ReproductionSystem =
        "You write a FAILING test that reproduces a reported bug, before any fix is made. " +
        "Reply with JSON only: {\"notes\": string, \"operations\": [{\"kind\": \"create\"|\"edit\", " +
        "\"path\": string, \"contents\": string, \"find\": string, \"replace\": string}]}. " +
        "Only create or edit TEST files — never implementation code. The test must fail against the " +
        "current (buggy) code. For a new file use kind=create with contents; to change an existing " +
        "file use kind=edit with an exact find snippet and its replacement.";

    private const string FixSystem =
        "You are given a failing reproduction test and its output. Modify IMPLEMENTATION code so the " +
        "test passes. Reply with JSON only: {\"notes\": string, \"operations\": [{\"kind\": " +
        "\"create\"|\"edit\", \"path\": string, \"contents\": string, \"find\": string, \"replace\": " +
        "string}]}. Do NOT modify the reproduction test. Prefer minimal anchored edits (kind=edit " +
        "with an exact find snippet). Keep the change as small as possible.";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public Task<ChangeProposal> ProposeReproductionAsync(
        IssueContext issue, IssueAnalysis analysis, string codeContext, string? previousAttempt, CancellationToken cancellationToken)
    {
        var user = $"{FormatIssue(issue, analysis)}\n\n{codeContext}" +
            (previousAttempt is null ? string.Empty : $"\n\nPrevious attempt did not work: {previousAttempt}");
        return CompleteAsync(ReproductionSystem, user, cancellationToken);
    }

    public Task<ChangeProposal> ProposeFixAsync(
        IssueContext issue, IssueAnalysis analysis, string codeContext, string failingTestOutput, string? previousAttempt, CancellationToken cancellationToken)
    {
        var user = $"{FormatIssue(issue, analysis)}\n\n{codeContext}\n\nFailing test output:\n{failingTestOutput}" +
            (previousAttempt is null ? string.Empty : $"\n\nPrevious fix attempt did not work: {previousAttempt}");
        return CompleteAsync(FixSystem, user, cancellationToken);
    }

    private async Task<ChangeProposal> CompleteAsync(string system, string user, CancellationToken cancellationToken)
    {
        var reply = await model.CompleteAsync(new ModelRequest(options.Model, system, user), cancellationToken);
        var dto = TryParse(reply.Text ?? string.Empty);
        var operations = (dto?.Operations ?? [])
            .Select(ToOperation)
            .Where(op => op is not null)
            .Select(op => op!)
            .ToList();
        return new ChangeProposal(operations, dto?.Notes?.Trim() ?? string.Empty);
    }

    private static FileOperation? ToOperation(OperationDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Path) || string.IsNullOrWhiteSpace(dto.Kind))
        {
            return null;
        }

        return dto.Kind.Trim().ToLowerInvariant() switch
        {
            "create" when dto.Contents is not null => FileOperation.Create(dto.Path.Trim(), dto.Contents),
            "edit" when dto.Find is { Length: > 0 } && dto.Replace is not null =>
                FileOperation.Edit(dto.Path.Trim(), dto.Find, dto.Replace),
            _ => null,
        };
    }

    private static string FormatIssue(IssueContext issue, IssueAnalysis analysis) =>
        $"Issue #{issue.Number}: {issue.Title}\n{issue.Body}\n\n" +
        $"Problem: {analysis.Problem}\n" +
        $"Suspected areas: {string.Join(", ", analysis.SuspectedAreas)}";

    private static ProposalDto? TryParse(string reply)
    {
        var start = reply.IndexOf('{');
        var end = reply.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ProposalDto>(reply[start..(end + 1)], Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record ProposalDto(string? Notes, List<OperationDto>? Operations);

    private sealed record OperationDto(string? Kind, string? Path, string? Contents, string? Find, string? Replace);
}
