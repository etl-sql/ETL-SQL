# Portal `eng.*` Catalog

A `PORTAL` connection exposes permission-aware administrative tables and table-valued functions under its `eng` schema. Query them from an `EXECUTE portal BEGIN ... END` block or qualify them with the connection name.

```sql
SELECT * INTO #history
FROM prod_portal.eng.report_history('Monthly Sales');
```

## Tables

- **`eng.users`** — Portal users visible to the administrator.
- **`eng.connections`** — Governed shared connections with secrets redacted.
- **`eng.connection_config`** — Redacted connection configuration rows.
- **`eng.sessions` / `eng.active_sessions`** — Active Portal sessions.
- **`eng.reports`** — Report catalog rows.
- **`eng.subscriptions`** — Report-delivery subscriptions.
- **`eng.operational_metrics`** — Current queue, execution, storage, audit, and schema health.
- **`eng.protected_data` / `eng.protected_data_suggestions`** — Protected-data inventory and review suggestions.

## Table-valued functions

- **`eng.recent_reports(limit)`** — Recent reports visible to the caller.
- **`eng.favorites([user_or_limit])`** — Favorite reports for the caller or named user.
- **`eng.usage_metrics(days)`** — Usage and refresh-health metrics.
- **`eng.audit([limit, action])`** — Portal audit events.
- **`eng.catalog_search(query [, limit])`** — Permission-aware catalog search.
- **`eng.report_history(report)`** — Publish, refresh, validation, and audit history.
- **`eng.report_dependencies(report)`** — Dataset, script, refresh-job, and source dependencies.
- **`eng.share_links(report)`** — Named share links for a report.
- **`eng.embed_tokens(report)`** — Named embed tokens for a report.
- **`eng.saved_views(report)`** — Saved parameter views for a report.
- **`eng.alerts(report)`** — Alerts for a report.
- **`eng.effective_permissions(target)`** or **`eng.effective_permissions(type, target)`** — Resolved user, folder, or report access.
- **`eng.data_quality_rules(job)`** — `@expect`/`@fail` rules protecting each target and column in the named job's script. Requires data-quality steward access; the job name is required because rules bind to the statement that declares them.

## References

- [Portal Administration](../portal-admin/README.md)
- [Engine Catalog](README.md)
