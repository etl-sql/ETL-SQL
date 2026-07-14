# Alerting and Service Objectives

This guide defines baseline service indicators, starter objectives, alert routing, and runbook
actions for Report Portal and related operational signals. Tune thresholds from measured workload
history and business criticality; these defaults are conservative starter values, not universal SLOs.

## Service Indicators

| Area | Indicator | Starter objective | Primary signal |
| :--- | :--- | :--- | :--- |
| Availability | Portal `/healthz` readiness | 99.9% during business hours after planned maintenance is excluded | Load balancer health, uptime monitor |
| Queue wait | Interactive execution queue age | p95 under 5 minutes during normal peaks | `etlsql_portal_execution_queue_age_average_seconds` |
| Execution success | Report execution failure rate | under 5% excluding intentional cancellations and test failures | `etlsql_portal_execution_recent_failures / etlsql_portal_execution_recent_total` |
| Execution latency | Completed report duration | p95 target per deployment; start with the capacity-test baseline | operational metrics and capacity report |
| Freshness | Scheduled snapshots/datasets | no critical report beyond its business freshness window | report refresh history and stale snapshot/dataset checks |
| Policy availability | Policy authority and cache | governed execution never runs on expired policy | policy health checks and policy expiry/signature alerts |
| Audit delivery | Durable audit outbox | no pending event older than 15 minutes for fail-closed deployments | `etlsql_portal_audit_outbox_oldest_pending_age_seconds` |
| Security delivery | Security-event outbox | no pending event older than 15 minutes when collector is configured | `etlsql_security_event_oldest_pending_age_seconds` |
| Database health | Portal database connectivity and migrations | reachable and zero pending migrations | `/health`, migration metrics |
| Recovery | Backup/restore evidence | successful backup within the configured recovery window | backup report admin service |

## Alert Routing

ETL-SQL emits operational alerts through existing observability systems:

- **Operational digest email** - `Portal:OperationalDigest` sends a scheduled summary and alert list.
- **Prometheus scrape** - `/metrics` emits `etlsql_portal_operational_alert_active` with `severity`,
  `alert_code`, and `runbook` labels for active alert conditions.
- **Health probes** - `/health` and `/healthz` remain the readiness and liveness inputs for uptime
  monitors and load balancers.

ETL-SQL does not implement a proprietary pager. Route the Prometheus alert gauge or digest output to
the organization's monitor, SIEM, ticketing system, or on-call platform. Deduplicate by
`component`, `node`, and `alert_code`; notify recovery when the alert gauge disappears or the digest
no longer lists the code.

## Configuration

```json
{
  "Portal": {
    "OperationalDigest": {
      "Enabled": true,
      "AlertOnly": true,
      "IntervalHours": 1,
      "Recipients": "ops@example.com",
      "SmtpAlias": "ops-smtp",
      "FailureRatePercentThreshold": 25,
      "QueueDepthAlertThreshold": 20,
      "QueueAgeSecondsAlertThreshold": 300,
      "DeliveryFailureRatePercentThreshold": 25,
      "AuditOutboxPendingAlertThreshold": 1000,
      "AuditOutboxAgeSecondsAlertThreshold": 900,
      "SecurityEventPendingAlertThreshold": 1000,
      "SecurityEventAgeSecondsAlertThreshold": 900,
      "DatasetStorageBytesAlertThreshold": 0,
      "SnapshotStorageBytesAlertThreshold": 0,
      "SnapshotFreshnessHours": 0,
      "DatasetFreshnessHours": 0,
      "PolicyVersionExpiryWarningHours": 72,
      "CertificateExpiryWarningHours": 168,
      "AlertOnPolicyAuthorityUnavailable": true,
      "AlertOnDatabaseConnectivityFailure": true,
      "AlertOnDatabasePoolExhaustion": true,
      "AlertOnUnhealthyFleetNodes": true,
      "RunbookBaseUri": "Docs/Operations/Alerting_Service_Objectives.md"
    }
  }
}
```

