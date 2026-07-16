# EXTRACT
Extracts a specified date part component from a date or time expression.

**Category:** Date

## Syntax
```sql
EXTRACT(field FROM source)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `field` | `KEYWORD` | The part to extract (e.g., `YEAR`, `EPOCH`) |
| `source` | `DATE` / `DATETIME` | The date or time expression to extract from |

## Returns
`NUMERIC` — The value of the extracted component (e.g., an integer for components like `YEAR`, or a decimal value for `EPOCH` representing total seconds).

## Accepted Values for `field`
`YEAR`, `MONTH`, `DAY`, `HOUR`, `MINUTE`, `SECOND`, `MILLISECOND`, `DOW`, `DOY`, `EPOCH`, `QUARTER`, `WEEK`, `ISODOW`, `DECADE`, `CENTURY`, `MILLENNIUM`

## Example
```sql
SELECT EXTRACT(YEAR FROM '2026-05-28');             -- → 2026
SELECT EXTRACT(EPOCH FROM '2026-05-28 13:20:00');   -- → 1779974400
```

## See Also
- [Standard Library — §4. Date & Time Functions](../../../guides/getting-started.md#4-date--time-functions)
- Related: [`DATEPART`](datepart.md), [`DATE_PART`](../general/date_part.md)
