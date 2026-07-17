# SHOW VIEWS
Displays session-scoped ETL-SQL query views.

## Syntax
```sql
SHOW VIEWS [INTO #table];
```

## Parameters
- **INTO #table** — Optional. Captures the result set into a temp table for programmatic use.

## Returns
A result set with view name and the underlying query text for each session view.

## Example
```sql
-- Create some session views
CREATE VIEW ActiveOrders AS SELECT * FROM #orders WHERE Status = 'Active';
CREATE VIEW HighValue AS SELECT * FROM #orders WHERE Amount > 10000;

-- List session query views
SHOW VIEWS;

-- Capture and inspect
SHOW VIEWS INTO #views;
SELECT Name, Query FROM #views;
```

## Notes
- Only shows views defined in the current engine session. Database-level views on remote connections are not listed.
- Views created with `CREATE VIEW` are session-scoped and do not persist after the session ends.

## References
- [SHOW Commands](README.md)
