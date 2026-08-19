# SaaS Operator Best Practices & FAQ

This guide provides practical operational patterns, architectural blueprints, and best practices for managed service providers (MSPs), centralized IT teams, and SaaS operators hosting **ETL-SQL** for multiple small-to-medium enterprise (SME) clients.

---

## 1. The SaaS Mental Model

ETL-SQL allows an operator to provide complete data orchestration, pipeline execution, and interactive reporting to multiple distinct organizations without forcing every client to procure, configure, and maintain high-availability server clusters.

```
                     ┌─────────────────────────────────────────────────────────┐
                     │          Platform Admin Control Plane (/platform)       │
                     │  - Fleet metrics & capacity   - Tenant quota limits     │
                     │  - Provisioning & lifecycle    - Cryptographic receipts  │
                     └────────────────────────────┬────────────────────────────┘
                                                  │
                ┌─────────────────────────────────┴─────────────────────────────────┐
                ▼                                                                   ▼
┌───────────────────────────────┐                                   ┌───────────────────────────────┐
│       Tenant A (Acme Corp)    │                                   │      Tenant B (Beta Health)   │
│ - Isolated Catalog & Reports  │                                   │ - Isolated Catalog & Reports  │
│ - Dedicated / Shared Storage  │                                   │ - Dedicated / Shared Storage  │
│ - Quotas: 5 Jobs, 5GB Storage │                                   │ - Quotas: 2 Jobs, 1GB Storage │
│ - Outbound Gateway to On-Prem │                                   │ - Cloud-only DB Connectors    │
└───────────────────────────────┘                                   └───────────────────────────────┘
```

---

## 2. Choosing a Topology: Managed Dedicated vs. Shared Pool

When onboarding SME clients, ETL-SQL supports two primary hosting topologies:

| Feature / Dimension | Managed Dedicated SaaS | Shared Pool Multi-Tenancy |
| :--- | :--- | :--- |
| **Best For** | Regulated clients (HIPAA, SOC2, PCI), high-volume workloads, distinct SLA requirements. | Cost-effective tier, lightweight internal teams, trial/freemium tenants. |
| **Database Boundary** | Dedicated PostgreSQL database or schema per tenant. | Shared PostgreSQL database with server-enforced tenant ID isolation. |
| **Storage Boundary** | Dedicated storage root per tenant (`Smb`/UNC directory). | Shared artifact directory partitioned by tenant subpaths. |
| **Secrets & Keys** | Isolated Data Protection key rings or dedicated secret namespaces. | Platform Data Protection key ring with tenant-isolated secret resolution. |
| **Noisy Neighbor Risk** | Zero compute or connection pool contention. | Managed via per-tenant concurrency ceilings and memory throttles. |
| **Upgrade Cadence** | Per-tenant scheduled upgrade and maintenance windows (`saas-upgrade`). | Estate-wide zero-downtime rolling upgrades across Portal nodes. |

> [!TIP]
> **Operator Recommendation**: Start with **Managed Dedicated SaaS** for your first few enterprise clients. It gives you immediate compliance isolation with minimal operational complexity. Transition high-density, low-margin tenants into the **Shared Pool** as your fleet scales.

---

## 3. Day-1 Tenant Onboarding Checklist

### Step 1: Provision the Tenant Boundary
You can provision a tenant either through the Web UI or via the CLI:

- **Via Control Plane Web UI**: Navigate to `/platform` (requires `X-Portal-Platform-Key`), click **Provision Tenant**, specify the Tenant ID, Name, Tier (`Standard`, `Professional`, `Enterprise`), and initial resource quotas.
- **Via CLI**:
  ```bash
  etl-sql admin promotion saas-onboard \
    --tenant acme-corp \
    --tier standard \
    --db-conn "Host=pg.internal;Database=etlsql_acme;Username=etl_app;Password=SECRET:db-pwd" \
    --storage-root "\\storage\tenants\acme-corp"
  ```

### Step 2: Configure Initial Quotas
Prevent runaway execution by establishing baseline boundaries in `appsettings.json` or through the Control Plane modal:
```json
{
  "Portal": {
    "SharedTenancy": {
      "Enabled": true,
      "LifecycleManagementKey": "SECRET:platform-mgmt-key",
      "DefaultMaxConcurrentJobs": 2,
      "DefaultMaxStorageMb": 2048,
      "DefaultMaxReportSessions": 3
    }
  }
}
```

### Step 3: Configure Authentication
- **OIDC Federated Login**: Configure the tenant's IdP (Entra ID, Okta, Google Workspace) in `Portal:Oidc` for automated user provisioning and group claim synchronization.
- **Local RBAC**: Create the initial tenant administrator account in **Admin → Users** and assign the `Admin` role.

---

## 4. The Zero-Inbound Gateway Blueprint

### The Ingress Dilemma
Your SME clients frequently have SQL Server, Oracle, PostgreSQL, or network file shares behind corporate firewalls. They **cannot and should not** open inbound firewall ports to your SaaS platform.

### The Reverse-Tunnel Solution
ETL-SQL solves this via the **Secure Outbound Gateway**:

