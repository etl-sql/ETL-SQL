# CONCAT_WS
Concatenates strings with a separator, automatically skipping NULL values.

**Category:** String

## Syntax
```sql
CONCAT_WS(separator, string1, string2, ...)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `separator` | `STRING` | String inserted between each non-NULL value |
| `string1` | `STRING` | First value |
| `string2` | `STRING` | Second value |
| `...` | `STRING` | Additional values (variadic) |

## Returns
`STRING` — All non-NULL arguments joined with the separator between them.

## Remarks
- `NULL` arguments are silently skipped — no separator is inserted adjacent to a NULL.
- If all arguments are `NULL`, returns `NULL`.

## Example
```sql
SELECT CONCAT_WS(', ', 'Alice', NULL, 'Bob');  -- → 'Alice, Bob'
SELECT CONCAT_WS(' ', first_name, middle_name, last_name) AS full_name
  FROM #people;
```

## See Also
- [Standard Library — §3.3 Concatenation & Splitting](../../../guides/getting-started.md#33-concatenation--splitting)
- Related: [`CONCAT`](concat.md), [`STRING_AGG`](../aggregate/string_agg.md)
