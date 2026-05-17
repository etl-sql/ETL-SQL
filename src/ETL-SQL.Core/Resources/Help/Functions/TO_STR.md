# TO_STR
Converts any value to its string representation.

**Category:** String

## Syntax
```sql
TO_STR(value)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `value` | `ANY` | The value to convert — numbers, dates, booleans, GUIDs, etc. |

## Returns
`STRING` — The string representation of the value. Returns `NULL` if the input is `NULL`.

## Remarks
- `TO_STR` is a convenience alias for `CAST(value AS STRING)`.
- For locale-aware formatting of numbers and dates, use [`FORMAT`](FORMAT.md) instead.

## Example
```sql
SELECT TO_STR(42);            -- → '42'
SELECT TO_STR(GETDATE());     -- → '2026-05-17 09:00:00'
SELECT 'Order #' + TO_STR(order_id) AS label FROM #orders;
```

## See Also
- [Standard Library — §3.4 Formatting & Padding](../../../../../Docs/Reference/Standard_Library.md#34-formatting--padding)
- Related: [`CAST`](CAST.md), [`FORMAT`](FORMAT.md), [`STR`](STR.md)
