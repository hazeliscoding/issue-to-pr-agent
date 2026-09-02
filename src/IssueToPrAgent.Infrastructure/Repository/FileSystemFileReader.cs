using System.Text;
using IssueToPrAgent.Application.Repository;
using IssueToPrAgent.Domain;
using IssueToPrAgent.Domain.Repository;

namespace IssueToPrAgent.Infrastructure.Repository;

/// <summary>
/// Reads a file from the workspace, always through the workspace's containment guard and always
/// within a byte budget, so a tool call can neither escape the repo nor return an unbounded
/// blob. Supports a line range for zooming in on a region.
/// </summary>
public sealed class FileSystemFileReader(RepositoryWorkspace workspace) : IFileReader
{
    public async Task<FileContent> ReadAsync(
        string relativePath, FileReadOptions options, CancellationToken cancellationToken)
    {
        var absolute = workspace.Resolve(relativePath); // throws on escape
        if (!File.Exists(absolute))
        {
            throw new DomainRuleException($"File '{relativePath}' does not exist.");
        }

        var allLines = await File.ReadAllLinesAsync(absolute, cancellationToken);
        var totalLines = allLines.Length;

        var startLine = Math.Max(1, options.StartLine ?? 1);
        var skip = startLine - 1;
        var take = options.LineCount ?? Math.Max(0, totalLines - skip);
        var selected = allLines.Skip(skip).Take(take).ToArray();

        // Enforce the byte budget on the selected text, keeping whole lines.
        var (text, linesReturned, truncated) = ApplyByteBudget(selected, options.MaxBytes);

        return new FileContent(
            Path: workspace.ToRelative(absolute),
            Text: text,
            TotalLines: totalLines,
            StartLine: startLine,
            LineCount: linesReturned,
            Truncated: truncated);
    }

    private static (string Text, int Lines, bool Truncated) ApplyByteBudget(string[] lines, int maxBytes)
    {
        var builder = new StringBuilder();
        var bytes = 0;
        var linesReturned = 0;

        foreach (var line in lines)
        {
            var lineBytes = Encoding.UTF8.GetByteCount(line) + 1; // +1 for the newline
            if (bytes + lineBytes > maxBytes && builder.Length > 0)
            {
                return (builder.ToString(), linesReturned, Truncated: true);
            }

            builder.Append(line).Append('\n');
            bytes += lineBytes;
            linesReturned++;
        }

        return (builder.ToString(), linesReturned, Truncated: false);
    }
}
