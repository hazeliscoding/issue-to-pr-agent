using IssueToPrAgent.Application.Fixing;
using IssueToPrAgent.Domain;
using IssueToPrAgent.Domain.Fixing;
using IssueToPrAgent.Domain.Repository;

namespace IssueToPrAgent.Infrastructure.Fixing;

/// <summary>
/// Applies file changes under the workspace, validating the whole batch before writing anything so
/// a bad operation can't leave a half-applied change. Every path goes through the containment
/// guard; an edit's anchor must appear exactly once (missing or ambiguous is refused rather than
/// guessed).
/// </summary>
public sealed class FileSystemFileWriter(RepositoryWorkspace workspace) : IFileWriter
{
    public async Task ApplyAsync(IReadOnlyList<FileOperation> operations, CancellationToken cancellationToken)
    {
        foreach (var operation in operations)
        {
            await ValidateAsync(operation, cancellationToken);
        }

        foreach (var operation in operations)
        {
            await ApplyOneAsync(operation, cancellationToken);
        }
    }

    private async Task ValidateAsync(FileOperation operation, CancellationToken cancellationToken)
    {
        var absolute = workspace.Resolve(operation.Path); // throws PathEscapeException on escape

        if (operation.Kind == FileOperationKind.Create)
        {
            if (operation.Contents is null)
            {
                throw new DomainRuleException($"Create of '{operation.Path}' has no contents.");
            }

            return;
        }

        if (operation.Find is not { Length: > 0 } || operation.Replace is null)
        {
            throw new DomainRuleException($"Edit of '{operation.Path}' needs a find anchor and a replacement.");
        }

        if (!File.Exists(absolute))
        {
            throw new DomainRuleException($"Cannot edit '{operation.Path}': the file does not exist.");
        }

        var text = await File.ReadAllTextAsync(absolute, cancellationToken);
        var occurrences = CountOccurrences(text, operation.Find);
        if (occurrences == 0)
        {
            throw new DomainRuleException($"Edit anchor was not found in '{operation.Path}'.");
        }

        if (occurrences > 1)
        {
            throw new DomainRuleException($"Edit anchor is ambiguous ({occurrences} matches) in '{operation.Path}'.");
        }
    }

    private async Task ApplyOneAsync(FileOperation operation, CancellationToken cancellationToken)
    {
        var absolute = workspace.Resolve(operation.Path);

        if (operation.Kind == FileOperationKind.Create)
        {
            var directory = Path.GetDirectoryName(absolute);
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(absolute, operation.Contents!, cancellationToken);
            return;
        }

        var text = await File.ReadAllTextAsync(absolute, cancellationToken);
        var index = text.IndexOf(operation.Find!, StringComparison.Ordinal);
        var updated = string.Concat(text.AsSpan(0, index), operation.Replace, text.AsSpan(index + operation.Find!.Length));
        await File.WriteAllTextAsync(absolute, updated, cancellationToken);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = text.IndexOf(value, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
