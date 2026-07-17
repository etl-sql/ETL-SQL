# Health Monitoring and Audit Log

## 10. Health Monitoring

`GET /health` returns a JSON document with the overall portal status and the state of each subsystem.

```json
{
  "status": "Healthy",
  "checks": {
    "db": {
      "status": "Healthy",
      "description": "Database reachable. 3 users registered."
    },
    "orchestrator": {
      "status": "Degraded",
      "description": "Orchestrator DB not found. Scheduled jobs will not run."
    },
    "execution": {
      "status": "Healthy",
      "description": "0/4 slots in use. 2 SMTP connections. 5 active subscriptions."
    }
  }
}
```

| Check | Healthy | Degraded | Unhealthy |
| :--- | :--- | :--- | :--- |
| `db` | Configured Portal database reachable | — | Cannot connect to database |
| `orchestrator` | Orchestrator DB found | Orchestrator DB not found | — |
| `execution` | Capacity available | Slots nearing cap | — |

The overall `status` is the worst of all individual checks: `Unhealthy` > `Degraded` > `Healthy`.

> [!TIP]
> Wire `GET /health` into your uptime monitor or monitoring dashboard. A `Degraded` response means the portal is functional but subscriptions may not fire. An `Unhealthy` response means the database is down and no API calls will succeed.

`GET /healthz` is the load-balancer-ready probe. It is anonymous and intentionally checks only the
dependencies a Portal node needs to accept traffic safely: portal database connectivity, shared
artifact storage readability, and node-registry/lease-store connectivity. It returns HTTP 200 with
`"status": "Healthy"` when all three checks are `ok`, otherwise HTTP 503.

For operational alert thresholds, Prometheus routing, and runbook links, see
[Alerting and Service Objectives](../../architecture/decisions/Alerting_Service_Objectives.md). The Portal emits active
alert conditions through the operational digest and through `/metrics` as
`etlsql_portal_operational_alert_active{severity,alert_code,runbook,...}` so external monitoring
systems can handle deduplication, severity routing, recovery notifications, and escalation.

---

## 10. Audit Log

Every significant action is written to the audit log. Open **Admin → Audit Log** to browse or search.

### 10.1 Logged Events

| Action | Trigger |
| :--- | :--- |
| `LOGIN` | Successful login |
| `LOGIN_FAILED` | Failed login attempt |
| `LOGOUT` | Explicit logout |
| `PASSWORD_CHANGED` | User changed their own password |
| `CREATE_USER` | Admin created a new user |
| `UPDATE_USER` | Admin edited a user |
| `DELETE_USER` | Admin deleted a user |
| `CREATE_FOLDER` | Folder created |
| `DELETE_FOLDER` | Folder deleted |
| `PUBLISH_REPORT` | Report published |
| `DELETE_REPORT` | Report soft-deleted |
| `EXECUTE_REPORT` | Report execution started |
| `CREATE_SUBSCRIPTION` | Subscription created |
| `DELETE_SUBSCRIPTION` | Subscription deleted |
| `CREATE_SMTP` | SMTP connection added |
| `DELETE_SMTP` | SMTP connection removed |
| `REFRESH_TOKEN_REUSE` | A revoked refresh token was replayed (theft signal); all of the user's sessions were invalidated |
| `UPDATE_ORCHESTRATOR_SETTINGS` | Admin changed the Orchestrator URL or API key via the Settings tab |

### 10.2 Exporting the Audit Log

Click **Export CSV** to download up to 10,000 most-recent entries as a UTF-8 CSV file. You can also filter by action type and user before exporting. The export includes each row's **correlation id** — the HTTP request trace identifier or the background operation id (e.g. `delivery-<id>` for subscription deliveries) — so every event can be tied back to the operation that produced it.

### 10.3 Audit Guarantees, Retention, and the Tamper-Evidence Boundary

- **Mutations and their audit rows commit together.** Security-sensitive changes (user role/active/password/token changes, user deletion and ownership transfer, group membership, folder and dataset ACLs, dataset metadata/move/delete, SMTP definitions, share-link/embed-token revocation, subscription delivery outcomes) write their audit row in the same database transaction as the change itself: the operation cannot succeed without its durable audit event, and a rejected or conflicted operation leaves no audit row behind. Informational events (views, exports, logins, denials) remain independent best-effort records.
- **Retention is opt-in.** By default every audit row is kept forever. Set `Portal:Audit:RetentionDays` to enable a daily sweep that deletes rows older than the window (`Portal:Audit:PurgeIntervalSeconds` tunes the cadence). Export or forward rows you must keep **before** enabling retention.
- **The audit table is not tamper-proof — by design.** It lives in the writable portal database, so an attacker (or administrator) with database access can alter it. The supported enterprise posture is to **export or forward audit data to external append-only storage on a schedule** (the CSV endpoint, or log forwarding per the security guide) and treat the in-portal table as the operational view. Tamper-evident hash chaining inside the portal database is a deliberate non-goal for this release (see `ROADMAP.md`).

---

