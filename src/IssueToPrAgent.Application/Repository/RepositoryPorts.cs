using IssueToPrAgent.Domain.Repository;

namespace IssueToPrAgent.Application.Repository;

/// <summary>How much of a file to read. Bounded by default so a tool call can't return an
/// unbounded blob; a line range narrows it further.</summary>
/// <param name="StartLine">1-based first line to return; null starts at the top.</param>
/// <param name="LineCount">Number of lines to return; null reads to the end (within the byte budget).</param>
/// <param name="MaxBytes">Hard ceiling on returned text size.</param>
public sealed record FileReadOptions(int? StartLine = null, int? LineCount = null, int MaxBytes = 256 * 1024);

/// <summary>Reads repository files — the <c>ReadFile</c> tool.</summary>
public interface IFileReader
{
    Task<FileContent> ReadAsync(string relativePath, FileReadOptions options, CancellationToken cancellationToken);
}

/// <summary>A code search request. Substring by default; opt into regex explicitly.</summary>
/// <param name="Pattern">The text or regular expression to find.</param>
/// <param name="IsRegex">Treat <paramref name="Pattern"/> as a regular expression.</param>
/// <param name="CaseSensitive">Match case exactly.</param>
/// <param name="MaxMatches">Ceiling on returned matches.</param>
/// <param name="PathGlob">Optional glob (e.g. <c>**/*.cs</c>) limiting which files are searched.</param>
public sealed record CodeSearchQuery(
    string Pattern,
    bool IsRegex = false,
    bool CaseSensitive = false,
    int MaxMatches = 200,
    string? PathGlob = null);

/// <summary>Searches file contents — the <c>SearchCode</c> tool.</summary>
public interface ICodeSearch
{
    Task<CodeSearchResult> SearchAsync(CodeSearchQuery query, CancellationToken cancellationToken);
}

/// <summary>A symbol lookup by name, optionally narrowed to one kind.</summary>
public sealed record SymbolQuery(string Name, SymbolKind? Kind = null, int MaxResults = 100);

/// <summary>Finds where symbols are declared — the <c>FindSymbol</c> tool.</summary>
public interface ISymbolFinder
{
    Task<IReadOnlyList<SymbolLocation>> FindAsync(SymbolQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// Which changes a diff should cover. With both refs null it compares the working tree against
/// HEAD (the agent's uncommitted edits); with refs set it compares those two commits.
/// </summary>
/// <param name="FromRef">Base ref/commit; null means HEAD.</param>
/// <param name="ToRef">Target ref/commit; null means the working tree.</param>
/// <param name="PathScope">Optional repository-relative path to limit the diff to.</param>
/// <param name="MaxBytes">Hard ceiling on the returned patch size.</param>
public sealed record DiffOptions(
    string? FromRef = null,
    string? ToRef = null,
    string? PathScope = null,
    int MaxBytes = 512 * 1024);

/// <summary>Read-only git inspection — the <c>GetGitDiff</c>, <c>GetGitHistory</c>, and
/// <c>ListChangedFiles</c> tools.</summary>
public interface IGitInspector
{
    Task<GitDiff> GetDiffAsync(DiffOptions options, CancellationToken cancellationToken);

    Task<IReadOnlyList<GitHistoryEntry>> GetHistoryAsync(
        string? pathScope, int maxEntries, CancellationToken cancellationToken);

    Task<IReadOnlyList<ChangedFile>> ListChangedFilesAsync(
        string? fromRef, string? toRef, CancellationToken cancellationToken);
}
