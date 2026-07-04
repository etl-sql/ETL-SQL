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
    : SecurityException(decision.Reason)
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
            if (validateFileType) securityService.ValidateFileType(canonical,
                context.AllowUnknownFileTypes, context.AllowedFileTypeOverrides);
            EnforceEnterpriseRoots(snapshot, canonical, access);
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
