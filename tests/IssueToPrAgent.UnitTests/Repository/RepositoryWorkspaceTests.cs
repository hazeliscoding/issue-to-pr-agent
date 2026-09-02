using IssueToPrAgent.Domain;
using IssueToPrAgent.Domain.Repository;

namespace IssueToPrAgent.UnitTests.Repository;

public class RepositoryWorkspaceTests
{
    // A rooted absolute path that is valid on the host OS, without touching the disk.
    private static readonly string Root =
        OperatingSystem.IsWindows() ? @"C:\repo" : "/repo";

    private static RepositoryWorkspace Workspace() => new(Root);

    [Fact]
    public void Resolve_returns_a_path_inside_the_root()
    {
        var resolved = Workspace().Resolve("src/app/Program.cs");

        Assert.StartsWith(Path.GetFullPath(Root), resolved);
        Assert.EndsWith("Program.cs", resolved);
    }

    [Theory]
    [InlineData("../secrets.txt")]
    [InlineData("src/../../outside.cs")]
    [InlineData("a/b/../../../escape")]
    public void Resolve_rejects_paths_that_escape_the_root(string relative)
    {
        Assert.Throws<PathEscapeException>(() => Workspace().Resolve(relative));
    }

    [Fact]
    public void Resolve_rejects_absolute_paths()
    {
        var absolute = OperatingSystem.IsWindows() ? @"C:\Windows\system32" : "/etc/passwd";

        Assert.Throws<DomainRuleException>(() => Workspace().Resolve(absolute));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_rejects_empty_paths(string relative)
    {
        Assert.Throws<DomainRuleException>(() => Workspace().Resolve(relative));
    }

    [Fact]
    public void A_sibling_directory_sharing_the_root_prefix_is_not_contained()
    {
        // "repo-evil" must not pass containment for a workspace rooted at "repo".
        var escape = OperatingSystem.IsWindows() ? @"..\repo-evil\x.cs" : "../repo-evil/x.cs";

        Assert.Throws<PathEscapeException>(() => Workspace().Resolve(escape));
    }

    [Fact]
    public void ToRelative_round_trips_a_resolved_path_with_forward_slashes()
    {
        var workspace = Workspace();
        var resolved = workspace.Resolve("src/app/Program.cs");

        Assert.Equal("src/app/Program.cs", workspace.ToRelative(resolved));
    }
}