```
┌─────────────────────────────────┐                 ┌─────────────────────────────────┐
│     Client Private Network      │                 │       Your SaaS Cloud Fleet     │
│                                 │                 │                                 │
│  ┌───────────────────────────┐  │  Outbound TLS   │  ┌───────────────────────────┐  │
│  │ Local MSSQL / File Share  │  │  (Port 443)     │  │ ETL-SQL Portal Instance   │  │
│  └─────────────▲─────────────┘  │ ──────────────> │  │ (Receives Reverse Tunnel) │  │
│                │                │                 │  └─────────────▲─────────────┘  │
│  ┌─────────────┴─────────────┐  │                 │                │                │
│  │ ETL-SQL Gateway Agent     │  │                 │  ┌─────────────┴─────────────┐  │
│  └───────────────────────────┘  │                 │  │ Tenant Report Execution   │  │
└─────────────────────────────────┘                 └─────────────────────────────────┘
```

### Operator Workflow:
1. **Issue Enrollment Token**:
   - In **Admin → Gateways**, click **Enroll Gateway** (or call `POST /api/admin/gateways/enroll`).
   - The Portal generates a single-use enrollment token with an expiration window.
2. **Hand Off Agent to Client**:
   - Provide the client IT team with the lightweight Gateway binary and enrollment token:
```bash
etl-sql-gateway --portal-url https://portal.yourdomain.com --enrollment-token <TOKEN>
```
3. **Execute Pushdown Queries**:
   - Tenant scripts reference the registered Gateway resource transparently through its shared connection alias:
```sql
CREATE CONNECTION onprem_db AS MSSQL('SHARED:onprem_erp');
SELECT * INTO #staged_sales FROM onprem_db.dbo.Orders WHERE OrderDate >= DATEADD(DAY, -1, GETDATE());
```

---

## 5. Noisy Neighbor Mitigation & Resource Controls

In a shared SaaS environment, one tenant executing a runaway script must never impact other tenants' queries or reports.

### 1. Concurrency Ceilings
Limit how many background pipeline tasks and interactive sessions a single tenant can run concurrently:
```http
POST /api/platform/control-plane/tenants/acme-corp/quotas
Content-Type: application/json
X-Portal-Platform-Key: <PLATFORM_MANAGEMENT_KEY>

{
  "maxConcurrentJobs": 3,
  "maxStorageMb": 5120,
  "maxReportSessions": 5
}
```

### 2. Memory & Query Caps
Set memory boundaries on execution sandboxes so unindexed operations fail gracefully inside their own process rather than crashing the server:
- **Environment Limit**: Set `DOTNET_GCHeapHardLimit` on worker containers (e.g. `8GB`).
- **Engine Query Timeout**: Set default command execution timeouts in `Engine:ExecutionTimeoutSeconds=300`.
- **Row Limits**: Staged tables in `#temp` tables throw actionable out-of-memory errors before exhausting host resources.

---

## 6. Customer SaaS Exit & Tenant Portability

Enterprise and SME customers frequently require reassurance that their data, pipeline definitions, and scheduled reports are not locked into your platform.

### Proving Portability:
ETL-SQL provides native tenant export/import tooling that packages the entire tenant state into a signed, portable zip bundle:

```bash
# Operator exports full tenant bundle
etl-sql admin tenant export \
  --tenant acme-corp \
  --output-dir /backups/exports \
  --sign-key SECRET:tenant-signing-key
```

### What Is Included in the Export:
- All `.etlsql` and `.rptsql` script definitions and version histories.
- Orchestrator job definitions, schedules, and dependencies.
- Folder hierarchies and access control lists (ACLs).
- Lineage, metadata tags, and governance audit records.
- Stored report snapshots and visual layouts.

### Importing to Client On-Premises:
If the client graduates from SaaS to an on-premises Enterprise instance:
```bash
# Preflight validation against local server
etl-sql admin tenant preflight --package acme-corp-bundle.zip

# Import and rebind connection aliases to internal servers
etl-sql admin tenant import \
  --package acme-corp-bundle.zip \
  --rebind "SHARED:analytics_db=MSSQL(SERVER='sql01.internal',DATABASE='Analytics')"
```

---

## 7. Disaster Recovery & Split-Custody Key Management

### The Secret Store Risk
The Portal encrypts all database passwords and credentials stored in `Admin → Secrets` using ASP.NET Core Data Protection key rings. **If you back up the database but lose the key ring, all stored secrets are permanently unrecoverable.**

### Split-Custody Backup Protocol:
Always execute platform backups with split custody:
```bash
etl-sql admin backup --output-dir /secure/backups
```
This produces two distinct, timestamped archives:
1. `etlsql-data-<timestamp>.tar.gz`: The database state and catalog metadata.
2. `etlsql-keys-<timestamp>.tar.gz`: The Data Protection encryption key ring.

Store these two archives in separate access-controlled locations (e.g., separate S3 buckets with distinct IAM policies or cloud HSM boundaries).

### Testing Backup Health:
Run non-secret validation probes regularly:
```bash
# Verify that all stored secrets can still be decrypted by the current key ring
POST /api/admin/secrets/verify-all
```

