# SET_JOB_STATE

Sets a saved state value for the current script or job execution context.

## Syntax

```sql
SELECT SET_JOB_STATE(key, value);
```

SET_JOB_STATE is a function, not a statement. Invoke it through `SELECT` or another expression position.
A bare `SET_JOB_STATE(...);` line is a syntax error.

## Parameters

- **key** - State key to write, such as `'last_loaded_id'` or `'last_backup_status'`.
- **value** - Value to store. Non-string values are converted to strings; use `CAST(... AS VARCHAR)` when the format matters.

## Returns

Returns the assigned state value as `STRING`.

## Null Behavior

If **key** is `NULL`, no state value is written and the result is `NULL`.

## Remarks

- Keys are scoped to the current context: the orchestrator job name for scheduled jobs, or the script file for CLI runs.
- State updates are buffered during execution and only committed upon successful top-level script
  completion. A failed script leaves previously saved state untouched, preserving atomic watermarks.
- If running as a scheduled orchestrator job, state is committed to the orchestrator store keyed by
  the job name.
- If running from the CLI, state falls back to a local JSON `.etlstate` file next to the script.
- Read values back with `GET_JOB_STATE(key)` in the same script on a later run, or from a
  different monitoring script running under the same job/script context.
- To inspect saved state across jobs (all keys, any job) use `SHOW JOB STATE ['<job>'] [INTO #t]`.
- Used in conjunction with `GET_JOB_STATE` for incremental watermarking.

## Examples

```sql
DECLARE @max_id INT = (SELECT MAX(order_id) FROM #staging);

IF @max_id IS NOT NULL
BEGIN
    SELECT SET_JOB_STATE('last_loaded_id', CAST(@max_id AS VARCHAR));
END
```

## References

- [Functions](../README.md)
- [GET_JOB_STATE](get_job_state.md)
