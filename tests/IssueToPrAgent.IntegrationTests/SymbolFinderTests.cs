using IssueToPrAgent.Application.Repository;
using IssueToPrAgent.Domain.Repository;
using IssueToPrAgent.Infrastructure.Repository;

namespace IssueToPrAgent.IntegrationTests;

public class SymbolFinderTests
{
    private static RegexSymbolFinder FinderOver(TempWorkspace ws) => new(ws.Workspace);

    [Fact]
    public async Task Finds_a_csharp_class_and_reports_its_kind()
    {
        using var ws = new TempWorkspace();
        ws.Write("src/OrderService.cs", "namespace X;\npublic sealed class OrderService\n{\n}");

        var results = await FinderOver(ws).FindAsync(new SymbolQuery("OrderService"), CancellationToken.None);

        var location = Assert.Single(results);
        Assert.Equal(SymbolKind.Class, location.Kind);
        Assert.Equal(2, location.Line);
        Assert.Equal("src/OrderService.cs", location.Path);
    }

    [Fact]
    public async Task Finds_a_csharp_method_and_property()
    {
        using var ws = new TempWorkspace();
        ws.Write("src/Svc.cs",
            "public class Svc\n{\n    public int Total { get; private set; }\n    public void Recalculate() { }\n}");

        var method = await FinderOver(ws).FindAsync(new SymbolQuery("Recalculate"), CancellationToken.None);
        Assert.Equal(SymbolKind.Method, Assert.Single(method).Kind);

        var property = await FinderOver(ws).FindAsync(new SymbolQuery("Total"), CancellationToken.None);
        Assert.Equal(SymbolKind.Property, Assert.Single(property).Kind);
    }

    [Fact]
    public async Task Finds_typescript_and_python_declarations()
    {
        using var ws = new TempWorkspace();
        ws.Write("ui/app.ts", "export function loadUser(id: string) { return id; }");
        ws.Write("scripts/tool.py", "class Runner:\n    def execute(self):\n        pass");

        var ts = await FinderOver(ws).FindAsync(new SymbolQuery("loadUser"), CancellationToken.None);
        Assert.Equal(SymbolKind.Function, Assert.Single(ts).Kind);

        var py = await FinderOver(ws).FindAsync(new SymbolQuery("Runner"), CancellationToken.None);
        Assert.Equal(SymbolKind.Class, Assert.Single(py).Kind);
    }

    [Fact]
    public async Task Narrows_results_by_kind()
    {
        using var ws = new TempWorkspace();
        // A method named the same as a type elsewhere.
        ws.Write("a.cs", "public class Build { }");
        ws.Write("b.cs", "public class Other\n{\n    public void Build()\n    {\n    }\n}");

        var onlyMethods = await FinderOver(ws).FindAsync(
            new SymbolQuery("Build", Kind: SymbolKind.Method), CancellationToken.None);

        var location = Assert.Single(onlyMethods);
        Assert.Equal(SymbolKind.Method, location.Kind);
        Assert.Equal("b.cs", location.Path);
    }

    [Fact]
    public async Task Returns_nothing_for_an_unknown_symbol()
    {
        using var ws = new TempWorkspace();
        ws.Write("a.cs", "public class Known { }");

        var results = await FinderOver(ws).FindAsync(new SymbolQuery("Unknown"), CancellationToken.None);

        Assert.Empty(results);
    }
}
