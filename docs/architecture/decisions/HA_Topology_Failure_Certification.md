# HA Topology and Failure Certification

This guide defines the supported Portal and Orchestrator deployment topologies, the readiness
contract exposed to load balancers, and the failure scenarios that must be certified before an HA
environment is treated as production-ready.

## Supported Topologies

| Topology | State | Artifact storage | Traffic | Intended use |
| :--- | :--- | :--- | :--- | :--- |
| Standalone | SQLite on one host | Local directories on one host | Direct or single reverse proxy | Developer, single-user, small team installs |
| Departmental | SQLite on one managed host, or PostgreSQL for easier operations | Local or managed shared directory | One Portal endpoint, optional external Orchestrator | Team/shared service where maintenance windows are acceptable |
| High Availability | Shared PostgreSQL for Portal and Orchestrator | Shared SMB/UNC or equivalent mounted roots plus shared Data Protection key ring | Load-balanced Portal with sticky affinity; one or more Orchestrator nodes | Production operations that require node loss and rolling maintenance tolerance |

## Exact HA Requirements

Every HA Portal node must use:

- `Portal:Database:Provider=Postgres` with the same PostgreSQL Portal database.
- `Orchestrator:Database:Provider=Postgres` with the same PostgreSQL Orchestrator database used by
  Orchestrator nodes.
- Shared `Portal:ScriptRootPath`, `Portal:SnapshotDirectory`, `Portal:DatasetRootPath`, and
  `Portal:MapRootPath`.
- Shared `Portal:Storage:KeyRingPath` so cookies, protected credentials, dataset keys, and Portal
  secret-store values decrypt on every Portal node.
- Identical `Portal:Jwt:Secret`, accepted previous JWT secrets during rotation, dataset at-rest key,
  Orchestrator API key, and policy-authority signing certificate identity.
- `Portal:LoadBalancer:SessionAffinityEnabled=true`; load balancers must route on
  `Portal:LoadBalancer:SessionAffinityCookieName` because interactive report sessions are node-local.
- Service identities with explicit PostgreSQL, share, and filesystem permissions. Do not run Portal or
  Orchestrator as a broad local administrator account to compensate for missing ACLs.
- DNS names and certificates that match the public Portal VIP, Orchestrator API VIP, PostgreSQL
  endpoint, artifact storage endpoint, and policy-authority enrollment endpoint.
- Process supervision through Windows Services, systemd, Kubernetes, Docker Compose/Swarm, or an
  equivalent supervisor that restarts failed Portal and Orchestrator processes.

Recommended HA readiness configuration:

```json
{
  "Portal": {
    "Topology": {
      "ExpectedMode": "HighAvailability",
      "MinLivePortalNodes": 2,
      "MinLiveOrchestratorNodes": 1,
      "RequirePostgresForHa": true,
      "RequireSharedKeyRingForHa": true
    }
  }
}
```

Keep `MinLivePortalNodes` at `1` during bootstrap if the first node must become ready before the
remaining nodes are started. Raise it after the cluster is fully provisioned.

## Readiness and Health

Use `GET /healthz` as the load-balancer readiness probe. It returns HTTP 200 only when this node can
safely accept traffic for the configured topology. The response includes:

- `status` — `Healthy` or `Unhealthy`.
- `mode` — resolved topology mode: `Standalone`, `Departmental`, or `HighAvailability`.
- `checks.database` — Portal state database reachability.
- `checks.storage` — snapshot artifact root enumeration.
- `checks.lease` — node-registry/lease-store reachability.
- `checks.topology` — topology contract validation.
- `checks.portalNodes` and `checks.orchestratorNodes` — present for HA mode when minimum live-node
  thresholds are configured.
- `findings` — stable operator-facing finding codes such as `ha-requires-portal-postgres`,
  `ha-requires-shared-key-ring`, or `ha-live-portal-node-threshold-not-met`.

Use `GET /health` for richer operator diagnostics. It may show degraded non-routing conditions such as
policy-authority or Orchestrator issues; `/healthz` remains the traffic gate and fails closed for the
dependencies that make this node unsafe to route to.

## Responsibility Boundaries

ETL-SQL coordinates:

- Portal and Orchestrator schema migration serialization.
- Database-backed node heartbeats, leader leases, scheduled-work fencing, artifact write fencing, and
  duplicate-delivery suppression.
- Node-local readiness decisions through `/healthz`.
- Fleet status aggregation through the scoped read-only FleetReader endpoint.
- Operational alerts and Prometheus signals for queue, storage, policy, audit/security, database, and
  fleet-node health.

Operators and infrastructure must provide:

- PostgreSQL high availability, backups, WAL archiving, point-in-time recovery, monitoring, and
  connection-pool sizing.
- Load-balancer routing, sticky affinity, TLS termination or pass-through, and external dead-man
  probing of each node.
- Shared storage availability, snapshots, restore procedures, SMB/object-storage access control, and
  capacity monitoring.
