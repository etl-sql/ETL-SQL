# Portal Saved Views
Create and manage named parameter snapshots for portal reports inside an `EXECUTE portal` block.

## Syntax
```sql
EXECUTE portal BEGIN
  CREATE SAVED VIEW 'ViewName' FOR REPORT 'ReportName'
    WITH (PARAMETERS = '@param1=value1,@param2=value2');
  SHOW SAVED VIEWS FOR REPORT 'ReportName';
  DROP SAVED VIEW 'ViewName' FOR REPORT 'ReportName';
END;
```

## Examples
```sql
-- Create a saved view for a specific quarter and region
EXECUTE portal BEGIN
  CREATE SAVED VIEW 'Q1 2025 - West' FOR REPORT 'Sales Dashboard'
    WITH (PARAMETERS = '@start_date=2025-01-01,@end_date=2025-03-31,@region=West');
END;

-- Create a second view for the same report
EXECUTE portal BEGIN
  CREATE SAVED VIEW 'Q2 2025 - East' FOR REPORT 'Sales Dashboard'
    WITH (PARAMETERS = '@start_date=2025-04-01,@end_date=2025-06-30,@region=East');
END;

-- List all saved views for a report
EXECUTE portal BEGIN
  SHOW SAVED VIEWS FOR REPORT 'Sales Dashboard' INTO #views;
END;
SELECT view_name, parameters, created_by, created_at FROM #views;

-- Remove a saved view
EXECUTE portal BEGIN
  DROP SAVED VIEW 'Q1 2025 - West' FOR REPORT 'Sales Dashboard';
END;
```

## Notes
- Saved views store a named set of parameter values for a report, allowing users to quickly restore a specific data perspective without re-entering filter values.
- The `PARAMETERS` value is a comma-separated list of `@name=value` pairs. Parameter names must match the parameters declared in the report script.
- Date and datetime values should be formatted as ISO 8601 strings (e.g., `2025-01-01` or `2025-01-01T08:00:00`).
- Saved views are scoped to the report — the same view name can exist on different reports.
- `DROP SAVED VIEW` removes the view definition but does not affect the underlying report or its data.
- Dropping a report does not automatically remove its saved views; clean up views before dropping a report to avoid orphaned entries.
- See: PORTAL_REPORT, PORTAL_SHOW

References:
- [Data Connectors](../../administration/platform/README.md)
- [Portal Admin Commands](README.md)
