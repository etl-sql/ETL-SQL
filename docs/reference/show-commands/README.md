# SHOW Commands

`SHOW` displays engine state — active connections, variables, execution profile, tables, jobs, locks, and more. All SHOW commands support `INTO #table` to capture the result set for programmatic use.

```sql
SHOW <subject> [INTO #table];
```

## Engine State

| Command | Description |
| :--- | :--- |
| [SHOW CONNECTIONS](show-connections.md) | All registered data sources and their status |
| [SHOW CONNECTION CONFIG](show-connection-config.md) | Configuration options for a specific connection (redacted) |
| [SHOW VARIABLES](show-variables.md) | All declared variables in scope (SECRET vars masked) |
| [SHOW PROFILE](show-profile.md) | Per-statement timing (requires `SET PROFILING = ON`) |
| [SHOW TABLES](show-tables.md) | Tables available on a connection |
| [SHOW VIEWS](show-views.md) | Session-scoped ETL-SQL query views |
| [SHOW TAGS](show-tags.md) | Lineage tags applied in the current session |
| [SHOW VERSION](show-version.md) | Engine version and build metadata |
| [SHOW LOCKS](show-locks.md) | Active database/job throttle slots and concurrency queue details |

## Orchestrator & Jobs

| Command | Description |
| :--- | :--- |
| [SHOW JOBS](show-jobs.md) | Active and pending background or scheduled jobs |
| [SHOW JOB HISTORY](show-job-history.md) | Recent job execution records (all jobs, or one named job) |
| [SHOW JOB STATE](show-job-state.md) | Saved job-state key/value pairs (watermarks, backup markers) |
| [SHOW HOST METRICS](show-host-metrics.md) | Host-utilization time series for capacity planning |
| [SHOW SUBSCRIPTIONS](show-subscriptions.md) | Defined report subscriptions |

## Report Portal

These commands must be executed within an `EXECUTE portal BEGIN...END` block.

| Command | Description |
| :--- | :--- |
| [SHOW REPORT](show-report.md) | Portal report metadata |
| [SHOW REPORT HISTORY](show-report-history.md) | Portal report refresh/history rows |
| [SHOW REPORT DEPENDENCIES](show-report-dependencies.md) | Dependencies discovered for a portal report |
| [SHOW SHARE LINKS FOR REPORT](show-share-links.md) | Active portal share links |
| [SHOW EMBED TOKENS FOR REPORT](show-embed-tokens.md) | Portal embed tokens |
| [SHOW SAVED VIEWS FOR REPORT](show-saved-views.md) | Saved parameter views |
| [SHOW ALERTS FOR REPORT](show-alerts.md) | Portal report alerts |
| [SHOW FAVORITES](show-favorites.md) | Portal favorites |
| [SHOW RECENT REPORTS](show-recent-reports.md) | Recently viewed portal reports |
| [SHOW CATALOG SEARCH](show-catalog-search.md) | Portal catalog search |
| [SHOW EFFECTIVE PERMISSIONS](show-effective-permissions.md) | Resolved portal permissions |
| [SHOW PORTAL USAGE METRICS](show-portal-usage-metrics.md) | Portal usage and refresh metrics |
| [SHOW ACTIVE SESSIONS](show-active-sessions.md) | Unrevoked, unexpired portal refresh sessions |

## References

- [Statement Reference](../statements/README.md)
- [Syntax Index](../../syntax-index.md)
