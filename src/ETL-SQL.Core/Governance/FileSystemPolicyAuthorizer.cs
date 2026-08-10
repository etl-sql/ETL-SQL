using ETL_SQL.Services;

namespace ETL_SQL.Core.Governance;

public enum FileSystemAccessKind
{
    Read,
    Enumerate,
    Write,
    Delete,
    Move,
    Extract
}

public sealed record AuthorizedFileSystemPath(
    string CanonicalPath,
    FileSystemAccessKind Access,
    OperationPolicyDecision Decision);

public sealed class FileSystemPolicyDeniedException(
    OperationPolicyDecision decision,
    Exception? innerException = null)
    : SecurityException(decision.Reason), ISecurityEventEmittedException
{
    public OperationPolicyDecision Decision { get; } = decision;
    public Exception? AuthorizationFailure { get; } = innerException;
}

/// <summary>
/// Canonical operation boundary for script-selected filesystem paths.
/// </summary>
public sealed class FileSystemPolicyAuthorizer(SecurityService securityService)
{
    public AuthorizedFileSystemPath Authorize(
        IExecutionContext context,
        string path,
        FileSystemAccessKind access,
        bool validateFileType = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var snapshot = RefreshSnapshotAtBoundary(context);
        string canonical;
        try
        {
            RejectNonCanonicalWindowsForms(path);
            canonical = SecurityService.ResolvePathSymlinks(Path.GetFullPath(path));
            RejectNonCanonicalWindowsForms(canonical);
            securityService.ValidatePath(canonical);
            if (IsMutation(access)) securityService.ValidateWriteAccess(canonical);
            context.StorageCapability?.RequirePath(canonical, IsMutation(access));
            if (validateFileType) securityService.ValidateFileType(canonical,
                context.AllowUnknownFileTypes, context.AllowedFileTypeOverrides);
            EnforceEnterpriseRoots(snapshot, canonical, access);
            EnforceWriteExtensions(snapshot, canonical, access);
        }
        catch (SecurityException ex) when (ex is not FileSystemPolicyDeniedException)
        {
            var denied = OperationPolicyDecision.Deny(snapshot, PolicyKey(access),
                Sanitize(canonical: null, path), EffectiveConstraint(snapshot, access), ex.Message);
            throw new FileSystemPolicyDeniedException(denied, ex);
        }

        var allowed = OperationPolicyDecision.Allow(snapshot, PolicyKey(access),
            Sanitize(canonical, path), EffectiveConstraint(snapshot, access), "Filesystem access allowed.");
        return new AuthorizedFileSystemPath(canonical, access, allowed);
    }

    /// <summary>
    /// Opens the authorized file for reading and verifies the OS-resolved final path of the
    /// opened handle still matches the authorized canonical target (link-race re-check).
    /// </summary>
    public FileStream OpenValidatedRead(IExecutionContext context, AuthorizedFileSystemPath authorized) =>
        OpenValidatedCore(context, authorized, FileMode.Open, FileAccess.Read, FileShare.Read,
            truncate: false);

    /// <summary>
    /// Opens the authorized file for writing with the handle-based link-race re-check. The file
    /// is opened without truncation first; only after the handle's final path is verified is it
    /// truncated, so a swapped link never destroys data at an unauthorized target.
    /// </summary>
    public FileStream OpenValidatedWrite(
        IExecutionContext context,
        AuthorizedFileSystemPath authorized,
        bool truncate = true,
        bool failIfExists = false) =>
        OpenValidatedCore(context, authorized,
            failIfExists ? FileMode.CreateNew : FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None, truncate);

    public bool DeleteValidatedFile(
        IExecutionContext context,
        AuthorizedFileSystemPath authorized,
        bool ifExists = false)
    {
        if (!File.Exists(authorized.CanonicalPath))
        {
            return false;
        }

        ValidateExistingFileHandle(context, authorized);
        ValidateCanonicalPathStillMatches(context, authorized);
        File.Delete(authorized.CanonicalPath);
        return true;
    }

