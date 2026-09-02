using IssueToPrAgent.Application.Repository;
using IssueToPrAgent.Domain;
using IssueToPrAgent.Infrastructure.Repository;

namespace IssueToPrAgent.IntegrationTests;

public class FileReaderTests
{
    [Fact]
    public async Task Reads_a_whole_file()
    {
        using var ws = new TempWorkspace();
        ws.Write("src/Program.cs", "line1\nline2\nline3\n");
        var reader = new FileSystemFileReader(ws.Workspace);

        var result = await reader.ReadAsync("src/Program.cs", new FileReadOptions(), CancellationToken.None);

        Assert.Equal(3, result.TotalLines);
        Assert.Equal(3, result.LineCount);
        Assert.False(result.Truncated);
        Assert.Contains("line2", result.Text);
        Assert.Equal("src/Program.cs", result.Path);
    }

    [Fact]
    public async Task Reads_a_line_range()
    {
        using var ws = new TempWorkspace();
        ws.Write("a.txt", "one\ntwo\nthree\nfour\n");
        var reader = new FileSystemFileReader(ws.Workspace);

        var result = await reader.ReadAsync("a.txt", new FileReadOptions(StartLine: 2, LineCount: 2), CancellationToken.None);

        Assert.Equal(2, result.StartLine);
        Assert.Equal(2, result.LineCount);
        Assert.Equal("two\nthree\n", result.Text);
        Assert.Equal(4, result.TotalLines); // still reports the whole-file size
    }

    [Fact]
    public async Task Enforces_the_byte_budget_and_flags_truncation()
    {
        using var ws = new TempWorkspace();
        ws.Write("big.txt", "aaaa\nbbbb\ncccc\ndddd\n");
        var reader = new FileSystemFileReader(ws.Workspace);

        var result = await reader.ReadAsync("big.txt", new FileReadOptions(MaxBytes: 6), CancellationToken.None);

        Assert.True(result.Truncated);
        Assert.True(result.LineCount < 4);
    }

    [Fact]
    public async Task A_missing_file_is_a_rule_violation()
    {
        using var ws = new TempWorkspace();
        var reader = new FileSystemFileReader(ws.Workspace);

        await Assert.ThrowsAsync<DomainRuleException>(() =>
            reader.ReadAsync("nope.cs", new FileReadOptions(), CancellationToken.None));
    }

    [Fact]
    public async Task A_path_escape_is_blocked_before_any_read()
    {
        using var ws = new TempWorkspace();
        var reader = new FileSystemFileReader(ws.Workspace);

        await Assert.ThrowsAsync<PathEscapeException>(() =>
            reader.ReadAsync("../outside.cs", new FileReadOptions(), CancellationToken.None));
    }
}
