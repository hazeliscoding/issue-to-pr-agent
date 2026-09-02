using System.Text.RegularExpressions;
using IssueToPrAgent.Application.Repository;
using IssueToPrAgent.Domain;
using IssueToPrAgent.Domain.Repository;

namespace IssueToPrAgent.Infrastructure.Repository;

/// <summary>
/// Finds where a named symbol is declared using language-aware regexes over declaration syntax
/// (no compilation). Deliberately broad-but-approximate: it covers C#, TypeScript/JavaScript,
/// and Python declarations well enough to point the agent at the right file and line, which is
/// all Phase 1 needs. It matches definitions, not references.
/// </summary>
public sealed class RegexSymbolFinder(RepositoryWorkspace workspace) : ISymbolFinder
{
    // A declaration pattern: the regex, the group holding the symbol name, and how to decide the
    // kind — either a fixed kind or a keyword capture group mapped through KindFromKeyword.
    private sealed record Pattern(Regex Regex, string NameGroup, SymbolKind? FixedKind, string? KeywordGroup = null);

    private static readonly Pattern[] CSharp =
    [
        new(new Regex(@"\b(?<kw>class|interface|record|struct|enum)\s+(?<name>\w+)", RegexOptions.Compiled),
            "name", null, "kw"),
        new(new Regex(@"^\s*(?:\[[^\]]*\]\s*)*(?:(?:public|private|protected|internal|static|virtual|override|sealed|abstract|async|new|partial|unsafe|extern)\s+)+[\w\.<>\[\],\?]+\s+(?<name>\w+)\s*(?:<[^>]*>)?\s*\(", RegexOptions.Compiled),
            "name", SymbolKind.Method),
        new(new Regex(@"^\s*(?:\[[^\]]*\]\s*)*(?:(?:public|private|protected|internal|static|virtual|override|required)\s+)+[\w\.<>\[\],\?]+\s+(?<name>\w+)\s*\{\s*(?:get|set|init)", RegexOptions.Compiled),
            "name", SymbolKind.Property),
    ];

    private static readonly Pattern[] TypeScript =
    [
        new(new Regex(@"\b(?<kw>class|interface|enum)\s+(?<name>\w+)", RegexOptions.Compiled), "name", null, "kw"),
        new(new Regex(@"\bfunction\s+(?<name>\w+)", RegexOptions.Compiled), "name", SymbolKind.Function),
        new(new Regex(@"\b(?:const|let|var)\s+(?<name>\w+)\s*=\s*(?:async\s*)?\(", RegexOptions.Compiled), "name", SymbolKind.Function),
        new(new Regex(@"\btype\s+(?<name>\w+)\s*=", RegexOptions.Compiled), "name", SymbolKind.Type),
    ];

    private static readonly Pattern[] Python =
    [
        new(new Regex(@"^\s*class\s+(?<name>\w+)", RegexOptions.Compiled), "name", SymbolKind.Class),
        new(new Regex(@"^\s*def\s+(?<name>\w+)", RegexOptions.Compiled), "name", SymbolKind.Function),
    ];

    private static readonly Dictionary<string, Pattern[]> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = CSharp,
        [".ts"] = TypeScript,
        [".tsx"] = TypeScript,
        [".js"] = TypeScript,
        [".jsx"] = TypeScript,
        [".py"] = Python,
    };

    public async Task<IReadOnlyList<SymbolLocation>> FindAsync(SymbolQuery query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.Name))
        {
            throw new DomainRuleException("A symbol query requires a name.");
        }

        var results = new List<SymbolLocation>();
        var seen = new HashSet<(string Path, int Line, SymbolKind Kind)>();

        foreach (var file in WorkspaceFiles.Enumerate(workspace))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!ByExtension.TryGetValue(Path.GetExtension(file), out var patterns))
            {
                continue;
            }

            string[] lines;
            try
            {
                lines = await File.ReadAllLinesAsync(file, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var relative = workspace.ToRelative(file);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (var pattern in patterns)
                {
                    var match = pattern.Regex.Match(lines[i]);
                    if (!match.Success || match.Groups[pattern.NameGroup].Value != query.Name)
                    {
                        continue;
                    }

                    var kind = pattern.FixedKind ?? KindFromKeyword(match.Groups[pattern.KeywordGroup!].Value);
                    if (query.Kind is not null && query.Kind != kind)
                    {
                        continue;
                    }

                    if (seen.Add((relative, i + 1, kind)))
                    {
                        results.Add(new SymbolLocation(relative, i + 1, kind, query.Name, lines[i].Trim()));
                        if (results.Count >= query.MaxResults)
                        {
                            return results;
                        }
                    }
                }
            }
        }

        return results;
    }

    private static SymbolKind KindFromKeyword(string keyword) => keyword switch
    {
        "class" => SymbolKind.Class,
        "interface" => SymbolKind.Interface,
        "record" => SymbolKind.Record,
        "struct" => SymbolKind.Struct,
        "enum" => SymbolKind.Enum,
        _ => SymbolKind.Unknown,
    };
}
