# eng.locks
Active engine and job-throttle lock records for concurrency diagnostics. Rows appear while a lock is held and are removed automatically when the lock is released or the holding session ends.

## Query

```sql
SELECT * FROM eng.locks ORDER BY acquired_at;
```

## Columns

| Column | Type | Description |
| :--- | :--- | :--- |
| `id` | INT | Unique lock record identifier. |
| `lock_type` | VARCHAR | Lock category: `JOB`, `MIGRATION`, `LEASE`, or `THROTTLE`. |
| `lock_scope` | VARCHAR | Resource being locked — a job name, script path, or system singleton key. |
| `process_id` | INT | OS process ID of the holding engine process. |
| `job_name` | VARCHAR | Name of the Orchestrator job that holds the lock, if applicable. Null for engine-internal locks. |
| `machine_name` | VARCHAR | Hostname of the node that acquired the lock. Useful in HA deployments to identify the active node. |
| `acquired_at` | DATETIME | UTC timestamp when the lock was granted. |
| `expires_at` | DATETIME | UTC timestamp when the lock will be forcibly released if not renewed. Null for non-expiring locks. |

## Examples

```sql
-- Check for locks held longer than 10 minutes (possible stuck job)
SELECT lock_scope, lock_type, machine_name, acquired_at,
       DATEDIFF(MINUTE, acquired_at, GETDATE()) AS held_minutes
FROM eng.locks
WHERE DATEDIFF(MINUTE, acquired_at, GETDATE()) > 10
ORDER BY held_minutes DESC;
```

```sql
-- Check whether a specific job is currently running (holds a lock)
SELECT COUNT(*) AS is_running
FROM eng.locks
WHERE lock_type = 'JOB'
  AND lock_scope = 'MonthlySalesLoad';
```

## Notes

- **`JOB`** locks are acquired at job start and released at job end (success or failure).
- **`MIGRATION`** locks are held by the first booting node during schema upgrade and prevent other nodes from running migrations concurrently.
- **`LEASE`** locks protect singleton scheduled work — only one node in an HA cluster can hold a given lease at a time.
- **`THROTTLE`** locks enforce concurrency limits set by `SET MAX_PARALLEL_JOBS`.
- In an HA deployment, `machine_name` identifies which node owns the lock. A lock held by a node that has since gone offline will expire at `expires_at` and be automatically reclaimed.

## References

- [Engine Catalog](README.md)
- [Orchestrator Jobs](../orchestrator-jobs/README.md)
