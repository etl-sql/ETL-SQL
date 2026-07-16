# SPACE
Returns a string of N space characters.

**Category:** String

## Syntax
```sql
SPACE(count)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `count` | `INT` | Number of space characters to return |

## Returns
`STRING` — A string containing exactly `count` spaces. Returns an empty string if `count` ≤ 0.

## Example
```sql
SELECT SPACE(5);                             -- → '     '
SELECT name + SPACE(20 - LEN(name)) AS padded FROM #items;
```

## See Also
- [Standard Library — §3.4 Formatting & Padding](../../../guides/getting-started.md#34-formatting--padding)
- Related: [`REPLICATE`](replicate.md), [`STR`](str.md)
