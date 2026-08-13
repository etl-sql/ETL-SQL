using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Multitenancy;

namespace ETL_SQL.App;

/// <summary>
/// Deployment-plane deletion for one physically isolated Managed Dedicated tenant boundary.
/// Authorization is a short-lived signed organization-policy grant; CLI tenant input is only a
/// mismatch assertion.
/// </summary>
internal static class SaasTenantDeletionService
{
    internal const string ReceiptSchema = "etl-sql.saas-tenant-deletion/v1";

    internal sealed record DeletionAuthority(
        TenantContext TenantContext,
        DateTimeOffset RetentionUntilUtc,
        bool LegalHoldCleared);

    internal sealed record DeletionReceipt(
        string SchemaVersion,
        string OperationId,
        string TenantId,
        string Status,
        string PlatformOperator,
        string AuthorizationReference,
        string Reason,
        DateTimeOffset AuthorizationExpiresUtc,
        DateTimeOffset RetentionUntilUtc,
        bool LegalHoldCleared,
        long FileCount,
        long TotalBytes,
        string BoundaryDigestSha256,
        DateTimeOffset StartedUtc,
        DateTimeOffset? CompletedUtc,
        bool TenantUserImpersonation);

    internal static async Task<int> RunAsync(
        CliContext context,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            var authority = ResolveAuthorizedContext(
                context, EnterprisePolicyRuntime.Current, now);
            var receipt = await DeleteAsync(
                context, authority, now, context.SaasDeletionExecute, cancellationToken);
            if (!context.SaasDeletionExecute)
            {
                logger.WriteLine(
                    $"Deletion preflight passed for {receipt.TenantId}: {receipt.FileCount} file(s), {receipt.TotalBytes} byte(s); add --execute to delete the boundary.",
                    ConsoleColor.Yellow);
                return 0;
            }

            logger.WriteLine(
                $"Tenant boundary {receipt.TenantId} deleted; completion record {receipt.OperationId} persisted.",
                ConsoleColor.Green);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or InvalidDataException or InvalidOperationException
                                   or ArgumentException or JsonException)
        {
            logger.WriteLine($"SaaS tenant deletion failed: {ex.Message}", ConsoleColor.Red);
            return 1;
        }
    }

    internal static DeletionAuthority ResolveAuthorizedContext(
        CliContext context,
        EffectiveEnterprisePolicy policy,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(policy);
        if (!policy.IsAvailable || policy.Document?.SaasDeletion.Enabled != true)
            throw new UnauthorizedAccessException(
                "SaaS deletion requires an active signed organization-policy authorization.");

        var authorization = policy.Document.SaasDeletion;
        if (!authorization.LegalHoldCleared)
            throw new UnauthorizedAccessException("SaaS deletion is blocked by legal-hold policy.");
        if (!authorization.RetentionUntilUtc.HasValue || now < authorization.RetentionUntilUtc.Value)
            throw new UnauthorizedAccessException(
                $"SaaS deletion is retained until {authorization.RetentionUntilUtc:O}.");

        var grant = PlatformAccessGrant.Issue(
            authorization.TenantId!, authorization.OperatorPrincipal!,
            authorization.AuthorizationReference!, authorization.Reason!,
            authorization.ExpiresUtc!.Value, now);
        var tenantContext = TenantContext.FromPlatformGrant(grant, now);
        tenantContext.RequireTenant(context.SaasTenantId);
        return new DeletionAuthority(
            tenantContext, authorization.RetentionUntilUtc.Value, authorization.LegalHoldCleared);
    }

    internal static async Task<DeletionReceipt> DeleteAsync(
        CliContext context,
        DeletionAuthority authority,
        DateTimeOffset now,
        bool execute = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authority);
        authority.TenantContext.RequireActivePlatformGrant(now);
        if (!authority.LegalHoldCleared || now < authority.RetentionUntilUtc)
            throw new UnauthorizedAccessException("Retention or legal-hold policy blocks tenant deletion.");
        if (string.IsNullOrWhiteSpace(context.SaasDeletionTenantRoot)
            || string.IsNullOrWhiteSpace(context.SaasDeletionReceiptRoot))
            throw new ArgumentException("--tenant-root and --receipt-root are required.");

        var tenant = authority.TenantContext.RequireTenant(context.SaasTenantId).Value;
        var tenantRoot = Path.GetFullPath(context.SaasDeletionTenantRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var receiptRoot = Path.GetFullPath(context.SaasDeletionReceiptRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        RejectFilesystemRoot(tenantRoot, "tenant boundary");
        RejectFilesystemRoot(receiptRoot, "receipt root");
        if (!Directory.Exists(tenantRoot))
            throw new DirectoryNotFoundException($"Tenant boundary not found: {tenantRoot}");
        EnsureNoReparseAncestors(tenantRoot);
        if (receiptRoot.StartsWith(tenantRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(receiptRoot, tenantRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The durable deletion receipt must be stored outside the tenant boundary.");

        await ValidateBoundaryIdentityAsync(tenantRoot, tenant, cancellationToken);
        var inventory = await InventoryAsync(tenantRoot, cancellationToken);
        if (Directory.Exists(receiptRoot))
        {
            EnsureNoReparseAncestors(receiptRoot);
            EnsureNotReparsePoint(receiptRoot);
        }
        else if (execute)
        {
            EnsureNoReparseAncestors(Path.GetDirectoryName(receiptRoot)!);
            Directory.CreateDirectory(receiptRoot);
            EnsureNotReparsePoint(receiptRoot);
        }

        var grant = authority.TenantContext.Grant!;
        var operationId = Guid.NewGuid().ToString("N");
        if (!execute)
        {
            return new DeletionReceipt(
                ReceiptSchema, operationId, tenant, "Preflight", grant.OperatorPrincipal,
                grant.AuthorizationReference, grant.Reason, grant.ExpiresUtc,
                authority.RetentionUntilUtc, authority.LegalHoldCleared,
                inventory.FileCount, inventory.TotalBytes, inventory.Digest,
                now, null, TenantUserImpersonation: false);
        }
        var receiptPath = Path.Combine(receiptRoot, $"tenant-deletion-{tenant}-{operationId}.json");
        var started = new DeletionReceipt(
            ReceiptSchema, operationId, tenant, "Started", grant.OperatorPrincipal,
            grant.AuthorizationReference, grant.Reason, grant.ExpiresUtc,
            authority.RetentionUntilUtc, authority.LegalHoldCleared,
            inventory.FileCount, inventory.TotalBytes, inventory.Digest,
            now, null, TenantUserImpersonation: false);
        await WriteReceiptAsync(receiptPath, started, createNew: true, cancellationToken);

        var tombstone = Path.Combine(
            Path.GetDirectoryName(tenantRoot)!, $".deleting-{tenant}-{operationId}");
        Directory.Move(tenantRoot, tombstone);
        try
        {
            DeleteTreeWithoutFollowingReparsePoints(tombstone);
            var completed = started with
            {
                Status = "Completed",
                CompletedUtc = DateTimeOffset.UtcNow
            };
            await WriteReceiptAsync(receiptPath, completed, createNew: false, cancellationToken);
            return completed;
        }
        catch
        {
            // The Started record and uniquely named tombstone are intentionally retained. Moving the
            // boundary back could re-authorize a partially deleted tenant; reconciliation is explicit.
            throw;
        }
    }

    private static async Task ValidateBoundaryIdentityAsync(
        string root,
        string expectedTenant,
        CancellationToken cancellationToken)
    {
        EnsureNotReparsePoint(root);
        var manifestPath = Path.Combine(root, "tenant-manifest.json");
        var configPath = Path.Combine(root, "config", "appsettings.tenant.json");
        if (!File.Exists(manifestPath) || !File.Exists(configPath))
            throw new InvalidDataException("The target is not a provisioned Managed Dedicated boundary.");
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath, cancellationToken))?.AsObject()
            ?? throw new InvalidDataException("Tenant manifest is unreadable.");
        var config = JsonNode.Parse(await File.ReadAllTextAsync(configPath, cancellationToken))?.AsObject()
            ?? throw new InvalidDataException("Tenant configuration is unreadable.");
        var manifestTenant = TenantId.FromTrustedSource((string?)manifest["tenantId"]).Value;
        var configTenant = TenantId.FromTrustedSource(
            (string?)config["saasTenant"]?["tenantId"]
            ?? (string?)config["SaasTenant"]?["TenantId"]).Value;
        if (manifestTenant != expectedTenant || configTenant != expectedTenant)
            throw new UnauthorizedAccessException(
                "Signed tenant authority does not match the boundary manifest and host configuration.");
    }

    private sealed record BoundaryInventory(long FileCount, long TotalBytes, string Digest);

    private static async Task<BoundaryInventory> InventoryAsync(
        string root,
        CancellationToken cancellationToken)
    {
        long count = 0;
        long bytes = 0;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var (files, _) = EnumerateTreeWithoutFollowingReparsePoints(root);
        foreach (var file in files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureNotReparsePoint(file);
            var info = new FileInfo(file);
            count++;
            bytes = checked(bytes + info.Length);
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            var metadata = Encoding.UTF8.GetBytes($"{relative}\0{info.Length}\0");
            digest.AppendData(metadata);
            await using var stream = new FileStream(
                file, FileMode.Open, FileAccess.Read, FileShare.Read,
                81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var fileHash = await SHA256.HashDataAsync(stream, cancellationToken);
            digest.AppendData(fileHash);
        }
        return new BoundaryInventory(count, bytes, Convert.ToHexStringLower(digest.GetHashAndReset()));
    }

    private static async Task WriteReceiptAsync(
        string path,
        DeletionReceipt receipt,
        bool createNew,
        CancellationToken cancellationToken)
    {
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        await using (var stream = new FileStream(
                         temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                         81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            await JsonSerializer.SerializeAsync(stream, receipt, JsonOptions, cancellationToken);
        if (createNew)
            File.Move(temporary, path);
        else
            File.Move(temporary, path, overwrite: true);
    }

    private static void DeleteTreeWithoutFollowingReparsePoints(string root)
    {
        var (files, directories) = EnumerateTreeWithoutFollowingReparsePoints(root);
        foreach (var file in files)
        {
            File.SetAttributes(file, FileAttributes.Normal);
            File.Delete(file);
        }
        foreach (var directory in directories.OrderByDescending(path => path.Length))
            Directory.Delete(directory);
        Directory.Delete(root);
    }

    private static (IReadOnlyList<string> Files, IReadOnlyList<string> Directories)
        EnumerateTreeWithoutFollowingReparsePoints(string root)
    {
        EnsureNotReparsePoint(root);
        var files = new List<string>();
        var directories = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                EnsureNotReparsePoint(entry);
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    directories.Add(entry);
                    pending.Push(entry);
                }
                else
                {
                    files.Add(entry);
                }
            }
        }
        return (files, directories);
    }

    private static void EnsureNotReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"Deletion refuses reparse point: {path}");
    }

    private static void EnsureNoReparseAncestors(string path)
    {
        var current = new DirectoryInfo(Path.GetFullPath(path));
        while (current is not null)
        {
            if (current.Exists)
                EnsureNotReparsePoint(current.FullName);
            current = current.Parent;
        }
    }

    private static void RejectFilesystemRoot(string path, string label)
    {
        var root = Path.GetPathRoot(path)?.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"A filesystem root cannot be used as the {label}.");
    }

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };
}
