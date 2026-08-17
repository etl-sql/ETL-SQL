# eng.sessions
Persisted engine sessions and their size, activity, and ownership metadata. Each row represents one saved session that can be resumed with `USE SESSION`. Sessions accumulate `#temp` tables and variables across script runs until explicitly cleared or expired.

## Query

```sql
SELECT * FROM eng.sessions ORDER BY last_modified DESC;
```

## Columns

| Column | Type | Description |
| :--- | :--- | :--- |
| `session_id` | VARCHAR | Unique session identifier. Pass to `USE SESSION` to resume. |
| `user` | VARCHAR | Username that owns the session. |
| `machine` | VARCHAR | Hostname of the machine where the session was last active. |
| `created` | DATETIME | UTC timestamp when the session was first created. |
| `last_modified` | DATETIME | UTC timestamp of the most recent write to the session (variable set, table insert, etc.). |
| `last_script` | VARCHAR | Path or name of the last script executed within the session. Null if no script has been run. |
| `size_mb` | DECIMAL | Approximate disk/memory footprint of the session in megabytes. |
| `temp_tables` | INT | Count of `#temp` tables currently stored in the session. |
| `variables` | INT | Count of named variables currently stored in the session. |
| `is_active` | BIT | `1` if a client process is currently connected; `0` for idle persisted sessions. |

## Examples

```sql
-- Find large idle sessions that may need cleanup
SELECT session_id, user, size_mb, last_modified,
       DATEDIFF(HOUR, last_modified, GETDATE()) AS idle_hours
FROM eng.sessions
WHERE is_active = 0
  AND size_mb > 100
ORDER BY size_mb DESC;
```

```sql
-- Count sessions per user
SELECT user, COUNT(*) AS session_count, SUM(size_mb) AS total_mb
FROM eng.sessions
GROUP BY user
ORDER BY total_mb DESC;
```

## Notes

- Sessions are identified by `session_id`. Use `CLEAR SESSION` inside a session to release its `#temp` tables and variables without destroying the session record.
- Orphaned sessions (where `machine` is no longer active) can be dropped with `DROP SESSION <session_id>`.
- `size_mb` is an estimate; it includes serialized variable values and spilled temp table pages.
- In HA deployments, sessions are stored in the shared Postgres or SQLite state store and can be resumed from any node.

## References

- [Engine Catalog](README.md)
- [CLEAR SESSION](../statements/session-control/clear.md)
