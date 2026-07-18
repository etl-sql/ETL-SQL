namespace ETL_SQL.WorkstationEditor;

public sealed class WorkstationWorkspace
{
    private static readonly HashSet<string> EditableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".etlsql",
        ".rptsql"
    };

    public WorkstationWorkspace(string workspaceRoot, bool readOnly)
    {
        Root = Path.GetFullPath(workspaceRoot);
        ReadOnly = readOnly;
    }

    public string Root { get; }
    public bool ReadOnly { get; }

    public IReadOnlyList<WorkspaceFileDto> ListFiles()
    {
        if (!Directory.Exists(Root))
            return [];

        return Directory.EnumerateFiles(Root, "*.*", SearchOption.AllDirectories)
            .Where(IsEditableScript)
            .Select(path => new WorkspaceFileDto(ToRelativePath(path), new FileInfo(path).Length))
            .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .Take(500)
            .ToList();
    }

    public async Task<string> ReadTextAsync(string relativePath, CancellationToken cancellationToken)
    {
        var path = ResolveEditablePath(relativePath);
        if (!File.Exists(path))
            throw new FileNotFoundException("Script file was not found.", relativePath);

        return await File.ReadAllTextAsync(path, cancellationToken);
    }

    public async Task WriteTextAsync(string relativePath, string content, CancellationToken cancellationToken)
    {
        if (ReadOnly)
            throw new InvalidOperationException("Workspace is read-only.");

        var path = ResolveEditablePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, cancellationToken);
    }

    public string? InitialRelativeFile(string? initialFile) =>
        string.IsNullOrWhiteSpace(initialFile) ? null : ToRelativePath(initialFile);

    internal string ResolveEditablePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("Path is required.", nameof(relativePath));

        if (Path.IsPathRooted(relativePath))
            throw new UnauthorizedAccessException("Absolute paths are not allowed.");

        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(Root, normalized));
        var rootWithSeparator = Root.EndsWith(Path.DirectorySeparatorChar)
            ? Root
            : Root + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(fullPath, Root, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Path escapes the workspace root.");
        }

        if (!IsEditableScript(fullPath))
            throw new UnauthorizedAccessException("Only .etlsql and .rptsql files are editable.");

        return fullPath;
    }

    private string ToRelativePath(string path) =>
        Path.GetRelativePath(Root, Path.GetFullPath(path)).Replace(Path.DirectorySeparatorChar, '/');

    private static bool IsEditableScript(string path) =>
        EditableExtensions.Contains(Path.GetExtension(path));
}

public sealed record WorkspaceFileDto(string Path, long Size);
