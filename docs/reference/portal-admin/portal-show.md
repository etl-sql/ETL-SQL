# Portal `eng.*` Inspection

Query Portal users, reports, sessions, permissions, audit, dependencies, and operational data inside an `EXECUTE portal` block with ordinary `SELECT` statements.

## Syntax

```sql
EXECUTE portal BEGIN
  SELECT * INTO #users FROM eng.users;
  SELECT * INTO #reports FROM eng.reports;
  SELECT * INTO #favorites FROM eng.favorites(25);
  SELECT * INTO #recent FROM eng.recent_reports(20);
  SELECT * INTO #sessions FROM eng.active_sessions;
  SELECT * INTO #perms FROM eng.effective_permissions('USER', 'username');
  SELECT * INTO #metrics FROM eng.usage_metrics(30);
  SELECT * INTO #ops FROM eng.operational_metrics;
  SELECT * INTO #audit FROM eng.audit(100, 'STEWARD_LINEAGE_IMPACT');
  SELECT * INTO #history FROM eng.report_history('ReportName');
  SELECT * INTO #deps FROM eng.report_dependencies('ReportName');
  SELECT * INTO #results FROM eng.catalog_search('search term', 50);
END;
```

## Examples

```sql
EXECUTE portal BEGIN
  SELECT * INTO #reports
  FROM eng.reports
  WHERE folder = 'Finance';

  SELECT * INTO #perms
  FROM eng.effective_permissions('USER', 'jsmith');

  SELECT * INTO #history
  FROM eng.report_history('Sales Dashboard');
END;

SELECT * FROM #reports ORDER BY report_name;
SELECT * FROM #perms ORDER BY access_level;
SELECT * FROM #history ORDER BY occurred_at DESC;
```

## Notes

- **Normal query clauses** — `WHERE`, `JOIN`, `ORDER BY`, `LIMIT`, and `INTO` work on inspection rows.
- **Permissions** — Portal endpoints remain permission-aware; an `eng.*` query does not bypass authorization.
- **Table-valued functions** — Search, history, dependencies, permissions, favorites, recent reports, audit, and usage accept arguments as documented in the [Portal engine catalog](../eng/portal-catalog.md).
- **Secret handling** — Connection configuration is redacted before it reaches the result set.

## References

- [Portal Engine Catalog](../eng/portal-catalog.md)
- [Portal Admin Commands](README.md)
