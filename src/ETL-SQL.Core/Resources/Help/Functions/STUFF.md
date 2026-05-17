# STUFF
Deletes a specified number of characters and inserts a replacement string at a given position.

**Category:** String

## Syntax
```sql
STUFF(string, start, length, replacement)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `string` | `STRING` | The source string to modify |
| `start` | `INT` | 1-based position where deletion/insertion begins |
| `length` | `INT` | Number of characters to delete from `start` |
| `replacement` | `STRING` | String to insert at `start` after deletion |

## Returns
`STRING` — The modified string. Returns `NULL` if any argument is `NULL`.

## Remarks
- To insert without deleting, pass `0` as `length`.
- To delete without inserting, pass `''` as `replacement`.

## Example
```sql
SELECT STUFF('Hello World', 6, 0, ' Beautiful');  -- → 'Hello Beautiful World'
SELECT STUFF('Hello World', 7, 5, 'SQL');          -- → 'Hello SQL'
SELECT STUFF(phone, 4, 0, '-') AS formatted FROM #contacts;  -- insert dash
```

## See Also
- [Standard Library — §3. String Functions](../../../../../Docs/Reference/Standard_Library.md#3-string-functions)
- Related: [`REPLACE`](REPLACE.md), [`OVERLAY`](OVERLAY.md), [`SUBSTRING`](SUBSTRING.md)
