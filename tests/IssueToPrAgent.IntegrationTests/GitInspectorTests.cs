using IssueToPrAgent.Application.Repository;
using IssueToPrAgent.Domain.Repository;
using IssueToPrAgent.Infrastructure.Repository;

namespace IssueToPrAgent.IntegrationTests;

public class GitInspectorTests
{
    private static LibGit2GitInspector InspectorOver(TempWorkspace ws) => new(ws.Workspace);

    [Fact]
    public async Task History_lists_commits_newest_first()
    {
        using var ws = new TempWorkspace();
        ws.InitGit();
        ws.Write("a.txt", "one");
        ws.CommitAll("first commit");
        ws.Write("b.txt", "two");
        ws.CommitAll("second commit");

        var history = await InspectorOver(ws).GetHistoryAsync(pathScope: null, maxEntries: 10, CancellationToken.None);

        Assert.Equal(2, history.Count);
        Assert.Equal("second commit", history[0].Message);
        Assert.Equal("first commit", history[1].Message);
        Assert.All(history, e => Assert.NotEmpty(e.Sha));
    }

    [Fact]
    public async Task History_can_be_scoped_to_a_path()
    {
        using var ws = new TempWorkspace();
        ws.InitGit();
        ws.Write("a.txt", "one");
        ws.CommitAll("touches a");
        ws.Write("b.txt", "two");
        ws.CommitAll("touches b");

        var history = await InspectorOver(ws).GetHistoryAsync("b.txt", maxEntries: 10, CancellationToken.None);

        var entry = Assert.Single(history);
        Assert.Equal("touches b", entry.Message);
    }

    [Fact]
    public async Task Changed_files_reports_working_tree_modifications_and_additions()
    {
        using var ws = new TempWorkspace();
        ws.InitGit();
        ws.Write("tracked.cs", "original");
        ws.CommitAll("baseline");

        ws.Write("tracked.cs", "edited");  // modify a tracked file
        ws.Write("new.cs", "brand new");   // add an untracked file

        var changed = await InspectorOver(ws).ListChangedFilesAsync(fromRef: null, toRef: null, CancellationToken.None);

        Assert.Contains(changed, c => c.Path == "tracked.cs" && c.Kind == ChangeKind.Modified);
        Assert.Contains(changed, c => c.Path == "new.cs" && c.Kind == ChangeKind.Added);
    }

    [Fact]
    public async Task Diff_shows_uncommitted_changes_to_tracked_files()
    {
        using var ws = new TempWorkspace();
        ws.InitGit();
        ws.Write("code.cs", "return 1;\n");
        ws.CommitAll("baseline");
        ws.Write("code.cs", "return 2;\n");

        var diff = await InspectorOver(ws).GetDiffAsync(new DiffOptions(), CancellationToken.None);

        Assert.True(diff.FilesChanged >= 1);
        Assert.Contains("-return 1;", diff.Patch);
        Assert.Contains("+return 2;", diff.Patch);
        Assert.False(diff.Truncated);
    }

    [Fact]
    public async Task Diff_between_two_commits_shows_the_change()
    {
        using var ws = new TempWorkspace();
        ws.InitGit();
        ws.Write("code.cs", "v1\n");
        var first = ws.CommitAll("v1");
        ws.Write("code.cs", "v2\n");
        var second = ws.CommitAll("v2");

        var diff = await InspectorOver(ws).GetDiffAsync(
            new DiffOptions(FromRef: first, ToRef: second), CancellationToken.None);

        Assert.Contains("-v1", diff.Patch);
        Assert.Contains("+v2", diff.Patch);
    }

    [Fact]
    public async Task An_unresolvable_ref_is_a_rule_violation()
    {
        using var ws = new TempWorkspace();
        ws.InitGit();
        ws.Write("a.txt", "x");
        ws.CommitAll("init");

        await Assert.ThrowsAsync<Domain.DomainRuleException>(() =>
            InspectorOver(ws).GetDiffAsync(new DiffOptions(FromRef: "no-such-ref", ToRef: "HEAD"), CancellationToken.None));
    }
}
