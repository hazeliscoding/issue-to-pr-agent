using IssueToPrAgent.Domain.Repository;
using Git = LibGit2Sharp;

namespace IssueToPrAgent.IntegrationTests;

/// <summary>
/// A throwaway repository on disk for exercising the filesystem- and git-backed tools against
/// real I/O. Writes files, optionally inits a git repo, and cleans itself up.
/// </summary>
internal sealed class TempWorkspace : IDisposable
{
    public string Root { get; }
    public RepositoryWorkspace Workspace { get; }

    public TempWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "i2pr-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Workspace = new RepositoryWorkspace(Root);
    }

    public void Write(string relativePath, string content)
    {
        var full = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    public void InitGit() => Git.Repository.Init(Root);

    /// <summary>Stages everything and commits it, returning the commit sha.</summary>
    public string CommitAll(string message)
    {
        using var repo = new Git.Repository(Root);
        Git.Commands.Stage(repo, "*");
        var signature = new Git.Signature("Test", "test@example.com", DateTimeOffset.UtcNow);
        return repo.Commit(message, signature, signature, new Git.CommitOptions()).Sha;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup: a lingering file handle shouldn't fail the test run.
        }
    }
}
