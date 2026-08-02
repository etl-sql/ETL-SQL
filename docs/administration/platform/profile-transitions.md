# Deployment profile transitions

ETL-SQL promotions preserve source-controlled pipeline and report logic while changing target
bindings, persistence, identity, and operational controls. Always retain the preflight, package,
validation report, backup identifiers, and post-cutover counts as one promotion evidence set.

## Solo to Team

This transition moves a workstation workload into a shared, single-node Orchestrator and optional
Portal. SQLite remains a supported Team store; PostgreSQL and OIDC are not prerequisites.

1. Stop local automation that can schedule or mutate the source catalog. Interactive authoring may
   continue only after the repository is frozen at the promotion commit.
2. Run `admin promotion preflight --from-profile Solo --to-profile Team`. Resolve raw credentials
   and every blocking finding.
3. Run `admin backup` for any local Portal/Orchestrator state. Keep the data and key archives in
   separate custody.
4. Export the local Orchestrator package with `admin promotion export`. If Portal is in use, run
   `EXPORT PORTAL CONFIGURATION` and collect its report-script manifest.
5. Copy the hashed `.etlsql`, `.rptsql`, and policy artifacts from the preflight inventory to the
   Team script root. Provision every required `SECRET:name` in the Team secret provider.
6. Validate the Orchestrator package against the Team store with all path and connection bindings.
   Replay the Portal bootstrap against the Team Portal after copying its report scripts.
7. Import the Orchestrator package. Leave imported jobs disabled until the target binding checks and
   Portal report validation are green; then enable schedules on the Team Orchestrator only.
8. Prove artifact hashes, job/schedule/link counts, ownership, a representative quality-gated run,
   lineage/tag counts, report execution, and notification delivery. Re-import the same package and
   confirm no duplicates or changed logical state.
9. Roll back by disabling Team schedules and restoring the source backup/key pair, or by returning
   automation to the unchanged Solo store. Do not run both schedulers concurrently.

## Team to Enterprise

This transition retains the same artifacts and catalogs while adding PostgreSQL/shared storage,
central identity and policy, durable audit delivery, and optionally HA.

1. Fence Team scheduling and publishing, then collect `admin backup`, Portal configuration export,
   Orchestrator promotion export, and preflight evidence with `Team` as the source and `Enterprise`
   as the target.
2. Provision PostgreSQL schemas, shared Portal artifact roots, a shared Data Protection key ring,
   identical service keys, the enterprise secret provider, OIDC, organization policy authority,
   audit collector, and load-balancer affinity. Do not copy private keys or resolved secrets from
   the Team host.
3. Run `admin migrate-database --dry-run`, then the live SQLite-to-PostgreSQL migration while all
   source writers remain stopped. Alternatively, for a clean target, validate/import the
   Orchestrator package and replay the Portal bootstrap with explicit identity and binding maps.
4. Start one target node, verify `/healthz`, validate effective policy and identity mappings, and
   reconcile artifact/catalog/quality/lineage counts. Add nodes only after the single-node proof is
   green; verify node heartbeats, lease fencing, shared artifact access, and sticky report sessions.
5. Enable Enterprise schedules after confirming no Team scheduler owns a live lease. Run one
   representative pipeline, quality failure route, notification, Portal publication, and audit
   delivery before directing normal traffic to the target.
6. Roll back before new target writes by returning traffic and scheduling to Team. After target
   writes begin, fence Enterprise and restore the coordinated pre-cutover database, artifact, and
   key-ring recovery set; there is no supported PostgreSQL-to-SQLite down-migration.

## In-place N to N+1

All currently implemented profiles use forward-only schema migration. Back up state, artifacts, and
keys; fence schedulers; run the release's N-to-N+1 drill; upgrade one node first where topology
allows; verify health, compatibility metadata, catalog/quality/lineage continuity, and representative
execution; then roll forward remaining nodes. Rollback is restore-from-backup, never a database
down-migration. A profile is not certified for a release until its current transition lane produces
commit-bound evidence.

