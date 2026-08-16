using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Orchestrator.Storage;

namespace ETL_SQL.Tests.Multitenancy;

/// <summary>
/// The orchestrator's per-object grant store, answered against the cross-tenant negative contract
/// every shared surface must satisfy.
///
/// <para>Grants are the surface where a tenant boundary failing is a disclosure rather than an
/// inconvenience: a grant read across the boundary is another tenant's authorization decision applied
/// to your caller. Before this, the boundary was argued from the schema — the ACL table keys on a
/// surrogate object id, and resolving a name to one requires a tenant — which is true but is exactly
/// the kind of reasoning a reviewer accepts and a regression walks straight through. Inheriting the
/// contract answers the six cases, including the <c>acme</c>/<c>acme-evil</c> prefix trap, by
/// execution instead.</para>
/// </summary>
public sealed class OrchestratorObjectAclSharedSurfaceTests : SharedTenantSurfaceContractTests, IDisposable
{
    private readonly List<string> _paths = [];

    protected override ISharedTenantSurface CreateSurface()
    {
        var path = Path.Combine(Path.GetTempPath(), $"etlsql-acl-surface-{Guid.NewGuid():N}.db");
        _paths.Add(path);
        return new OrchestratorObjectAclSurface(new SQLiteJobHistoryStore(path));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var path in _paths)
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                try { if (File.Exists(path + suffix)) File.Delete(path + suffix); } catch (IOException) { }
            }
        }
    }

    /// <summary>
    /// Presents the grant store as a shared surface: a logical id is a job name, and the value written
    /// under it is the principal the job is granted to. Nothing here does any scoping of its own — the
    /// point is to route each access through the store's own tenant-qualified resolution and let the
    /// contract judge the result.
    /// </summary>
    private sealed class OrchestratorObjectAclSurface(SQLiteJobHistoryStore store) : ISharedTenantSurface
    {
        public async Task WriteAsync(TenantContext context, string logicalId, string value)
        {
            var tenant = context.Tenant.Value;
            await store.SaveJobAsync(new JobDefinition(
                logicalId, "SELECT 1;", 1, "HOUR", null, null, null, TenantId: tenant));

            var objectId = await store.ResolveObjectIdAsync(tenant, OrchestratorObjectKind.Job, logicalId)
                ?? throw new InvalidOperationException($"'{logicalId}' did not resolve in '{tenant}' after being saved there.");

            // One grant per object, replaced on rewrite, so the contract's "read back what this tenant
            // wrote" cases have a single unambiguous answer.
            await store.DeleteObjectGrantsAsync(objectId);
            await store.SaveObjectGrantAsync(new OrchestratorObjectGrant(
                objectId,
                OrchestratorObjectKind.Job,
                OrchestratorPrincipalKind.Group,
                value,
                OrchestratorObjectPermission.Read,
                "contract-test"));
        }

        public async Task<string?> ReadAsync(TenantContext context, string callerSuppliedId)
        {
            // The identifier is untrusted, so whatever tenant it claims is discarded and only the
            // logical part survives. Resolution then happens in the caller's own verified tenant,
            // which is the single place a name becomes an identity — and the reason knowing another
            // tenant's identifier buys nothing.
            var objectId = await store.ResolveObjectIdAsync(
                context.Tenant.Value, OrchestratorObjectKind.Job, LogicalOf(context, callerSuppliedId));
            if (objectId is null) return null;

            var grants = await store.GetObjectGrantsAsync(objectId);
            return grants.Count == 0 ? null : grants[0].PrincipalId;
        }

        public async Task<IReadOnlyList<string>> ListAsync(TenantContext context)
        {
            var tenant = context.Tenant.Value;
            var visible = new List<string>();
            foreach (var job in (await store.GetAllJobsAsync())
                .Where(j => string.Equals(j.TenantId, tenant, StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var grant in await store.GetObjectGrantsAsync(job.Id.Value))
                    visible.Add(grant.PrincipalId);
            }
            return visible;
        }

        /// <summary>
        /// Strips any tenant the caller claimed in the identifier. A caller-supplied identifier arrives
        /// either scoped (<c>tenant/logical</c>) or bare; in both cases the claim is not authority, so
        /// only the logical part is carried forward.
        /// </summary>
        private static string LogicalOf(TenantContext context, string callerSuppliedId)
        {
            if (callerSuppliedId.StartsWith(context.ScopePrefix, StringComparison.Ordinal))
                return callerSuppliedId[context.ScopePrefix.Length..];

            var separator = callerSuppliedId.IndexOf('/');
            return separator < 0 ? callerSuppliedId : callerSuppliedId[(separator + 1)..];
        }
    }
}
