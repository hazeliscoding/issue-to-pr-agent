using System.Text.RegularExpressions;
using IssueToPrAgent.Domain.Repository;

namespace IssueToPrAgent.Infrastructure.Repository;

/// <summary>
/// Shared file enumeration for the search-style tools. Skips the noise directories that would
/// otherwise drown a search (build output, dependencies, VCS internals) and binary files, so
/// the agent only ever sees source it can reason about. Deterministic and I/O-only — no rules
/// live here.
/// </summary>
internal static class WorkspaceFiles
{
    // Directories that never contain source worth searching.
    private static readonly HashSet<string> SkipDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "bin", "obj", "node_modules", "dist", ".vs", ".angular", "TestResults", ".idea",
    };

    /// <summary>Every readable, non-ignored, non-binary file under the root, as absolute paths.</summary>
    public static IEnumerable<string> Enumerate(RepositoryWorkspace workspace)
    {
        foreach (var path in EnumerateDirectory(workspace.Root))
        {
            yield return path;
        }
    }

    private static IEnumerable<string> EnumerateDirectory(string directory)
    {
        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break; // unreadable directory — skip rather than fail the whole search
        }

        foreach (var entry in entries)
        {
            if (Directory.Exists(entry))
            {
                if (SkipDirectories.Contains(Path.GetFileName(entry)))
                {
                    continue;
                }

                foreach (var nested in EnumerateDirectory(entry))
                {
                    yield return nested;
                }
            }
            else if (!LooksBinary(entry))
            {
                yield return entry;
            }
        }
    }

    /// <summary>Cheap binary sniff: a NUL byte in the first chunk means "not text".</summary>
    private static bool LooksBinary(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> buffer = stackalloc byte[4096];
            var read = stream.Read(buffer);
            return buffer[..read].IndexOf((byte)0) >= 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true; // can't read it → don't try to search it
        }
    }

    /// <summary>
    /// Translates a simple glob (<c>*</c>, <c>?</c>, <c>**</c>) into a regex over the
    /// forward-slashed repository-relative path. Null matches everything.
    /// </summary>
    public static Regex? GlobToRegex(string? glob)
    {
        if (string.IsNullOrWhiteSpace(glob))
        {
            return null;
        }

        var pattern = Regex.Escape(glob)
            .Replace(@"\*\*/", "(.*/)?")   // **/ spans any number of directories
            .Replace(@"\*\*", ".*")
            .Replace(@"\*", "[^/]*")        // * stays within a path segment
            .Replace(@"\?", "[^/]");

        return new Regex($"^{pattern}$", RegexOptions.IgnoreCase);
    }
}