Run the implementation drill with:

```powershell
.\scripts\Test-DeploymentProfileCertification.ps1 -Transition Upgrade
```

The lane has named Solo, Team, Enterprise, and SaaS lifecycle cases. Each creates a versioned
logical restore point, fences the scheduler, opens the state with N+1 components, verifies artifact
hash plus catalog/job/quality/lineage continuity, and restores into a separate scheduler-fenced
release-N boundary. Portal and Orchestrator schema drills additionally migrate populated N state to
HEAD, check rolling migration convergence, and exercise coordinated backup/restore. Solo's host
state is source artifacts plus optional local SQLite state; Team adds its durable scheduler store;
Enterprise adds Portal migrations, shared-state fencing, and coordinated recovery; SaaS repeats the
drill inside a host-fixed tenant boundary and composes tenant-isolation proof.

The four promotion lanes use the same executable lifecycle contract:

```powershell
.\scripts\Test-DeploymentProfileCertification.ps1 -Transition All
```

For Solo → Team, Team → Enterprise, Enterprise → SaaS, and Solo → SaaS, the runner executes a
profile-specific backup/export, scheduler fence, cutover continuity check, and import into a
separate scheduler-safe rollback store before the transition-specific database, Portal, or SaaS
proof. A generated certification report is evidence of the drill; it is not itself a production
backup and never replaces the split-custody archives described in the backup guide.

## Enterprise or Solo to SaaS

SaaS onboarding creates one tenant per runtime boundary; callers cannot choose a tenant id at request
time. The deployment plane fixes tenant authority in generated host configuration and gives each
tenant distinct databases, artifact/report roots, key directories, caches, queues/outboxes, logs,
telemetry namespace, support workspace, secret-provider namespace, and resource settings.

```powershell
etl-sql admin promotion saas-onboard `
  --tenant customer-a `
  --source-profile Enterprise `
  --source C:\exports\customer-a `
  --package C:\exports\customer-a\orchestrator-promotion.json `
  --portal-bootstrap C:\exports\customer-a\portal-bootstrap.etlsql `
  --output-root D:\saas-tenants `
  --bind 'SHARED:corp-mail=SHARED:tenant-mail' `
  --max-concurrent-jobs 4 `
  --max-storage-mb 10240 `
  --max-report-sessions 20
```

The same command accepts `--source-profile Solo` for direct onboarding. It runs the SaaS preflight,
copies only hashed portable artifacts, imports eligible Orchestrator history into the tenant's own
database, stages a secret-free Portal bootstrap, creates empty tenant-owned key roots, and writes an
`etl-sql.saas-tenant-boundary/v1` manifest. Imported jobs are disabled, support access is disabled,
and `activated` is false. The command fails if the tenant already exists and never overwrites an
existing boundary.

Every staged write is resolved beneath the host-fixed tenant root; rooted paths and traversal are
rejected. Before the boundary is made visible, onboarding totals every staged file and fails if the
declared `--max-storage-mb` quota is exceeded. The SaaS certification lane uses two independent
tenant runtimes to prove negative cross-reads for Orchestrator lineage and quality history,
schema-only PII results, security-event queues, Portal audit/outbox rows, runtime security caches,
and cache paths.

Before activation, provision the tenant's secret namespace and identity authority, replay and
validate the Portal bootstrap inside that tenant runtime, enforce ongoing storage consumption in the
hosting layer, run the retained negative cross-tenant certification, and collect post-cutover proof. Rollback
before activation is removal of the newly staged boundary; after activation, fence the tenant
scheduler and restore that tenant's own coordinated database/artifact/key recovery set. Never restore
one tenant over another or copy platform/other-tenant secrets into the boundary.

## References

- [Deployment promotion](deployment-promotion.md)
- [Operator CLI](operator-cli.md)
- [Deployment Profile Standard](../../architecture/standards/Deployment_Profile_Standards.md)
- [Portal administration](../portal/README.md)
