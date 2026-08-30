using System.Security.Cryptography;
using ETL_SQL.Services;

namespace ETL_SQL.WorkstationEditor;

public sealed class WorkstationWorkspace
{
    private static readonly HashSet<string> EditableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".etlsql",
        ".rptsql",
        ".sql"
    };

    public WorkstationWorkspace(string workspaceRoot, bool readOnly)
    {
        Root = SecurityService.ResolvePathSymlinks(Path.GetFullPath(workspaceRoot));
        ReadOnly = readOnly;
    }

    public string Root { get; }
    public bool ReadOnly { get; }

    public IReadOnlyList<WorkspaceFileDto> ListFiles()
    {
        if (!Directory.Exists(Root))
            return [];

        return Directory.EnumerateFiles(Root, "*.*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        })
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

    public async Task<WorkspaceFileContent> ReadFileAsync(string relativePath, CancellationToken cancellationToken)
    {
        var content = await ReadTextAsync(relativePath, cancellationToken);
        return new WorkspaceFileContent(relativePath, content, ComputeRevision(content));
    }

    public async Task WriteTextAsync(string relativePath, string content, CancellationToken cancellationToken)
        => await WriteTextAsync(relativePath, content, null, cancellationToken);

    public async Task<string> WriteTextAsync(
        string relativePath,
        string content,
        string? baseRevision,
        CancellationToken cancellationToken)
    {
        if (ReadOnly)
            throw new InvalidOperationException("Workspace is read-only.");

        var path = ResolveEditablePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!string.IsNullOrWhiteSpace(baseRevision) && File.Exists(path))
        {
            var current = await File.ReadAllTextAsync(path, cancellationToken);
            if (!string.Equals(baseRevision, ComputeRevision(current), StringComparison.OrdinalIgnoreCase))
            {
                throw new WorkspaceSaveConflictException("The file changed outside this Studio instance. Reopen it before saving.");
            }
        }
        await File.WriteAllTextAsync(path, content, cancellationToken);
        return ComputeRevision(content);
    }

    public async Task<string> GetRevisionAsync(string relativePath, CancellationToken cancellationToken)
    {
        var content = await ReadTextAsync(relativePath, cancellationToken);
        return ComputeRevision(content);
    }

    public WorkspaceFileDto RenameFile(string relativePath, string newName)
    {
        if (ReadOnly)
            throw new InvalidOperationException("Workspace is read-only.");

        var sourcePath = ResolveEditablePath(relativePath);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Script file was not found.", relativePath);

        var trimmedName = newName?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
            throw new ArgumentException("A file name is required.", nameof(newName));
        if (!string.Equals(Path.GetFileName(trimmedName), trimmedName, StringComparison.Ordinal) ||
            trimmedName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("Enter a file name without a folder path.", nameof(newName));
        }

        if (string.IsNullOrEmpty(Path.GetExtension(trimmedName)))
            trimmedName += Path.GetExtension(sourcePath);

        var sourceDirectory = Path.GetDirectoryName(relativePath.Replace('/', Path.DirectorySeparatorChar)) ?? string.Empty;
        var targetRelativePath = Path.Combine(sourceDirectory, trimmedName);
        var targetPath = ResolveEditablePath(targetRelativePath);
        if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
            return new WorkspaceFileDto(ToRelativePath(sourcePath), new FileInfo(sourcePath).Length);
        if (File.Exists(targetPath))
            throw new WorkspaceRenameConflictException($"A file named '{trimmedName}' already exists.");

        File.Move(sourcePath, targetPath);
        return new WorkspaceFileDto(ToRelativePath(targetPath), new FileInfo(targetPath).Length);
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
        var fullPath = SecurityService.ResolvePathSymlinks(Path.GetFullPath(Path.Combine(Root, normalized)));
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

    private static string ComputeRevision(string content) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
}

public sealed record WorkspaceFileDto(string Path, long Size);
public sealed record WorkspaceFileContent(string Path, string Content, string SourceRevision);
public sealed class WorkspaceSaveConflictException(string message) : Exception(message);
public sealed class WorkspaceRenameConflictException(string message) : Exception(message);
