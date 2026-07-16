# STRING_ESCAPE
Escapes special characters in a string for safe embedding in a target format.

**Category:** String

## Syntax
```sql
STRING_ESCAPE(text, type)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `text` | `STRING` | The string to escape |
| `type` | `STRING` | The target format — see Accepted Values |

## Returns
`STRING` — The input string with special characters escaped for the specified format.

## Accepted Values
| `type` | Description |
| :--- | :--- |
| `'json'` | Escapes `"`, `\`, and control characters (U+0000–U+001F) for embedding in JSON strings |

## Example
```sql
SELECT STRING_ESCAPE('Line1\nLine2', 'json');  -- → 'Line1\\nLine2'
SELECT STRING_ESCAPE(notes, 'json') AS safe_notes FROM #records;

-- Build a JSON string manually
SELECT '{"message": "' + STRING_ESCAPE(body, 'json') + '"}' FROM #messages;
```

## See Also
- [Standard Library — §3.6 Translation & Escaping](../../../guides/getting-started.md#36-translation--escaping)
- Related: [`QUOTENAME`](quotename.md), [`JSON_MODIFY`](../json-xml/json_modify.md)
