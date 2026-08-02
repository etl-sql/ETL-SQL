# Administration Operations Hub

The Admin **Operations** tab is the Portal control room for online-safe operational work. It joins
durable health and authority records so an administrator can move from a signal to the responsible
identity, access grant, node, or service run without exposing credentials or crossing a host
recovery boundary.

## Operational signals

- **Fleet** — Shows the environment and node, installed version, schema state, storage provider,
  policy/upgrade readiness, and any readiness findings returned by the fleet-status contract.
- **Workload** — Shows active and queued executions, capacity, recent failures, stale datasets,
  storage use, audit delivery, and security-event queue state.
- **Partial availability** — Each source loads independently. If one service is unavailable, the
  page names the missing source and continues to display the durable data it did receive; it never
  turns an incomplete snapshot into an all-clear state.

## Authority workflows

- **Pending approvals** — Review report-access requests, choose the permission to grant, and record
  an approval or denial reason.
- **Service accounts** — View owner, scopes, roles, expiry, status, and last use. Administrators can
  create accounts, rotate their secret, revoke them, and inspect resource-filtered audit history.
  A new or rotated client secret appears once and is removed from the document when the dialog
  closes.
- **Anonymous access** — Inventory share links and embed tokens by report, folder, creator, expiry,
  and effective status. Revocation uses the inventory identifier; the browser and audit detail do
  not receive or record the bearer token.

## Administrative services

The Automation section shows whether each native failure-digest, backup-report, and capacity-report
service is enabled, its interval, SMTP alias and recipients, last outcome, calculated next run, and
durable run history. Service configuration remains file-based; this page is an operational view,
not a hidden live-reconfiguration path.

## Trust boundary

The hub is restricted to the `Admin` role. Host bootstrap, package deployment, database migration,
traffic draining, backup custody, restore, and destructive recovery remain external operator
actions. The Portal exposes evidence and safe online mutations only.

## References

- [Health Monitoring and Audit Log](monitoring-and-audit.md)
- [Self-Service Access Request Workflow](access-requests.md)
- [Production Readiness Checklist](production-readiness.md)
