# GET_JOB_STATE
Returns the saved state value for the current script or job execution context.

**Category:** System

## Syntax
```sql
GET_JOB_STATE(key)
```

## Returns
`STRING` — The saved state value, or `NULL` if not set.

## Remarks
- If running as a scheduled orchestrator job, state is retrieved from the orchestrator store.
- If running from the CLI, state falls back to a local JSON `.etlstate` file next to the script.
- Used in conjunction with `SET_JOB_STATE` for incremental watermarking.

## Example
```sql
DECLARE @last_watermark VARCHAR = COALESCE(GET_JOB_STATE('last_loaded_id'), '0');

SELECT * INTO #staging 
FROM source.orders 
WHERE order_id > @last_watermark;
```

References:
- [Standard Library](../../../../../Docs/Reference/Standard_Library.md#8-system--identity-functions)
- Related: [`SET_JOB_STATE`](SET_JOB_STATE.md)
