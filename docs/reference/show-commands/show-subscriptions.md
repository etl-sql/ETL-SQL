# SHOW SUBSCRIPTIONS
Displays defined report subscriptions.

## Syntax
```sql
SHOW SUBSCRIPTIONS [INTO #table];
```

## Parameters
- **INTO #table** — Optional. Captures the result set into a temp table for programmatic use.

## Returns
A result set with subscription name, target report, schedule, recipients, and status.

## Example
```sql
-- View all report subscriptions
SHOW SUBSCRIPTIONS;

-- Capture and filter
SHOW SUBSCRIPTIONS INTO #subs;
SELECT SubscriptionName, ReportName, Schedule FROM #subs WHERE Status = 'Active';
```

## Notes
- Shows subscriptions created via `CREATE SUBSCRIPTION` within `EXECUTE portal BEGIN...END` blocks.

## References
- [SHOW Commands](README.md)
