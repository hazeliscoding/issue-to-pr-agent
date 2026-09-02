using IssueToPrAgent.Domain;
using IssueToPrAgent.Domain.Fixing;
using IssueToPrAgent.Infrastructure.Fixing;

namespace IssueToPrAgent.IntegrationTests.Fixing;

public class FileSystemFileWriterTests
{
    private static FileSystemFileWriter WriterOver(TempWorkspace ws) => new(ws.Workspace);

    [Fact]
    public async Task Create_writes_a_new_file_including_nested_directories()
    {
        using var ws = new TempWorkspace();

        await WriterOver(ws).ApplyAsync([FileOperation.Create("src/new/Thing.cs", "public class Thing {}")], CancellationToken.None);

        Assert.Equal("public class Thing {}", File.ReadAllText(Path.Combine(ws.Root, "src", "new", "Thing.cs")));
    }

    [Fact]
    public async Task Edit_replaces_the_anchor()
    {
        using var ws = new TempWorkspace();
        ws.Write("src/Calc.cs", "int Add(int a, int b) => a - b;");

        await WriterOver(ws).ApplyAsync([FileOperation.Edit("src/Calc.cs", "a - b", "a + b")], CancellationToken.None);

        Assert.Contains("a + b", File.ReadAllText(Path.Combine(ws.Root, "src", "Calc.cs")));
    }

    [Fact]
    public async Task Edit_with_a_missing_anchor_is_rejected()
    {
        using var ws = new TempWorkspace();
        ws.Write("src/Calc.cs", "int Add(int a, int b) => a + b;");

        await Assert.ThrowsAsync<DomainRuleException>(() =>
            WriterOver(ws).ApplyAsync([FileOperation.Edit("src/Calc.cs", "nonexistent", "x")], CancellationToken.None));
    }

    [Fact]
    public async Task Edit_with_an_ambiguous_anchor_is_rejected()
    {
        using var ws = new TempWorkspace();
        ws.Write("src/Calc.cs", "x = 1;\nx = 1;");

        await Assert.ThrowsAsync<DomainRuleException>(() =>
            WriterOver(ws).ApplyAsync([FileOperation.Edit("src/Calc.cs", "x = 1;", "x = 2;")], CancellationToken.None));
    }

    [Fact]
    public async Task A_path_escape_is_blocked()
    {
        using var ws = new TempWorkspace();

        await Assert.ThrowsAsync<PathEscapeException>(() =>
            WriterOver(ws).ApplyAsync([FileOperation.Create("../evil.cs", "x")], CancellationToken.None));
    }

    [Fact]
    public async Task A_bad_operation_fails_the_whole_batch_without_partial_writes()
    {
        using var ws = new TempWorkspace();

        // The create is valid, the edit's target doesn't exist: the batch must be rejected whole.
        await Assert.ThrowsAsync<DomainRuleException>(() => WriterOver(ws).ApplyAsync(
        [
            FileOperation.Create("src/Good.cs", "ok"),
            FileOperation.Edit("src/Missing.cs", "a", "b"),
        ], CancellationToken.None));

        Assert.False(File.Exists(Path.Combine(ws.Root, "src", "Good.cs"))); // nothing was written
    }
}
