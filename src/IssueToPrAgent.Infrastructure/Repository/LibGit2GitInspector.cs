using System.Text;
using IssueToPrAgent.Application.Repository;
using IssueToPrAgent.Domain;
using IssueToPrAgent.Domain.Repository;
using Git = LibGit2Sharp;

namespace IssueToPrAgent.Infrastructure.Repository;

/// <summary>
/// Read-only git inspection over the workspace via LibGit2Sharp — in-process, no child git
/// process, which keeps Phase 1 entirely separate from the Phase 2 execution sandbox. Covers the
/// diff, history, and changed-files tools; it never mutates the repository.
/// </summary>
public sealed class LibGit2GitInspector(RepositoryWorkspace workspace) : IGitInspector
{
    public Task<GitDiff> GetDiffAsync(DiffOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var repo = Open();

        var paths = ResolveScope(options.PathScope);

        Git.Patch patch;
        if (options.FromRef is null && options.ToRef is null)
        {
            // The agent's uncommitted edits: HEAD tree vs index + working directory.
            patch = repo.Diff.Compare<Git.Patch>(
                repo.Head.Tip?.Tree, Git.DiffTargets.Index | Git.DiffTargets.WorkingDirectory, paths);
        }
        else if (options.ToRef is null)
        {
            patch = repo.Diff.Compare<Git.Patch>(
                ResolveCommit(repo, options.FromRef!).Tree, Git.DiffTargets.Index | Git.DiffTargets.WorkingDirectory, paths);
        }
        else
        {
            patch = repo.Diff.Compare<Git.Patch>(
                ResolveCommit(repo, options.FromRef ?? "HEAD").Tree, ResolveCommit(repo, options.ToRef).Tree, paths);
        }

        var (content, truncated) = Bound(patch.Content, options.MaxBytes);
        return Task.FromResult(new GitDiff(content, patch.Count(), truncated));
    }

    public Task<IReadOnlyList<GitHistoryEntry>> GetHistoryAsync(
        string? pathScope, int maxEntries, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var repo = Open();

        var commits = pathScope is null
            ? repo.Commits.AsEnumerable()
            : repo.Commits.QueryBy(NormalizeScope(pathScope)).Select(entry => entry.Commit);

        var history = commits
            .Take(maxEntries)
            .Select(c => new GitHistoryEntry(c.Sha, c.Author.Name, c.Author.When, c.MessageShort))
            .ToList();

        return Task.FromResult<IReadOnlyList<GitHistoryEntry>>(history);
    }

    public Task<IReadOnlyList<ChangedFile>> ListChangedFilesAsync(
        string? fromRef, string? toRef, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var repo = Open();

        List<ChangedFile> files;
        if (fromRef is null && toRef is null)
        {
            // Working-tree changes, including new (untracked) files — the common "what did I touch" case.
            files = repo.RetrieveStatus(new Git.StatusOptions { IncludeUntracked = true, RecurseUntrackedDirs = true })
                .Where(entry => entry.State is not (Git.FileStatus.Unaltered or Git.FileStatus.Ignored))
                .Select(entry => new ChangedFile(entry.FilePath.Replace('\\', '/'), MapStatus(entry.State)))
                .ToList();
        }
        else
        {
            var fromTree = ResolveCommit(repo, fromRef ?? "HEAD").Tree;
            var changes = toRef is null
                ? repo.Diff.Compare<Git.TreeChanges>(fromTree, Git.DiffTargets.Index | Git.DiffTargets.WorkingDirectory)
                : repo.Diff.Compare<Git.TreeChanges>(fromTree, ResolveCommit(repo, toRef).Tree);

            files = changes
                .Select(c => new ChangedFile(
                    c.Path.Replace('\\', '/'),
                    MapChange(c.Status),
                    c.Status == Git.ChangeKind.Renamed ? c.OldPath.Replace('\\', '/') : null))
                .ToList();
        }

        return Task.FromResult<IReadOnlyList<ChangedFile>>(files);
    }

    private Git.Repository Open()
    {
        try
        {
            return new Git.Repository(workspace.Root);
        }
        catch (Git.RepositoryNotFoundException)
        {
            throw new DomainRuleException($"'{workspace.Root}' is not a git repository.");
        }
    }

    private static Git.Commit ResolveCommit(Git.Repository repo, string refish)
    {
        if (string.Equals(refish, "HEAD", StringComparison.OrdinalIgnoreCase))
        {
            return repo.Head.Tip ?? throw new DomainRuleException("The repository has no commits.");
        }

        var resolved = repo.Lookup(refish);
        var commit = resolved as Git.Commit
            ?? (resolved as Git.TagAnnotation)?.Target as Git.Commit
            ?? repo.Branches[refish]?.Tip;

        return commit ?? throw new DomainRuleException($"Could not resolve ref '{refish}'.");
    }

    // Enforce containment on a path scope, then hand git the normalized relative form.
    private string[]? ResolveScope(string? pathScope)
    {
        if (pathScope is null)
        {
            return null;
        }

        workspace.Resolve(pathScope); // throws if it escapes the root
        return [NormalizeScope(pathScope)];
    }

    private string NormalizeScope(string pathScope)
    {
        workspace.Resolve(pathScope); // containment guard
        return pathScope.Replace('\\', '/');
    }

    private static ChangeKind MapChange(Git.ChangeKind status) => status switch
    {
        Git.ChangeKind.Added => ChangeKind.Added,
        Git.ChangeKind.Deleted => ChangeKind.Deleted,
        Git.ChangeKind.Renamed => ChangeKind.Renamed,
        _ => ChangeKind.Modified,
    };

    private static ChangeKind MapStatus(Git.FileStatus status)
    {
        if (Has(status, Git.FileStatus.NewInIndex | Git.FileStatus.NewInWorkdir))
        {
            return ChangeKind.Added;
        }

        if (Has(status, Git.FileStatus.DeletedFromIndex | Git.FileStatus.DeletedFromWorkdir))
        {
            return ChangeKind.Deleted;
        }

        if (Has(status, Git.FileStatus.RenamedInIndex | Git.FileStatus.RenamedInWorkdir))
        {
            return ChangeKind.Renamed;
        }

        return ChangeKind.Modified;
    }

    private static bool Has(Git.FileStatus status, Git.FileStatus flags) => (status & flags) != 0;

    private static (string Content, bool Truncated) Bound(string content, int maxBytes)
    {
        if (Encoding.UTF8.GetByteCount(content) <= maxBytes)
        {
            return (content, false);
        }

        // Trim to the byte budget on a char boundary — good enough for a bounded preview.
        var chars = content.Length;
        while (chars > 0 && Encoding.UTF8.GetByteCount(content.AsSpan(0, chars)) > maxBytes)
        {
            chars -= Math.Max(1, (Encoding.UTF8.GetByteCount(content.AsSpan(0, chars)) - maxBytes) / 2);
        }

        return (content[..Math.Max(0, chars)], true);
    }
}