Set storage thresholds after measuring normal dataset and snapshot growth. Leave a threshold at `0`
to disable that alert. Freshness windows are also disabled by default; set them per deployment based
on business requirements for report snapshots and shared datasets.
Set `PolicyVersionExpiryWarningHours` to the amount of lead time operators need before an active
organization policy expires. Set it to `0` only for standalone portals that do not use the policy
authority.
Set `CertificateExpiryWarningHours` to the lead time needed to replace client certificates before
enrolled machines lose policy access. Database and fleet-node alert switches are enabled by default;
disable them only when those signals are already routed from `/health` or an external fleet monitor.

## Runbooks

### portal-execution-failure-rate

Severity: critical.

1. Open the admin execution history and group failures by report, user/service account, and error.
2. Check whether failures started after a report script, connector, policy, or deployment change.
3. Inspect downstream database/API/file-share health before retrying high-volume work.
4. If errors contain sensitive provider details, verify redaction before sharing diagnostics.

### portal-execution-queue-depth

Severity: warning.

1. Check active executions against `Resources:MaxConcurrentReportExecutions`.
2. Compare queue age with CPU, memory, and storage pressure in the capacity report.
3. Spread scheduled refreshes/subscriptions if the pressure is bursty.
4. Raise concurrency only after host and downstream systems have headroom.

### portal-execution-queue-age

Severity: warning.

1. Confirm whether queued work is waiting on global, per-user, per-group, or node-capacity gates.
2. Use the capacity report's queue-wait versus run-duration diagnosis.
3. If run duration is low but queue age is high, tune schedules or execution caps.
4. If run duration is also high, inspect report logic and downstream systems first.

### portal-delivery-failure-rate

Severity: warning.

1. Verify SMTP connection health and credentials.
2. Check recipient rejection, relay throttling, and attachment size limits.
3. Review subscription delivery ledger rows for repeated failures.
4. Pause noisy subscriptions if they are causing repeated retries.

### portal-schema-pending-migrations

Severity: warning.

1. Confirm the deployed binary version and configured database provider.
2. Run the approved migration/upgrade path for the environment.
3. In HA, ensure only one node applies migrations and other nodes are compatible.
4. Recheck `/health` and `/metrics` after migration completes.

### portal-audit-outbox-backlog

Severity: critical.

1. Check collector reachability, TLS, authentication, and network ACLs.
2. Verify whether `Portal:Audit:RequireRemoteDelivery` is enabled and whether mutations are fail-closed.
3. Do not purge pending audit rows unless an administrator has exported and approved the loss.
4. Resume transport and confirm backlog drains.

### portal-audit-outbox-age

Severity: critical.

1. Treat this as delivery stall even when backlog count is small.
2. Inspect transport logs and collector acknowledgements.
3. Confirm clock synchronization between Portal and collector systems.
4. Verify the oldest pending age returns below threshold after recovery.

### security-event-outbox-backlog

Severity: warning.

1. Check security-event collector endpoint configuration and reachability.
2. Inspect local security-event transport logs for repeated failures.
3. Confirm security events are not being filtered below the configured minimum severity.
4. Verify backlog drains without dropping required events.

### security-event-outbox-age

Severity: warning.

1. Check whether the collector is down, slow, or rejecting batches.
2. Confirm local outbox storage is writable and not at capacity.
3. Review network and certificate changes near the first stalled event.
4. Verify the oldest pending age clears after recovery.

### portal-dataset-storage-bytes

Severity: warning.

1. Check dataset retention and stale cached datasets.
2. Confirm dataset root capacity and backup behavior.
3. Remove unused datasets through supported Portal operations.
4. Increase storage or repartition large datasets if growth is expected.

### portal-snapshot-storage-bytes

Severity: warning.

1. Check report snapshot retention and large export/report patterns.
2. Confirm snapshot root capacity and backup behavior.
3. Reduce retention or remove stale reports if appropriate.
4. Increase storage before the volume reaches the capacity report floor.

### portal-stale-snapshots

Severity: warning.

