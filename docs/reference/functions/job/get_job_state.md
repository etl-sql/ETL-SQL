# GET_JOB_STATE

Returns the saved state value for the current script or job execution context.

## Syntax

```sql
GET_JOB_STATE(key)
```

## Parameters

- **key** - State key saved by `SET_JOB_STATE`. Keys are free-form and scoped to the current context.

## Returns

Returns the saved state value as `STRING`, or `NULL` when the key has not been set.

## Null Behavior

`GET_JOB_STATE(NULL)` returns `NULL`.

## Remarks

- If running as a scheduled orchestrator job, state is retrieved from the orchestrator store.
- If running from the CLI, state falls back to a local JSON `.etlstate` file next to the script.
- To inspect saved state across jobs, query `eng.job_state` and filter by `job_name` when needed.
- Used in conjunction with `SET_JOB_STATE` for incremental watermarking.

## Examples

```sql
DECLARE @last_watermark VARCHAR = COALESCE(GET_JOB_STATE('last_loaded_id'), '0');

SELECT * INTO #staging 
FROM source.orders 
WHERE order_id > @last_watermark;
```

## References

- [Functions](../README.md)
- [SET_JOB_STATE](set_job_state.md)
