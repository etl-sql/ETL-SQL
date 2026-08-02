# SET MAX_GENERATE_ROWS
Sets the maximum number of rows that `GENERATE` is allowed to produce.

## Syntax
```sql
SET MAX_GENERATE_ROWS = <n>;
```

## Parameters
- **n** — Maximum rows. Default: 1,000,000.

## Example
```sql
-- Allow GENERATE to produce more rows for load testing
SET MAX_GENERATE_ROWS = 5000000;

GENERATE 2000000 ROWS INTO #test_data AS (
    id = 'SEQUENCE(1,1)',
    name = 'RANDOM(12)',
    amount = 'RANDOM_DECIMAL(0,10000)'
);
```

## Notes
- This is a safety limit to prevent accidental runaway row generation.
- Default: 1,000,000.

## References
- [SET Commands](README.md)