    public void MoveValidatedFile(
        IExecutionContext context,
        AuthorizedFileSystemPath source,
        AuthorizedFileSystemPath destination,
        bool overwrite)
    {
        ValidateExistingFileHandle(context, source);
        ValidateCanonicalPathStillMatches(context, source);
        ValidateCanonicalPathStillMatches(context, destination);

        if (File.Exists(destination.CanonicalPath))
        {
            if (!overwrite)
                throw new IOException($"Destination file already exists: {destination.CanonicalPath}");

            DeleteValidatedFile(context, destination);
        }

        File.Move(source.CanonicalPath, destination.CanonicalPath);
    }

    public bool DeleteValidatedDirectory(
        IExecutionContext context,
        AuthorizedFileSystemPath authorized,
        bool recursive,
        bool ifExists = false)
    {
        if (!Directory.Exists(authorized.CanonicalPath))
        {
            return false;
        }

        ValidateCanonicalPathStillMatches(context, authorized);
        Directory.Delete(authorized.CanonicalPath, recursive);
        return true;
    }

    public void MoveValidatedDirectory(
        IExecutionContext context,
        AuthorizedFileSystemPath source,
        AuthorizedFileSystemPath destination,
        bool overwrite)
    {
        ValidateCanonicalPathStillMatches(context, source);
        ValidateCanonicalPathStillMatches(context, destination);

        if (Directory.Exists(destination.CanonicalPath))
        {
            if (!overwrite)
                throw new IOException($"Destination directory already exists: {destination.CanonicalPath}");

            DeleteValidatedDirectory(context, destination, recursive: true);
        }

        Directory.Move(source.CanonicalPath, destination.CanonicalPath);
    }

