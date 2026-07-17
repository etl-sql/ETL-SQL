# SHOW LOCKS
Displays active database and job throttle slots and concurrency queue details.

## Syntax
```sql
SHOW LOCKS [INTO #table];
```

## Parameters
- **INTO #table** — Optional. Captures the result set into a temp table for programmatic use.

## Returns
A result set with lock type, owner, target resource, acquisition time, and queue wait information.

## Example
```sql
-- Check active locks and queue wait times
SHOW LOCKS;

-- Capture and analyze
SHOW LOCKS INTO #locks;
SELECT LockType, Owner, Target, WaitTimeMs FROM #locks ORDER BY WaitTimeMs DESC;
```

## Notes
- Useful for diagnosing concurrency bottlenecks and throttle queue contention.
- In Orchestrator-managed environments, shows lease-based locks across nodes.

## References
- [SHOW Commands](README.md)
