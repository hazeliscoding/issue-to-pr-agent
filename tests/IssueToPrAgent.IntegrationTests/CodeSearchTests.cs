using IssueToPrAgent.Application.Repository;
using IssueToPrAgent.Infrastructure.Repository;

namespace IssueToPrAgent.IntegrationTests;

public class CodeSearchTests
{
    private static FileSystemCodeSearch SearchOver(TempWorkspace ws) => new(ws.Workspace);

    [Fact]
    public async Task Finds_a_substring_across_files()
    {
        using var ws = new TempWorkspace();
        ws.Write("a.cs", "var widget = 1;");
        ws.Write("b.cs", "// no match here");
        ws.Write("c.cs", "Widget Build() => null;");

        var result = await SearchOver(ws).SearchAsync(new CodeSearchQuery("widget"), CancellationToken.None);

        Assert.Equal(2, result.Matches.Count); // case-insensitive by default
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task Respects_case_sensitivity()
    {
        using var ws = new TempWorkspace();
        ws.Write("a.cs", "widget\nWidget");

        var result = await SearchOver(ws).SearchAsync(
            new CodeSearchQuery("Widget", CaseSensitive: true), CancellationToken.None);

        var match = Assert.Single(result.Matches);
        Assert.Equal(2, match.Line);
    }

    [Fact]
    public async Task Supports_regex()
    {
        using var ws = new TempWorkspace();
        ws.Write("a.cs", "public void Foo() {}\npublic int Bar() {}");

        var result = await SearchOver(ws).SearchAsync(
            new CodeSearchQuery(@"public\s+\w+\s+Bar", IsRegex: true), CancellationToken.None);

        Assert.Single(result.Matches);
    }

    [Fact]
    public async Task Caps_results_at_the_match_budget()
    {
        using var ws = new TempWorkspace();
        ws.Write("a.cs", string.Join('\n', Enumerable.Repeat("needle", 10)));

        var result = await SearchOver(ws).SearchAsync(
            new CodeSearchQuery("needle", MaxMatches: 3), CancellationToken.None);

        Assert.Equal(3, result.Matches.Count);
        Assert.True(result.Truncated);
    }

    [Fact]
    public async Task Skips_build_output_directories()
    {
        using var ws = new TempWorkspace();
        ws.Write("src/App.cs", "needle in source");
        ws.Write("obj/Debug/App.g.cs", "needle in generated output");
        ws.Write("bin/App.dll.cs", "needle in bin");

        var result = await SearchOver(ws).SearchAsync(new CodeSearchQuery("needle"), CancellationToken.None);

        var match = Assert.Single(result.Matches);
        Assert.Equal("src/App.cs", match.Path);
    }

    [Fact]
    public async Task Honors_a_path_glob()
    {
        using var ws = new TempWorkspace();
        ws.Write("src/App.cs", "target");
        ws.Write("src/App.ts", "target");

        var result = await SearchOver(ws).SearchAsync(
            new CodeSearchQuery("target", PathGlob: "**/*.cs"), CancellationToken.None);

        var match = Assert.Single(result.Matches);
        Assert.EndsWith(".cs", match.Path);
    }
}