    private FileStream OpenValidatedCore(
        IExecutionContext context,
        AuthorizedFileSystemPath authorized,
        FileMode mode,
        FileAccess fileAccess,
        FileShare share,
        bool truncate)
    {
        var stream = new FileStream(authorized.CanonicalPath, mode, fileAccess, share, 4096,
            FileOptions.Asynchronous);
        try
        {
            var finalPath = FileHandleFinalPath.Resolve(stream.SafeFileHandle);
            if (finalPath != null && !FileHandleFinalPath.Matches(finalPath, authorized.CanonicalPath))
            {
                var snapshot = RefreshSnapshotAtBoundary(context);
                throw new FileSystemPolicyDeniedException(OperationPolicyDecision.Deny(snapshot,
                    PolicyKey(authorized.Access),
                    Sanitize(authorized.CanonicalPath, authorized.CanonicalPath),
                    EffectiveConstraint(snapshot, authorized.Access),
                    "The opened file handle resolved to a different final path than the authorized target (possible link substitution)."));
            }
            if (truncate && fileAccess != FileAccess.Read) stream.SetLength(0);
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private void ValidateExistingFileHandle(
        IExecutionContext context,
        AuthorizedFileSystemPath authorized)
    {
        using var stream = OpenValidatedCore(context, authorized, FileMode.Open,
            FileAccess.Read, FileShare.None, truncate: false);
    }

    private void ValidateCanonicalPathStillMatches(
        IExecutionContext context,
        AuthorizedFileSystemPath authorized)
    {
        string resolved;
        try
        {
            RejectNonCanonicalWindowsForms(authorized.CanonicalPath);
            resolved = SecurityService.ResolvePathSymlinks(Path.GetFullPath(authorized.CanonicalPath));
            RejectNonCanonicalWindowsForms(resolved);
        }
        catch (SecurityException ex)
        {
            var snapshot = RefreshSnapshotAtBoundary(context);
            throw new FileSystemPolicyDeniedException(OperationPolicyDecision.Deny(snapshot,
                PolicyKey(authorized.Access),
                Sanitize(authorized.CanonicalPath, authorized.CanonicalPath),
                EffectiveConstraint(snapshot, authorized.Access),
                ex.Message), ex);
        }

        if (!FileHandleFinalPath.Matches(resolved, authorized.CanonicalPath))
        {
            var snapshot = RefreshSnapshotAtBoundary(context);
            throw new FileSystemPolicyDeniedException(OperationPolicyDecision.Deny(snapshot,
                PolicyKey(authorized.Access),
                Sanitize(authorized.CanonicalPath, authorized.CanonicalPath),
                EffectiveConstraint(snapshot, authorized.Access),
                "The path resolved to a different canonical target than the authorized target (possible link substitution)."));
        }
    }

    private static ExecutionPolicySnapshot RefreshSnapshotAtBoundary(IExecutionContext context) =>
        OperationPolicyBoundary.Refresh(context, "<filesystem-operation>");

    /// <summary>
    /// Rejects Windows path forms that bypass canonical validation: NT/device namespace
    /// prefixes (which skip Win32 normalization), NTFS alternate data streams (which pass
    /// root-prefix checks while evading extension checks), and trailing dot/space segments
    /// (which Win32 strips at open time, defeating extension and script-immutability checks).
    /// </summary>
    private static void RejectNonCanonicalWindowsForms(string path)
    {
        if (!OperatingSystem.IsWindows()) return;

        if (path.StartsWith(@"\\?\", StringComparison.Ordinal)
            || path.StartsWith(@"\\.\", StringComparison.Ordinal)
            || path.StartsWith(@"\??\", StringComparison.Ordinal)
            || path.StartsWith(@"//?/", StringComparison.Ordinal)
            || path.StartsWith(@"//./", StringComparison.Ordinal))
        {
            throw new SecurityException(
                "Device and NT-namespace path prefixes are not permitted for script-selected paths.");
        }

        var root = Path.GetPathRoot(path) ?? string.Empty;
        var segments = path[root.Length..]
            .Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            if (segment.Contains(':'))
                throw new SecurityException(
                    "Alternate data stream syntax is not permitted for script-selected paths.");
            if (segment[^1] is '.' or ' ' && segment is not "." and not "..")
                throw new SecurityException(
                    "Path segments ending in a dot or space are not permitted for script-selected paths.");
        }
    }

    private static void EnforceEnterpriseRoots(
        ExecutionPolicySnapshot snapshot,
        string canonical,
        FileSystemAccessKind access)
    {
        if (!snapshot.IsEnrolled) return;
        var roots = snapshot.GovernedValues
            .Where(pair => pair.Key.StartsWith("Security:ApprovedSafeZones:",
                StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => SecurityService.ResolvePathSymlinks(Path.GetFullPath(pair.Value!)))
            .ToArray();
        if (roots.Length == 0) return;
        if (roots.Any(root => SafePath.TryResolveWithinRoot(root, canonical, out _))) return;

        throw new SecurityException($"Enterprise policy denied {access.ToString().ToLowerInvariant()} access outside approved filesystem roots.");
    }

    /// <summary>
    /// When an enrolled policy declares an allowed write-extension set, denies a mutating access
    /// whose canonical target has an extension outside it. Directory-targeting operations
    /// (Enumerate) and extension-less writes are not constrained here.
    /// </summary>
    private static void EnforceWriteExtensions(
        ExecutionPolicySnapshot snapshot,
        string canonical,
        FileSystemAccessKind access)
    {
        if (!snapshot.IsEnrolled || !IsMutation(access)) return;
        var allowed = snapshot.GovernedValues
            .Where(pair => pair.Key.StartsWith("Security:AllowedWriteExtensions:",
                StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => pair.Value!.Trim().TrimStart('.'))
            .ToArray();
        if (allowed.Length == 0) return;

        var extension = Path.GetExtension(canonical).TrimStart('.');
        if (extension.Length == 0) return;
        if (allowed.Any(value => string.Equals(value, extension, StringComparison.OrdinalIgnoreCase)))
            return;

        throw new SecurityException(
            $"Enterprise policy limits write extensions to [{string.Join(", ", allowed)}]; '.{extension}' is not permitted.");
    }

    private static bool IsMutation(FileSystemAccessKind access) =>
        access is FileSystemAccessKind.Write or FileSystemAccessKind.Delete
            or FileSystemAccessKind.Move or FileSystemAccessKind.Extract;

    private static string PolicyKey(FileSystemAccessKind access) => IsMutation(access)
        ? "Filesystem:Write"
        : "Filesystem:Read";

    private static string EffectiveConstraint(ExecutionPolicySnapshot snapshot, FileSystemAccessKind access) =>
        snapshot.IsEnrolled ? $"enterprise approved roots; access={access}" : $"local path guardrails; access={access}";

    private static string Sanitize(string? canonical, string requested) =>
        Path.GetFileName(canonical ?? requested) is { Length: > 0 } fileName
            ? $"<path>/{fileName}"
            : "<path>";
}