- Process supervision, container scheduling, node draining, restart policy, and host patching.
- DNS, PKI, certificate renewal, firewall rules, private routing, and network segmentation.
- Backup coordination across PostgreSQL, artifacts, key rings, policy material, certificates, and
  external dependencies.

## Failure Certification Matrix

| Scenario | Certification action | Expected result |
| :--- | :--- | :--- |
| Portal node loss | Stop one Portal process while traffic continues through the load balancer. | `/healthz` fails on the stopped node; surviving nodes continue serving non-sticky-new sessions; no duplicate report mutations occur. |
| Portal process crash | Kill the Portal process during queued or running refresh activity. | Process supervisor restarts it; stale node heartbeat expires; durable jobs remain recoverable or terminally failed with audit evidence. |
| Orchestrator node loss | Stop one Orchestrator process during scheduled workload. | Leases expire or transfer; due jobs run at most once; no duplicate scheduled mutations are emitted. |
| Network partition | Block a node from PostgreSQL or shared storage. | Partitioned node returns HTTP 503 from `/healthz`; load balancer removes it; whole-environment alerts still expose the underlying database/storage failure. |
| PostgreSQL failover | Promote/fail over PostgreSQL using the operator's database tooling. | Nodes return 503 while connections are unavailable and recover to 200 after the new primary is reachable; failed mutations are retried or reported once. |
| Shared-storage outage | Deny or unmount snapshot/dataset storage. | `/healthz` returns 503; snapshot/dataset writes fail closed; no catalog row points at a missing durable artifact. |
| Duplicate scheduler leadership | Start multiple schedulers against the same due job. | Database-backed leases allow one winner; losers observe the lease and suppress execution. |
| Orphaned work | Crash a worker after claim but before completion. | Reconciliation marks stale work failed/retryable according to the owning workflow; delivery ledgers and write fences prevent duplicate downstream mutation. |
| Recovery after outage | Restore PostgreSQL/storage/network, then restart nodes. | `/healthz` returns 200 only after database, storage, lease store, and topology checks pass; fleet preflight shows no pending migrations or drift. |

## Certification Evidence

For release or production go-live evidence, capture:

- `/healthz`, `/health`, `/metrics`, and `/api/fleet/status` samples before, during, and after each
  fault.
- PostgreSQL failover logs, load-balancer probe logs, and service-supervisor restart logs.
- Portal and Orchestrator application logs with correlation IDs.
- Job history, audit outbox state, security-event delivery state, and subscription delivery ledgers.
- Artifact root listings or storage snapshots that prove no missing durable artifacts were referenced.
- `etl-sql admin ha-soak fault-run` and `etl-sql admin ha-soak validate` outputs when using the native
  HA soak harness.

The certification pass condition is not "no failures observed." The pass condition is that each
failure is detected, unsafe nodes are removed from traffic, mutation ownership is fenced, recovery is
observable, and the final catalog contains no duplicate or lost committed mutations.

## Automated Coverage Map

The repository carries fast and integration coverage for the HA contract:

| Scenario | Automated coverage |
| :--- | :--- |
| Shared PostgreSQL and shared roots | `PortalMultiProcessPostgresTests.TwoPortalProcesses_StartAgainstSamePostgresAndSharedStorage` |
| Database outage and network partition | `PortalMultiProcessPostgresTests.DatabaseOutage_HealthzFailsClosed` and `DatabaseNetworkPartition_HealthzFailsClosedAndRecovers` |
| Cross-process state convergence and restart recovery | `CatalogWrites_AreVisibleAcrossProcesses_AndSurviveRestart`, `ProcessRestart_ReclaimsInterruptedRefreshJobAcrossProcesses`, and `ExecutionJobServiceTests.StartAsync_MarksAbandonedJobsAndReportRefreshAsInterrupted` |
| Duplicate refresh and scheduler ownership | `PortalMultiProcessPostgresTests.SimultaneousRefreshClaims_ConvergeAcrossProcesses`, `JobExecutionLeaseTests.TwoSchedulerInstances_SameDueJob_ExecuteExactlyOnce`, and `ClusterLockTests.IntervalGatedSend_ExactlyOneWinnerPerInterval_AndRestartSafe` |
| Shared-storage outage | `FaultInjectionRecoveryTests.Healthz_ReturnsUnavailableWhenSharedStorageFails` and snapshot write-failure tests |
| Lease loss and partition recovery | `ExecutionJobServiceTests.NodeLeaseLoss_CancelsLocalRunningJobs` and `PartitionRecovery_CancelsLocalWork_AndFencesStaleArtifactWriter` |
| Orphaned worker cleanup | `ProcessJobExecutorChaosTests.CleanupOrphans_KillsPersistedChildProcess_FromPreviousRun` |
| Subscription/job reconciliation | `SubscriptionLifecycleRecoveryTests.Reconcile_ConvergesScriptsAndOrchestratorJobsToRowState` |

Docker-backed `PortalMultiProcessPostgresTests` and long-running `etl-sql admin ha-soak` evidence are
the release gate for PostgreSQL/container-specific behavior. Fast-lane tests prove the fencing,
readiness, and recovery contracts without requiring Docker.
