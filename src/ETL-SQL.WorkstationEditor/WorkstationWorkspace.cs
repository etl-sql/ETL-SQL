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

    public IReadOnlyList<WorkspaceFolderDto> ListFolders()
    {
        if (!Directory.Exists(Root))
            return [];

        return Directory.EnumerateDirectories(Root, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        })
            .Select(path => new WorkspaceFolderDto(ToRelativePath(path)))
            .OrderBy(folder => folder.Path, StringComparer.OrdinalIgnoreCase)
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
        var entry = RenameEntry(relativePath, newName, isDirectory: false);
        return new WorkspaceFileDto(entry.Path, entry.Size ?? 0);
    }

    public WorkspaceFolderDto CreateFolder(string relativePath)
    {
        if (ReadOnly)
            throw new InvalidOperationException("Workspace is read-only.");

        var path = ResolveDirectoryPath(relativePath);
        if (Directory.Exists(path) || File.Exists(path))
            throw new WorkspaceEntryConflictException($"An entry named '{Path.GetFileName(path)}' already exists.");

        Directory.CreateDirectory(path);
        return new WorkspaceFolderDto(ToRelativePath(path));
    }

    public WorkspaceEntryDto RenameEntry(string relativePath, string newName, bool isDirectory)
    {
        if (ReadOnly)
            throw new InvalidOperationException("Workspace is read-only.");

        var sourcePath = isDirectory ? ResolveDirectoryPath(relativePath) : ResolveEditablePath(relativePath);
        if (isDirectory ? !Directory.Exists(sourcePath) : !File.Exists(sourcePath))
            throw new FileNotFoundException(isDirectory ? "Workspace folder was not found." : "Script file was not found.", relativePath);

        var trimmedName = ValidateEntryName(newName);

        if (!isDirectory && string.IsNullOrEmpty(Path.GetExtension(trimmedName)))
            trimmedName += Path.GetExtension(sourcePath);

        var sourceDirectory = Path.GetDirectoryName(relativePath.Replace('/', Path.DirectorySeparatorChar)) ?? string.Empty;
        var targetRelativePath = Path.Combine(sourceDirectory, trimmedName);
        var targetPath = isDirectory ? ResolveDirectoryPath(targetRelativePath) : ResolveEditablePath(targetRelativePath);
        if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
            return ToEntry(sourcePath, isDirectory);
        if (File.Exists(targetPath) || Directory.Exists(targetPath))
            throw new WorkspaceEntryConflictException($"An entry named '{trimmedName}' already exists.");

        if (isDirectory) Directory.Move(sourcePath, targetPath);
        else File.Move(sourcePath, targetPath);
        return ToEntry(targetPath, isDirectory);
    }

    public WorkspaceFileDto MoveFile(string relativePath, string destinationFolder)
    {
        if (ReadOnly)
            throw new InvalidOperationException("Workspace is read-only.");

        var sourcePath = ResolveEditablePath(relativePath);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Script file was not found.", relativePath);
        var destinationPath = ResolveDirectoryPath(destinationFolder, allowRoot: true);
        if (!Directory.Exists(destinationPath))
            throw new DirectoryNotFoundException("The destination folder was not found.");

        var targetPath = ResolveEditablePath(Path.Combine(destinationFolder, Path.GetFileName(sourcePath)));
        if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
            return new WorkspaceFileDto(ToRelativePath(sourcePath), new FileInfo(sourcePath).Length);
        if (File.Exists(targetPath) || Directory.Exists(targetPath))
            throw new WorkspaceEntryConflictException($"An entry named '{Path.GetFileName(sourcePath)}' already exists in the destination.");

        File.Move(sourcePath, targetPath);
        return new WorkspaceFileDto(ToRelativePath(targetPath), new FileInfo(targetPath).Length);
    }

    public void DeleteEntry(string relativePath, bool isDirectory)
    {
        if (ReadOnly)
            throw new InvalidOperationException("Workspace is read-only.");

        if (isDirectory)
        {
            var path = ResolveDirectoryPath(relativePath);
            if (!Directory.Exists(path))
                throw new DirectoryNotFoundException("Workspace folder was not found.");
            Directory.Delete(path, recursive: true);
            return;
        }

        var filePath = ResolveEditablePath(relativePath);
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Script file was not found.", relativePath);
        File.Delete(filePath);
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

    internal string ResolveDirectoryPath(string relativePath, bool allowRoot = false)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            if (allowRoot) return Root;
            throw new ArgumentException("Folder path is required.", nameof(relativePath));
        }
        if (Path.IsPathRooted(relativePath))
            throw new UnauthorizedAccessException("Absolute paths are not allowed.");

        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = SecurityService.ResolvePathSymlinks(Path.GetFullPath(Path.Combine(Root, normalized)));
        var rootWithSeparator = Root.EndsWith(Path.DirectorySeparatorChar)
            ? Root
            : Root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Path escapes the workspace root.");
        return fullPath;
    }

    private string ToRelativePath(string path) =>
        Path.GetRelativePath(Root, Path.GetFullPath(path)).Replace(Path.DirectorySeparatorChar, '/');

    private static bool IsEditableScript(string path) =>
        EditableExtensions.Contains(Path.GetExtension(path));

    private static string ValidateEntryName(string newName)
    {
        var trimmedName = newName?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
            throw new ArgumentException("A name is required.", nameof(newName));
        if (trimmedName is "." or ".." ||
            !string.Equals(Path.GetFileName(trimmedName), trimmedName, StringComparison.Ordinal) ||
            trimmedName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("Enter a name without a folder path.", nameof(newName));
        }
        return trimmedName;
    }

    private WorkspaceEntryDto ToEntry(string path, bool isDirectory) =>
        new(ToRelativePath(path), isDirectory, isDirectory ? null : new FileInfo(path).Length);

    private static string ComputeRevision(string content) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
}

public sealed record WorkspaceFileDto(string Path, long Size);
public sealed record WorkspaceFolderDto(string Path);
public sealed record WorkspaceEntryDto(string Path, bool IsDirectory, long? Size);
public sealed record WorkspaceFileContent(string Path, string Content, string SourceRevision);
public sealed class WorkspaceSaveConflictException(string message) : Exception(message);
public sealed class WorkspaceEntryConflictException(string message) : Exception(message);
