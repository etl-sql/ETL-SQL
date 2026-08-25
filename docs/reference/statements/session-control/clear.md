# CLEAR SESSION
Cleans up session state: temp files, recovery manifests, encrypted session data, and disk-spill artifacts.

## Syntax
```sql
-- Clear the current session only
CLEAR SESSION;

-- Clear all sessions (admin operation)
CLEAR SESSIONS ALL;

-- Clear only stale/orphaned sessions (safe for scheduled use)
CLEAR SESSIONS STALE;
```

## What is cleared
| Item | SESSION | SESSIONS ALL | SESSIONS STALE |
|---|---|---|---|
| #temp table disk spill files | Yes | Yes | Yes (if orphaned) |
| Recovery manifests | Yes | Yes | Yes (if orphaned) |
| Encrypted session state | Yes | Yes | Yes (if orphaned) |
| Active connection handles | Yes | Yes | No |
| Other sessions' data | No | Yes | Yes (orphaned only) |

## Notes
- `CLEAR SESSION` is automatically called at the end of a successful script run. Call it explicitly after a recoverable error to free disk space.
- `CLEAR SESSIONS STALE` is safe to run from a maintenance job. It removes data from sessions that have no active process, leaving running sessions untouched.
- `CLEAR SESSIONS ALL` requires elevated permissions and should be used carefully in multi-user Orchestrator deployments.
- See: SCHEDULE, TRANSACTION

References:
- [Statements](../README.md)


## References

- [Statements](../README.md)

## Examples

```sql
CLEAR ALL;
CLEAR TABLES;
```
