namespace IssueToPrAgent.Domain.Fixing;

/// <summary>Whether a change creates a new file or edits an existing one.</summary>
public enum FileOperationKind
{
    Create,
    Edit,
}

/// <summary>
/// A single proposed change to the working tree. <see cref="FileOperationKind.Create"/> writes a
/// new file's full <see cref="Contents"/>; <see cref="FileOperationKind.Edit"/> replaces an exact
/// <see cref="Find"/> anchor with <see cref="Replace"/> in an existing file. Anchored edits keep
/// diffs small and let the apply step reject a change whose anchor is missing or ambiguous, rather
/// than guessing.
/// </summary>
public sealed record FileOperation(
    FileOperationKind Kind,
    string Path,
    string? Contents,
    string? Find,
    string? Replace)
{
    public static FileOperation Create(string path, string contents) =>
        new(FileOperationKind.Create, path, contents, null, null);

    public static FileOperation Edit(string path, string find, string replace) =>
        new(FileOperationKind.Edit, path, null, find, replace);
}
