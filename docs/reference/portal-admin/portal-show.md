# Portal Show Commands
Query portal state, user information, report metadata, and usage metrics inside an `EXECUTE portal` block.

## Syntax
```sql
EXECUTE portal BEGIN
  SHOW USERS [INTO #users];
  SHOW REPORTS [IN FOLDER 'FolderName'] [INTO #reports];
  SHOW FAVORITES [INTO #favorites];
  SHOW RECENT REPORTS [INTO #recent];
  SHOW ACTIVE SESSIONS [INTO #sessions];
  SHOW EFFECTIVE PERMISSIONS FOR USER 'username' [INTO #perms];
  SHOW PORTAL USAGE METRICS [INTO #metrics];
  SHOW PORTAL OPERATIONAL METRICS [INTO #ops];
  SHOW REPORT HISTORY 'ReportName' [INTO #history];
  SHOW REPORT DEPENDENCIES 'ReportName' [INTO #deps];
  SHOW CATALOG SEARCH 'search_term' [INTO #results];
END;
```

## Examples
```sql
-- List all portal users
EXECUTE portal BEGIN
  SHOW USERS INTO #users;
END;
SELECT username, email, role, active FROM #users ORDER BY username;

-- List all reports in a specific folder
EXECUTE portal BEGIN
  SHOW REPORTS IN FOLDER 'Finance' INTO #reports;
END;
SELECT report_name, published_at, last_refreshed FROM #reports;

-- List all reports across all folders
EXECUTE portal BEGIN
  SHOW REPORTS INTO #all_reports;
END;

-- List the current user's favorite reports
EXECUTE portal BEGIN
  SHOW FAVORITES INTO #favorites;
END;

-- List recently accessed reports for the current session
EXECUTE portal BEGIN
  SHOW RECENT REPORTS INTO #recent;
END;

-- Show all active portal sessions
EXECUTE portal BEGIN
  SHOW ACTIVE SESSIONS INTO #sessions;
END;
SELECT username, session_started, last_active, client_ip FROM #sessions;

-- Resolve effective permissions for a specific user
EXECUTE portal BEGIN
  SHOW EFFECTIVE PERMISSIONS FOR USER 'jsmith' INTO #perms;
END;
SELECT folder, access_level, granted_via FROM #perms;

-- Check longer-term usage metrics
EXECUTE portal BEGIN
  SHOW PORTAL USAGE METRICS INTO #metrics;
END;
SELECT reportName, views, uniqueViewers, lastViewedAt, lastRefreshStatus FROM #metrics;

-- Check live operational load and resource metrics
EXECUTE portal BEGIN
  SHOW PORTAL OPERATIONAL METRICS INTO #ops;
END;
SELECT activeExecutions, queuedExecutions, averageExecutionDurationMs FROM #ops;

-- Review the publish and refresh history for a report
EXECUTE portal BEGIN
  SHOW REPORT HISTORY 'Sales Dashboard' INTO #history;
END;
SELECT event_type, occurred_at, performed_by, notes FROM #history ORDER BY occurred_at DESC;

-- Identify which datasets and connectors a report depends on
EXECUTE portal BEGIN
  SHOW REPORT DEPENDENCIES 'Sales Dashboard' INTO #deps;
END;
SELECT dependency_type, dependency_name, status FROM #deps;

-- Full-text search across report names, descriptions, and tags
EXECUTE portal BEGIN
  SHOW CATALOG SEARCH 'finance quarterly' INTO #results;
END;
SELECT report_name, folder, relevance_score FROM #results ORDER BY relevance_score DESC;
```

## Notes
- All `SHOW` commands support an optional `INTO #tempTable` clause that captures the result set as a temp table for further processing with `SELECT`, `INSERT`, or `EXPORT`.
- Omitting `INTO` prints the results directly to the output (same behavior as a bare `SELECT`).
- `SHOW EFFECTIVE PERMISSIONS FOR USER` resolves the combined permissions from all individual user grants and group memberships. The `granted_via` column indicates whether the access came from a direct user grant or a group.
- `SHOW PORTAL USAGE METRICS` returns report view counts, unique viewers, refresh health, and subscription delivery failures for the requested period.
- `SHOW PORTAL OPERATIONAL METRICS` returns live queue depth, execution caps, recent failure counts, storage size, schema status, and last-24-hour execution load/resource buckets.
- `SHOW CATALOG SEARCH` performs a full-text search across report names, descriptions, and tags. The search term supports simple keyword matching; multiple words are treated as an AND query.
- `SHOW REPORT HISTORY` covers publish events, refresh cycles, validation runs, and permission changes for the named report.
- `SHOW REPORT DEPENDENCIES` lists the datasets, connector types, and external scripts the report depends on, along with their current availability status.
- `SHOW ACTIVE SESSIONS` is an ADMIN-only command; non-admin users see only their own session.
- See: PORTAL_USER, PORTAL_GROUP, PORTAL_FOLDER, PORTAL_PERMISSIONS, PORTAL_REPORT, PORTAL_DATASET

References:
- [Data Connectors](../../guides/administration.md)
- [Portal Admin Commands](README.md)
