# REPLICATE
Repeats a string a specified number of times.

**Category:** String

## Syntax
```sql
REPLICATE(string, count)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `string` | `STRING` | The string to repeat |
| `count` | `INT` | Number of repetitions |

## Returns
`STRING` — The input `string` concatenated `count` times. Returns an empty string if `count` ≤ 0.

## Example
```sql
SELECT REPLICATE('ab', 3);                       -- → 'ababab'
SELECT REPLICATE('0', 5 - LEN(id)) + id         -- zero-pad an ID
  AS padded_id FROM #items;
```

## See Also
- [Standard Library — §3.4 Formatting & Padding](../../../../../Docs/Reference/Standard_Library.md#34-formatting--padding)
- Related: [`SPACE`](SPACE.md), [`STR`](STR.md)
