# SET_JOB_STATE
Sets the saved state value for the current script or job execution context.

**Category:** System

## Syntax
```sql
SET_JOB_STATE(key, value)
```

## Returns
`STRING` — The assigned state value.

## Remarks
- State updates are buffered during execution and only committed upon successful top-level script completion.
- If running as a scheduled orchestrator job, state is committed to the orchestrator store.
- If running from the CLI, state falls back to a local JSON `.etlstate` file next to the script.
- Used in conjunction with `GET_JOB_STATE` for incremental watermarking.

## Example
```sql
DECLARE @max_id INT = (SELECT MAX(order_id) FROM #staging);

IF @max_id IS NOT NULL
BEGIN
    SET_JOB_STATE('last_loaded_id', CAST(@max_id AS VARCHAR));
END
```

References:
- [Standard Library](../../../../../Docs/Reference/Standard_Library.md#8-system--identity-functions)
- Related: [`GET_JOB_STATE`](GET_JOB_STATE.md)
