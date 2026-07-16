# STRING_SPLIT
Splits a string by a delimiter and returns the segments as a table.

**Category:** String

## Syntax
```sql
STRING_SPLIT(string, delimiter)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `string` | `STRING` | The source string to split |
| `delimiter` | `STRING` | The separator string |

## Returns
Table with a single `value` column (`STRING`), one row per segment. Empty segments between consecutive delimiters are included.

## Remarks
- Used with `CROSS APPLY` or as a subquery source.
- To filter empty segments: `WHERE value <> ''`.

## Example
```sql
-- Split a comma-separated list into rows
SELECT value AS tag
FROM STRING_SPLIT('red,green,blue', ',');
-- → 'red', 'green', 'blue'

-- Use with CROSS APPLY to expand a column
SELECT o.order_id, t.value AS tag
FROM #orders AS o
CROSS APPLY STRING_SPLIT(o.tags, ',') AS t;
```

## See Also
- [Standard Library — §3.3 Concatenation & Splitting](../../../guides/getting-started.md#33-concatenation--splitting)
- Related: [`SPLIT_PART`](split_part.md), [`REGEXP_SPLIT_TO_TABLE`](../general/regexp_split_to_table.md)
