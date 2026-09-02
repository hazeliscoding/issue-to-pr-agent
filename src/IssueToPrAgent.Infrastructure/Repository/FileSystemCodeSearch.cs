using System.Text.RegularExpressions;
using IssueToPrAgent.Application.Repository;
using IssueToPrAgent.Domain;
using IssueToPrAgent.Domain.Repository;

namespace IssueToPrAgent.Infrastructure.Repository;

/// <summary>
/// Searches file contents line-by-line across the workspace (substring or regex), skipping
/// build output, dependencies, and binaries. Results are capped by the query's match budget and
/// flag when the cap was hit, so the agent never drowns in output.
/// </summary>
public sealed class FileSystemCodeSearch(RepositoryWorkspace workspace) : ICodeSearch
{
    public async Task<CodeSearchResult> SearchAsync(CodeSearchQuery query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(query.Pattern))
        {
            throw new DomainRuleException("A search requires a non-empty pattern.");
        }

        var matcher = BuildMatcher(query);
        var pathFilter = WorkspaceFiles.GlobToRegex(query.PathGlob);
        var matches = new List<SearchMatch>();

        foreach (var file in WorkspaceFiles.Enumerate(workspace))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relative = workspace.ToRelative(file);
            if (pathFilter is not null && !pathFilter.IsMatch(relative))
            {
                continue;
            }

            string[] lines;
            try
            {
                lines = await File.ReadAllLinesAsync(file, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            for (var i = 0; i < lines.Length; i++)
            {
                if (!matcher(lines[i]))
                {
                    continue;
                }

                if (matches.Count >= query.MaxMatches)
                {
                    return new CodeSearchResult(matches, Truncated: true);
                }

                matches.Add(new SearchMatch(relative, i + 1, lines[i]));
            }
        }

        return new CodeSearchResult(matches, Truncated: false);
    }

    private static Func<string, bool> BuildMatcher(CodeSearchQuery query)
    {
        if (query.IsRegex)
        {
            var options = query.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
            Regex regex;
            try
            {
                regex = new Regex(query.Pattern, options);
            }
            catch (ArgumentException ex)
            {
                throw new DomainRuleException($"Invalid search regex: {ex.Message}");
            }

            return line => regex.IsMatch(line);
        }

        var comparison = query.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return line => line.Contains(query.Pattern, comparison);
    }
}