---

## 8. Publishing Governance: Portal Script Editor Only

If you wish to enforce that tenant users **only** author scripts within the web Portal and cannot deploy unreviewed code from local tools:

1. **Enable Draft Approval**:
   ```json
   {
     "Portal": {
       "Studio": {
         "RequireApprovalToPublish": true
       }
     }
   }
   ```
   When enabled:
   - Authors draft, test, and preview `.etlsql` and `.rptsql` scripts in the browser IDE.
   - The script cannot be published to production without an explicit review and approval by an admin or designated reviewer.
   - **Authors cannot approve their own drafts.**

2. **Restrict Publishing Tokens**:
   - Do not grant `OrchestratorAdmin` or machine management keys to standard users.
   - Keep API tokens scoped strictly to viewer/editor roles.

3. **Secure Credential Handoff**:
   - Admin creates `SECRET:client-db-pwd` in **Admin → Secrets**.
   - Admin creates `SHARED:client_dw` in **Admin → Shared Connections** using that secret.
   - Users simply reference `CREATE CONNECTION dw AS SHARED:client_dw;`. The user never sees the password, and unauthorized users are blocked at query runtime.

---

## 9. Observability & User Coaching Playbook

How to identify who is thriving, who is inactive, and who needs help:

```
┌────────────────────────────────────────────────────────────────────────────────────────┐
│                              Operator Telemetry Strategy                               │
├────────────────────────────┬────────────────────────────┬──────────────────────────────┤
│ Metric / Signal            │ Data Source                │ Actionable Intervention      │
├────────────────────────────┼────────────────────────────┼──────────────────────────────┤
│ Zero activity in 30+ days  │ Admin → Users              │ Outreach: Offer onboarding   │
│                            │ (LastLoginUtc)             │ or check if pipeline is idle │
├────────────────────────────┼────────────────────────────┼──────────────────────────────┤
│ High execution errors      │ Admin → Audit              │ Coaching: Review script syntax│
│ (Syntax / Schema failures) │ (/api/admin/audit)         │ or data quality rules        │
├────────────────────────────┼────────────────────────────┼──────────────────────────────┤
│ Approaching storage quota  │ Control Plane (/platform)  │ Upsell: Increase quota tier  │
│                            │ (StorageUsedMb)            │ or configure auto-purge      │
├────────────────────────────┼────────────────────────────┼──────────────────────────────┤
│ Heavy report viewers       │ Admin → Usage Metrics      │ Optimization: Cache datasets │
│ (Slow render durations)    │ (/api/admin/metrics/usage) │ with scheduled snapshots     │
└────────────────────────────┴────────────────────────────┴──────────────────────────────┘
```

---

## 10. Frequently Asked Questions (FAQ)

### Q: Can tenant scripts access the host operating system or other tenants' files?
**No.** ETL-SQL enforces strict execution sandboxing:
- **Path Boundaries**: File operations are restricted to the tenant's designated storage root.
- **System Directory Blocking**: Direct access to `C:\Windows`, `/etc`, `/root`, `.git`, or raw drive roots (`C:\`) is permanently rejected.
- **Script Immutability**: Scripts cannot modify or overwrite executable `.etlsql` or `.rptsql` files.

### Q: How do we handle rolling upgrades without dropping active report sessions?
1. Deploy Portal nodes behind a load balancer with sticky sessions configured on cookie `ETLSQL_PORTAL_AFFINITY`.
2. Drain Node 1 from the load balancer pool.
3. Update Node 1 binaries and restart. Node 1 acquires the PostgreSQL advisory migration lock, applies schema updates, and becomes healthy (`GET /healthz`).
4. Re-add Node 1 to the pool and repeat for Node 2.

### Q: Can a tenant query multiple databases in a single script?
**Yes.** ETL-SQL's core differentiator is hybrid orchestration. A single tenant script can pull staged data from an on-premise SQL Server via Gateway, combine it with a Snowflake cloud warehouse table in an in-memory `#temp` table, and write the validated result to a local Excel file or Postgres database.

### Q: What happens if a tenant exceeds their storage quota?
When `StorageUsedMb` reaches `MaxStorageMb`:
- Existing reports remain viewable and readable.
- New snapshot generations, report exports, and file uploads are rejected with HTTP 429 / Quota Exceeded until old snapshots are purged or the operator increases the quota in the Control Plane.

---

## Related Documentation
- [Deployment Profile Architecture](file:///C:/Users/chuck/scratch/ETL-SQL/docs/architecture/DeploymentProfiles.md)
- [SaaS Tenant Isolation Specification](file:///C:/Users/chuck/scratch/ETL-SQL/docs/architecture/SaaSTenantIsolation.md)
- [Tenant Portability & Exit](file:///C:/Users/chuck/scratch/ETL-SQL/docs/architecture/TenantPortability.md)
- [Platform Configuration Settings Reference](appsettings-reference.md)
- [Secure Outbound Gateway Guide](secure-outbound-gateway.md)
- [Backup, Monitoring, and Health Guide](backup-and-monitoring.md)
