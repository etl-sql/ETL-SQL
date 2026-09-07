using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ETL_SQL.Orchestrator.Execution;

/// <summary>
/// Server-owned identity for one sandbox attempt. A scheduler creates this after authenticating the
/// tenant and fencing the attempt; workload input must never be used to construct it.
/// </summary>
public sealed record SandboxAssignmentIdentity
{
    public SandboxAssignmentIdentity(TenantContext tenant, string runId, string attemptId)
    {
        Tenant = tenant ?? throw new ArgumentNullException(nameof(tenant));
        RunId = RequirePathSegment(runId, nameof(runId));
        AttemptId = RequirePathSegment(attemptId, nameof(attemptId));
    }

    public TenantContext Tenant { get; }
    public string RunId { get; }
    public string AttemptId { get; }
    public string? AdmissionId { get; init; }

    private static string RequirePathSegment(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value is "." or ".." || value.Length > 128 ||
            value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            value.Contains('/') || value.Contains('\\'))
        {
            throw new ArgumentException(
                "Sandbox run and attempt identifiers must be single canonical path segments.",
                parameterName);
        }

        return value;
    }
}

/// <summary>
/// A single-use writable workspace which a hardened execution provider may mount into exactly one
/// sandbox. Disposing the assignment destructively removes its writable state.
/// </summary>
public sealed class SandboxWorkspaceAssignment : IAsyncDisposable
{
    private readonly Func<SandboxWorkspaceAssignment, CancellationToken, ValueTask> _destroy;
    private readonly SemaphoreSlim _destroyGate = new(1, 1);
    private bool _destroyed;

    internal SandboxWorkspaceAssignment(
        SandboxAssignmentIdentity identity,
        string assignmentId,
        string rootPath,
        string ownershipToken,
        Func<SandboxWorkspaceAssignment, CancellationToken, ValueTask> destroy)
    {
        Identity = identity;
        AssignmentId = assignmentId;
        RootPath = rootPath;
        OwnershipToken = ownershipToken;
        _destroy = destroy;
    }

    public SandboxAssignmentIdentity Identity { get; }
    public string AssignmentId { get; }
    public string RootPath { get; }
    public string InputPath => Path.Combine(RootPath, "input");
    public string ScratchPath => Path.Combine(RootPath, "scratch");
    public string OutputPath => Path.Combine(RootPath, "output");
    internal string OwnershipToken { get; }

    public async ValueTask DestroyAsync(CancellationToken cancellationToken = default)
    {
        await _destroyGate.WaitAsync(cancellationToken);
        try
        {
            if (_destroyed)
                return;

            await _destroy(this, cancellationToken);
            _destroyed = true;
        }
        finally
        {
            _destroyGate.Release();
        }
    }

    public ValueTask DisposeAsync() => DestroyAsync();
}

public interface ISandboxWorkspaceProvider
{
    ValueTask<SandboxWorkspaceAssignment> AssignAsync(
        SandboxAssignmentIdentity identity,
        CancellationToken cancellationToken = default);
}

public interface ISandboxWorkspaceRecovery
{
    /// <summary>Called only after the runtime has positively proved all assignment mounts detached.</summary>
    Task<bool> CleanupDetachedAsync(SandboxAdmissionLedgerEntry admission, CancellationToken cancellationToken);
}

public sealed class FileSystemSandboxWorkspaceOptions
{
    /// <summary>Absolute host-owned root containing ephemeral sandbox assignments.</summary>
    public required string RootPath { get; init; }
}

/// <summary>
/// Allocates fresh writable roots for provider-neutral sandbox assignments. This component owns
/// writable-state lifecycle; an OCI, microVM, Hyper-V, or equivalent provider remains responsible
/// for enforcing the hardened compute boundary and mounting only the returned paths.
/// </summary>
public sealed class FileSystemSandboxWorkspaceProvider : ISandboxWorkspaceProvider, ISandboxWorkspaceRecovery
{
    private const string MarkerName = ".etlsql-assignment.json";
    private readonly string _rootPath;
    private readonly string _recoveryPath;
    private readonly ILogger<FileSystemSandboxWorkspaceProvider> _logger;
    private readonly ConcurrentDictionary<string, string> _active = new(StringComparer.Ordinal);

    public FileSystemSandboxWorkspaceProvider(FileSystemSandboxWorkspaceOptions options,
        ILogger<FileSystemSandboxWorkspaceProvider>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.RootPath);
        if (!Path.IsPathFullyQualified(options.RootPath))
            throw new ArgumentException("The sandbox workspace root must be an absolute path.", nameof(options));

