# WATERMARK

Declarative incremental watermarking syntax attached to table references in `SELECT` queries (`WITH (WATERMARK = ...)`). Automatically retrieves the previous high-water mark from persistent job state or local session state, injects a filtering predicate into the query execution pipeline, and updates the high-water mark upon successful completion.

```sql
SELECT columns...
[ INTO destination ]
FROM table_reference WITH (
    WATERMARK = 'column_name'
    [, INITIAL = 'initial_value' | number ]
    [, KEY = 'custom_state_key' ]
    [, INCLUSIVE = TRUE | FALSE ]
    [, STRICT = TRUE | FALSE ]
)
[ WHERE additional_conditions... ];
```

- **WATERMARK = 'column_name'** — The column name (date, timestamp, integer ID, or string) on which to track change deltas.
- **INITIAL = 'value'** — Optional initial watermark boundary used when no state entry exists yet (e.g., `'2024-01-01'` or `0`). If omitted, all rows are ingested on first run.
- **KEY = 'state_key'** — Optional identifier key under which to persist and look up the watermark in the job history store or `.etlstate` file. Defaults to `"{TableName}:{ColumnName}"`.
- **INCLUSIVE = TRUE | FALSE** — When `TRUE`, generates `>=` comparison against the watermark instead of `>`. Defaults to `FALSE`.
- **STRICT = TRUE | FALSE** — When `TRUE`, enforces strict `>` comparison (opposite of `INCLUSIVE`). Defaults to `TRUE`.

## Examples

### Initial & Incremental Table Extraction

```sql
-- First execution loads records after 2024-01-01 and saves max UpdatedAt into state 'daily_orders'
-- Subsequent runs automatically filter by UpdatedAt > [last_saved_watermark]
SELECT OrderId, CustomerId, Amount, UpdatedAt
INTO #delta_orders
FROM prod_db.Orders WITH (
    WATERMARK = 'UpdatedAt',
    INITIAL = '2024-01-01',
    KEY = 'daily_orders'
);
```

### Monotonic ID Watermark

```sql
-- Extracts new events with EventId > initial or last processed ID
SELECT EventId, EventName, Payload
INTO #new_events
FROM kafka_stream.Events WITH (
    WATERMARK = 'EventId',
    INITIAL = 1000,
    KEY = 'events_stream'
);
```

## References

- [Statement Reference](../README.md)
- [Query Syntax Reference](README.md)
- [Job Orchestration](../../../administration/orchestration/README.md)
