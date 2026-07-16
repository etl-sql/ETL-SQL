# QUOTENAME
Returns a string wrapped in delimiters to make it a valid identifier.

**Category:** String

## Syntax
```sql
QUOTENAME(string, [delimiter])
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `string` | `STRING` | The identifier to delimit |
| `delimiter` | `STRING` | Optional: delimiting character — `[` (default), `"`, or `'` |

## Returns
`STRING` — The identifier wrapped in the specified delimiter pair. Embedded delimiters inside the string are escaped by doubling them.

## Example
```sql
SELECT QUOTENAME('my column');        -- → '[my column]'
SELECT QUOTENAME('my column', '"');   -- → '"my column"'
SELECT QUOTENAME('it''s here', ''''); -- → '''it''''s here'''
```

## See Also
- [Standard Library — §3.4 Formatting & Padding](../../../guides/getting-started.md#34-formatting--padding)
- Related: [`STRING_ESCAPE`](string_escape.md)
