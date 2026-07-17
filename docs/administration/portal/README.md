# Portal Administration

Administer the Report Portal application: users, permissions, publishing, subscriptions, and audit.

## Pages

- [Configuration Reference](configuration.md) - All settings live under the `"Portal"` key in `appsettings.json`. Every key can be overridden with an environment variable using the double-underscore separator: `Portal__Jwt__Secret`.
- [SMTP Connections and Subscriptions](connections-and-subscriptions.md) - SMTP connections are named credentials used by subscriptions to send email. Open **Admin → SMTP**.
- [Deployment and First-Run Setup](deployment.md) - The Portal is an ASP.NET Core 10 web application (`ETL-SQL-Portal`). It uses **SQLite** by
- [Health Monitoring and Audit Log](monitoring-and-audit.md) - `GET /health` returns a JSON document with the overall portal status and the state of each subsystem.
- [Orchestrator Management](orchestrator-management.md) - The portal includes a built-in **Orchestrator** tab that provides a web interface for managing ETL-SQL scheduled jobs. Access is controlled by the `OrchestratorAccess` policy: **Admin** or **OrchestratorManager** role.
- [Groups and Folder Permissions](permissions.md) - Folder visibility is controlled through **groups** and **ACLs** (access control lists).
- [Production Readiness Checklist](production-readiness.md) - Use this checklist before promoting the Portal to a production or customer-facing environment. Items marked **Required** will cause data loss, security exposure, or service failure if skipped. Items marked **Recommended** reduce operational risk.
- [Publishing Reports](publishing.md) - Publishing registers a `.rptsql` script file as a named report in a folder.
- [Quick Start](quick-start.md) - To get the Portal running in under 5 minutes:
- [Extended Admin Scripting](scripting.md) - The Portal connector supports script-first administration inside a remote block:
- [Security Model](security.md) - For non-interactive API and CLI identities, see [Service Accounts](../../reference/portal-admin/service-accounts.md).
- [User Management](users.md) - Open **Admin → Users** to manage accounts.

## See Also

- [Administration](../README.md) - the full admin area.
- [Platform Administration](../platform/README.md) · [Portal Administration](../portal/README.md) · [Orchestration](../orchestration/README.md)
- [CLI Reference](../../reference/cli/README.md) - every `etl-sql` command.
