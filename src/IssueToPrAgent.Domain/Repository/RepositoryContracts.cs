namespace IssueToPrAgent.Domain.Repository;

/// <summary>The contents of a file (or a line-range slice of it), with bounding metadata so a
/// caller knows whether it saw everything.</summary>
/// <param name="Path">Repository-relative path, forward-slashed.</param>
/// <param name="Text">The returned text (possibly a slice or truncated).</param>
/// <param name="TotalLines">Total line count of the whole file.</param>
/// <param name="StartLine">1-based line the returned text starts at.</param>
/// <param name="LineCount">Number of lines returned.</param>
/// <param name="Truncated">True if the file was larger than the byte budget and was cut short.</param>
public sealed record FileContent(
    string Path,
    string Text,
    int TotalLines,
    int StartLine,
    int LineCount,
    bool Truncated);

/// <summary>What kind of declaration a symbol match is.</summary>
public enum SymbolKind
{
    Class,
    Interface,
    Record,
    Struct,
    Enum,
    Method,
    Function,
    Property,
    Type,
    Unknown,
}

/// <summary>A place a symbol is declared.</summary>
public sealed record SymbolLocation(string Path, int Line, SymbolKind Kind, string Name, string LineText);

/// <summary>A single line matching a code search.</summary>
public sealed record SearchMatch(string Path, int Line, string Text);

/// <summary>The result of a code search, with a flag for when the match budget was hit.</summary>
public sealed record CodeSearchResult(IReadOnlyList<SearchMatch> Matches, bool Truncated);

/// <summary>How a file changed between two points.</summary>
public enum ChangeKind
{
    Added,
    Modified,
    Deleted,
    Renamed,
}

/// <summary>A file that changed, with its prior path when renamed.</summary>
public sealed record ChangedFile(string Path, ChangeKind Kind, string? OldPath = null);

/// <summary>A unified diff, with bounding metadata.</summary>
public sealed record GitDiff(string Patch, int FilesChanged, bool Truncated);

/// <summary>One commit in the history.</summary>
public sealed record GitHistoryEntry(string Sha, string Author, DateTimeOffset When, string Message);