1. Identify the reports counted stale and compare their expected freshness objective with business use.
2. Check recent refresh failures, execution queue pressure, and subscription/refresh schedules.
3. Trigger a manual refresh for critical reports after fixing the underlying failure.
4. Adjust `SnapshotFreshnessHours` only when the business freshness target has changed.

### portal-stale-datasets

Severity: warning.

1. Identify stale shared datasets and the reports or jobs that depend on them.
2. Check dataset refresh jobs, source-system availability, and dataset storage/key errors.
3. Refresh critical datasets after validating source-system health and permissions.
4. Adjust `DatasetFreshnessHours` only when the business freshness target has changed.

### portal-policy-version-expired

Severity: critical.

1. Publish a new signed organization policy version for the affected tenant/environment immediately.
2. Confirm enrolled hosts retrieve and validate the new active policy before protected workloads run.
3. Check policy-authority signing keys, clock synchronization, and rollback/expiry guard logs.
4. Treat repeated expiry as a release-process defect; add an earlier approval or publication checkpoint.

### portal-policy-version-expiring

Severity: warning.

1. Identify the active policy versions that expire inside `PolicyVersionExpiryWarningHours`.
2. Complete review and approval for the replacement policy before the current version expires.
3. Validate canary rollout where used, then activate the replacement policy.
4. Confirm Prometheus and digest alerts clear after the active version has sufficient lifetime.

### portal-policy-signature-unavailable

Severity: critical.

1. Check the policy-authority signing certificate or key reference configured for the Portal.
2. Verify the OS certificate store, private-key ACLs, key vault, or mounted secret is available to the service account.
3. Do not publish or roll back policies until the signing surface is healthy.
4. Recheck `/health` and the operational digest after restoring key access.

### portal-client-certificate-expiry

Severity: warning before expiry; critical after expiry.

1. Identify the enrolled machine certificate reported by fleet inventory or local enrollment status.
2. Issue and bind a replacement certificate before the warning window closes.
3. Confirm policy retrieval succeeds with the new certificate and old credentials are retired.
4. Treat repeated late rotations as a certificate lifecycle process defect.

### portal-database-connectivity

Severity: critical.

1. Check PostgreSQL/SQLite reachability, credentials, DNS, TLS, and network ACLs.
2. Remove this node from load-balancer rotation if `/healthz` is failing.
3. For PostgreSQL HA, verify primary/replica state and failover completion before restarting Portal nodes.
4. Confirm the health check and `etlsql_portal_database_connectivity_healthy` return healthy.

### portal-database-pool-exhaustion

Severity: critical.

1. Check database connection pool limits, slow queries, blocked transactions, and leaked long-running sessions.
2. Compare Portal execution concurrency with database `max_connections` and downstream capacity.
3. Reduce Portal concurrency or increase pool/database limits only after confirming the bottleneck.
4. Verify pool timeout/exhaustion messages stop and queue age returns below threshold.

### portal-unhealthy-fleet-nodes

Severity: warning.

1. Open `/health` and `/healthz` on the affected node and inspect failing checks.
2. Check node heartbeat, artifact storage, policy authority, database, and execution-capacity findings.
3. Drain the node before restart or repair when readiness is unsafe.
4. Confirm the fleet alert clears after the node reports Healthy.

## Tuning

Start with the defaults, then tune from measured data:

- Set queue thresholds from normal peak-hour queue depth and business tolerance.
- Set failure thresholds separately for interactive reports and scheduled workloads if the external
  monitor supports per-route or per-job grouping.
- Set outbox thresholds below fail-closed limits so operators receive warning before mutations block.
- Set storage thresholds from capacity-report forecasts and backup windows.
- Set freshness thresholds from explicit business freshness targets, not from default refresh
  intervals alone.
- Set policy expiry lead time from policy review and rollout duration, including canary soak time.
- Set certificate expiry lead time from certificate authority SLA and host restart requirements.
- Use `AlertOnly = true` when the digest is routed to an on-call or ticketing mailbox.