        _rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.RootPath));
        Directory.CreateDirectory(_rootPath);
        SandboxFilePermissions.RestrictToOwner(_rootPath);
        _recoveryPath = Path.Combine(_rootPath, ".recovery");
        _logger = logger ?? NullLogger<FileSystemSandboxWorkspaceProvider>.Instance;
    }

    public async ValueTask<SandboxWorkspaceAssignment> AssignAsync(
        SandboxAssignmentIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        cancellationToken.ThrowIfCancellationRequested();

        var assignmentId = Guid.NewGuid().ToString("N");
        var ownershipToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var assignmentRoot = Path.GetFullPath(Path.Combine(
            _rootPath,
            identity.Tenant.Tenant.Value,
            identity.RunId,
            identity.AttemptId,
            assignmentId));
        RequireContained(assignmentRoot);

        if (!_active.TryAdd(assignmentId, assignmentRoot))
            throw new InvalidOperationException("A fresh sandbox assignment identifier could not be allocated.");

        var markerWritten = false;
        try
        {
            if (Directory.Exists(assignmentRoot) || File.Exists(assignmentRoot))
                throw new IOException("A fresh sandbox assignment unexpectedly already exists.");

            var ownership = new AssignmentMarker(
                assignmentId,
                identity.Tenant.Tenant.Value,
                identity.RunId,
                identity.AttemptId,
                ownershipToken,
                identity.AdmissionId);
            // Persist outside mounted leaves before creating any tenant workspace content.
            if (identity.AdmissionId != null)
                await WriteRecoveryAsync(ownership, cancellationToken);
            Directory.CreateDirectory(assignmentRoot);
            var marker = JsonSerializer.Serialize(ownership);
            await using var stream = new FileStream(
                Path.Combine(assignmentRoot, MarkerName),
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            await stream.WriteAsync(Encoding.UTF8.GetBytes(marker), cancellationToken);
            await stream.FlushAsync(cancellationToken);
            markerWritten = true;

            SandboxFilePermissions.RestrictToOwner(assignmentRoot);
            Directory.CreateDirectory(Path.Combine(assignmentRoot, "input"));
            // A sandbox deliberately runs as an unprivileged uid unrelated to this process's, so the
            // writable leaves must be writable by it or the workload cannot produce output at all.
            // Only the leaves are opened up: every enclosing directory stays owner-only, and a runtime
            // mounts the leaf directly instead of walking the path, so no other host user can reach it.
            SandboxFilePermissions.AllowUnprivilegedSandboxWrites(Directory.CreateDirectory(Path.Combine(assignmentRoot, "scratch")).FullName);
            SandboxFilePermissions.AllowUnprivilegedSandboxWrites(Directory.CreateDirectory(Path.Combine(assignmentRoot, "output")).FullName);

            return new SandboxWorkspaceAssignment(
                identity, assignmentId, assignmentRoot, ownershipToken, DestroyAsync);
        }
        catch
        {
            _active.TryRemove(assignmentId, out _);
            if (markerWritten)
                await TryDeleteOwnedIncompleteAssignmentAsync(
                    assignmentRoot, assignmentId, ownershipToken, CancellationToken.None);
            throw;
        }
    }

    private async ValueTask DestroyAsync(
        SandboxWorkspaceAssignment assignment,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireContained(assignment.RootPath);

        if (!_active.TryGetValue(assignment.AssignmentId, out var registeredRoot) ||
            !PathEquals(registeredRoot, assignment.RootPath))
        {
            throw new UnauthorizedAccessException("The sandbox assignment is not active at this provider.");
        }

        var markerPath = Path.Combine(assignment.RootPath, MarkerName);
        AssignmentMarker? marker;
        try
        {
            await using var stream = new FileStream(
                markerPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            marker = await JsonSerializer.DeserializeAsync<AssignmentMarker>(stream, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            throw new UnauthorizedAccessException(
                "Sandbox teardown refused because its ownership marker could not be verified.", ex);
        }

        if (marker is null || marker.AssignmentId != assignment.AssignmentId ||
            marker.TenantId != assignment.Identity.Tenant.Tenant.Value ||
            marker.RunId != assignment.Identity.RunId || marker.AttemptId != assignment.Identity.AttemptId ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(marker.OwnershipToken),
                Encoding.UTF8.GetBytes(assignment.OwnershipToken)))
        {
            throw new UnauthorizedAccessException(
                "Sandbox teardown refused because its ownership marker does not match the active assignment.");
        }

        DeleteTreeWithoutFollowingLinks(new DirectoryInfo(assignment.RootPath));
        if (Directory.Exists(assignment.RootPath) || File.Exists(assignment.RootPath))
            throw new IOException("Sandbox assignment teardown left writable state behind.");

        _active.TryRemove(assignment.AssignmentId, out _);
        if (assignment.Identity.AdmissionId != null)
            File.Delete(RecoveryFile(assignment.Identity.AdmissionId));
    }

    private string RecoveryFile(string admissionId) => Path.Combine(_recoveryPath,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(admissionId))) + ".json");

    private async Task WriteRecoveryAsync(AssignmentMarker marker, CancellationToken token)
    {
        RequireContained(_recoveryPath);
        Directory.CreateDirectory(_recoveryPath);
        SandboxFilePermissions.RestrictToOwner(_recoveryPath);
        var path = RecoveryFile(marker.AdmissionId!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, marker, cancellationToken: token);
                await stream.FlushAsync(token);
            }
            File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public async Task<bool> CleanupDetachedAsync(SandboxAdmissionLedgerEntry admission, CancellationToken cancellationToken)
    {
        var recoveryFile = RecoveryFile(admission.AdmissionId);
        if (!File.Exists(recoveryFile))
        {
            // A different machine cannot acknowledge cleanup of this node's local disk.
            var localOwner = (admission.ClaimedByNode ?? admission.LeaseOwner)?.StartsWith(Environment.MachineName + ":", StringComparison.OrdinalIgnoreCase) == true;
            if (!localOwner)
                _logger.LogWarning("Sandbox workspace cleanup needs its owning node: admission={Admission} tenant={Tenant} node={Node}",
                    admission.AdmissionId, admission.TenantId, admission.ClaimedByNode ?? admission.LeaseOwner);
            return localOwner;
        }
        AssignmentMarker? recovery = null;
        try
        {
            RequireContained(recoveryFile);
            recovery = JsonSerializer.Deserialize<AssignmentMarker>(await File.ReadAllTextAsync(recoveryFile, cancellationToken));
            if (recovery == null || recovery.AdmissionId != admission.AdmissionId || recovery.TenantId != admission.TenantId
                || !Guid.TryParseExact(recovery.AssignmentId, "N", out _))
                throw new UnauthorizedAccessException("Workspace recovery identity does not match the retained admission.");
            var identity = new SandboxAssignmentIdentity(TenantContext.FromVerifiedCredential(recovery.TenantId), recovery.RunId, recovery.AttemptId);
            var root = Path.Combine(_rootPath, identity.Tenant.Tenant.Value, identity.RunId, identity.AttemptId, recovery.AssignmentId);
            RequireContained(root);
            if (Directory.Exists(root))
            {
                var markerPath = Path.Combine(root, MarkerName);
                RequireContained(markerPath);
                if (File.Exists(markerPath))
                {
                    var marker = JsonSerializer.Deserialize<AssignmentMarker>(await File.ReadAllTextAsync(markerPath, cancellationToken));
                    if (marker != recovery) throw new UnauthorizedAccessException("Workspace marker does not match server recovery metadata.");
                }
                // Missing marker is an allocation interrupted after recovery persistence but before mounting.
                DeleteTreeWithoutFollowingLinks(new DirectoryInfo(root));
            }
            if (File.Exists(root) || Directory.Exists(root)) throw new IOException("Workspace residue remains after cleanup.");
            File.Delete(recoveryFile);
            _active.TryRemove(recovery.AssignmentId, out _);
            _logger.LogInformation("Sandbox workspace recovered: admission={Admission} tenant={Tenant} run={Run} attempt={Attempt}",
                admission.AdmissionId, admission.TenantId, recovery.RunId, recovery.AttemptId);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sandbox workspace residue retained: admission={Admission} tenant={Tenant} run={Run} attempt={Attempt}",
                admission.AdmissionId, admission.TenantId, recovery?.RunId, recovery?.AttemptId);
            return false;
        }
    }

    private void RequireContained(string candidate)
    {
        var relative = Path.GetRelativePath(_rootPath, Path.GetFullPath(candidate));
        if (relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            Path.IsPathFullyQualified(relative))
        {
            throw new UnauthorizedAccessException("Sandbox assignment path escaped the configured workspace root.");
        }
        for (var path = Path.GetFullPath(candidate); path != null; path = Path.GetDirectoryName(path))
        {
            if ((Directory.Exists(path) || File.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Sandbox assignment ownership path contains a link.");
            if (PathEquals(path, _rootPath)) break;
        }
    }

    private static void DeleteTreeWithoutFollowingLinks(DirectoryInfo directory)
    {
        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                entry.Delete();
            }
            else if (entry is DirectoryInfo childDirectory)
            {
                DeleteTreeWithoutFollowingLinks(childDirectory);
            }
            else
            {
                entry.Attributes &= ~FileAttributes.ReadOnly;
                entry.Delete();
            }
        }

        directory.Attributes &= ~FileAttributes.ReadOnly;
        directory.Delete();
    }

    private static async ValueTask TryDeleteOwnedIncompleteAssignmentAsync(
        string path,
        string assignmentId,
        string ownershipToken,
        CancellationToken cancellationToken)
    {
        try
        {
            var markerPath = Path.Combine(path, MarkerName);
            if (!File.Exists(markerPath))
                return;

            var marker = JsonSerializer.Deserialize<AssignmentMarker>(
                await File.ReadAllTextAsync(markerPath, cancellationToken));
            if (marker?.AssignmentId == assignmentId && CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(marker.OwnershipToken),
                    Encoding.UTF8.GetBytes(ownershipToken)))
            {
                DeleteTreeWithoutFollowingLinks(new DirectoryInfo(path));
            }
        }
        catch
        {
            // Preserve the original allocation error. Admission-linked recovery metadata remains
            // available to the reconciler if this immediate cleanup could not remove the residue.
        }
    }

    private static bool PathEquals(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private sealed record AssignmentMarker(
        string AssignmentId,
        string TenantId,
        string RunId,
        string AttemptId,
        string OwnershipToken,
        string? AdmissionId = null);
}
