namespace IssueToPrAgent.Domain.Repository;

/// <summary>
/// A repository rooted at a single directory. Every path the agent asks to read is resolved
/// through <see cref="Resolve"/>, which guarantees the result stays inside the root — this is
/// the deterministic containment boundary the whole "controlled autonomy" model rests on. The
/// agent works in relative paths; absolute paths and <c>..</c> escapes are rejected, never
/// silently clamped. Path math only — this type does no I/O.
/// </summary>
public sealed class RepositoryWorkspace
{
    /// <summary>The absolute, normalized repository root. All resolved paths live under it.</summary>
    public string Root { get; }

    public RepositoryWorkspace(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new DomainRuleException("A workspace requires a root path.");
        }

        // Normalize once so every containment check compares like-for-like.
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
    }

    /// <summary>
    /// Resolves a repository-relative path to an absolute path guaranteed to live inside the
    /// root. Throws <see cref="PathEscapeException"/> if it would escape, or
    /// <see cref="DomainRuleException"/> if the input is absolute or empty.
    /// </summary>
    public string Resolve(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new DomainRuleException("A path is required.");
        }

        if (Path.IsPathRooted(relativePath))
        {
            throw new DomainRuleException($"Path '{relativePath}' must be relative to the repository root.");
        }

        var combined = Path.GetFullPath(Path.Combine(Root, relativePath));

        // Contained if it is the root itself or sits beneath it. The separator suffix stops a
        // sibling like "root-evil" from passing a naive prefix check on "root".
        var isContained = combined.Equals(Root, PathComparison)
            || combined.StartsWith(Root + Path.DirectorySeparatorChar, PathComparison);

        if (!isContained)
        {
            throw new PathEscapeException($"Path '{relativePath}' resolves outside the repository root.");
        }

        return combined;
    }

    /// <summary>Expresses an absolute path inside the root as a normalized repository-relative path.</summary>
    public string ToRelative(string absolutePath)
    {
        var full = Path.GetFullPath(absolutePath);
        var relative = Path.GetRelativePath(Root, full);
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    // Windows/macOS file systems are case-insensitive; Linux is not. Match the OS so containment
    // never rejects a legitimate path (or accepts a crafted-case escape) on the host it runs on.
    private static StringComparison PathComparison =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
}
