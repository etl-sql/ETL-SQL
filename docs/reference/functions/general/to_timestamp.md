# TO_TIMESTAMP
Converts a Unix epoch timestamp (number of seconds since `1970-01-01 00:00:00 UTC`) to a standard date/time representation.

**Category:** Date

## Syntax
```sql
TO_TIMESTAMP(seconds)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `seconds` | `NUMERIC` | The number of seconds elapsed since the Unix epoch (supports decimal fractions for milliseconds) |

## Returns
`DATETIME` — The corresponding datetime representation.

## Example
```sql
SELECT TO_TIMESTAMP(0);                     -- → 1970-01-01 00:00:00.000
SELECT TO_TIMESTAMP(1779974400.123);        -- → 2026-05-28 13:20:00.123
```

## See Also
- [Standard Library — §4. Date & Time Functions](../../../guides/getting-started.md#4-date--time-functions)
- Related: [`EXTRACT`](../../../guides/getting-started.md#extract) (with `EPOCH` field), [`DATEADD`](../datetime/dateadd.md)
