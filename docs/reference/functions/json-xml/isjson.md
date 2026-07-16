# ISJSON
Returns 1 if the string is valid JSON, 0 otherwise.

**Category:** JSON

## Syntax
```sql
ISJSON(string)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `string` | `STRING` | The value to test |

## Returns
`BIT` — `1` if valid JSON; `0` otherwise.

## Example
```sql
SELECT ISJSON('{"id": 1}');          -- → 1
SELECT ISJSON('[1, 2, 3]');          -- → 1
SELECT ISJSON('not json');           -- → 0

SELECT * FROM #raw WHERE ISJSON(payload) = 0;  -- find invalid JSON rows
```

## See Also
- [Standard Library — §11. JSON Functions](../../../guides/getting-started.md#11-json-functions)
- Related: [`JSON_VALUE`](../json-xml/json_value.md), [`TRY_CAST`](../conversion/try_cast.md)
