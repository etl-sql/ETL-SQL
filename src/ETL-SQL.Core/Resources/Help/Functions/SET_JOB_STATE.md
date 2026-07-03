# SET_JOB_STATE
Sets a saved state value for the current script or job execution context.

**Category:** System

## Syntax
```sql
SELECT SET_JOB_STATE(key, value);
```

SET_JOB_STATE is a function, not a statement — invoke it through `SELECT` (or any expression
position). A bare `SET_JOB_STATE(...);` line is a syntax error.

## Parameters
- **key** (`STRING`) — a name you choose for this piece of state (e.g. `'last_loaded_id'`,
  `'last_backup_status'`). Keys are free-form: they are not validated against any list. Each key is
  scoped to the current context — the orchestrator job name when running as a scheduled job, or the
  script file when running from the CLI — so two different jobs can use the same key without
  colliding.
- **value** (`STRING`) — the value to store. Non-string values are converted with `ToString()`;
  `CAST(... AS VARCHAR)` explicitly if you care about the format.

## Returns
`STRING` — The assigned state value.

## Remarks
- State updates are buffered during execution and only committed upon successful top-level script
  completion — a failed script leaves previously saved state untouched (atomic watermarks).
- If running as a scheduled orchestrator job, state is committed to the orchestrator store keyed by
  the job name.
- If running from the CLI, state falls back to a local JSON `.etlstate` file next to the script.
- Read values back with `GET_JOB_STATE(key)` — in the same script on a later run, or from a
  different monitoring script running under the same job/script context.
- Used in conjunction with `GET_JOB_STATE` for incremental watermarking.

## Example
```sql
DECLARE @max_id INT = (SELECT MAX(order_id) FROM #staging);

IF @max_id IS NOT NULL
BEGIN
    SELECT SET_JOB_STATE('last_loaded_id', CAST(@max_id AS VARCHAR));
END
```

References:
- [Standard Library](../../../../../Docs/Reference/Standard_Library.md#8-system--identity-functions)
- Related: [`GET_JOB_STATE`](GET_JOB_STATE.md)
