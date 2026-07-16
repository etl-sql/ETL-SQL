# GENERATE
Creates synthetic or mock data rows and loads them into a #temp table. Useful for testing, seeding, and load simulation.

## Syntax
```sql
GENERATE <n> ROWS INTO #table AS (
  <column> <type> <generator> [, ...]
);
```

## Generators
| Generator | Description |
|---|---|
| `SEQUENCE` | Auto-incrementing integer starting at 1 |
| `SEQUENCE(<start>)` | Auto-incrementing integer starting at `<start>` |
| `RANDOM(<len>)` | Random alphanumeric string of length `<len>` |
| `RANDOM(<min>, <max>)` | Random number in range [min, max] |
| `RANDOM_DATE(<from>, <to>)` | Random date between two dates |
| `VALUE(<literal>)` | Constant value for every row |
| `CHOICE('a','b','c')` | Random pick from a fixed list |

## Example
```sql
GENERATE 10000 ROWS INTO #mock AS (
  id          INT     SEQUENCE,
  name        STRING  RANDOM(12),
  region      STRING  CHOICE('North', 'South', 'East', 'West'),
  sale_amount DECIMAL RANDOM(10.00, 9999.99),
  sale_date   DATE    RANDOM_DATE('2024-01-01', '2024-12-31'),
  active      BOOL    VALUE(1)
);
```

## Notes
- Row count can be a variable: `GENERATE @batchSize ROWS INTO #data AS (...)`.
- Generated tables behave identically to any other #temp table.
- See: CREATE TABLE, INSERT, DECLARE

References:
- [Statements](../README.md)
