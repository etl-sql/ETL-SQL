# REVERSE
Returns a string with characters in reverse order.

**Category:** String

## Syntax
```sql
REVERSE(string)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `string` | `STRING` | The string to reverse |

## Returns
`STRING` — The input string with character order reversed.

## Example
```sql
SELECT REVERSE('hello');          -- → 'olleh'
SELECT REVERSE('racecar');        -- → 'racecar'  (palindrome check)
```

## Remarks
- Operates on Unicode code points; surrogate pairs are kept intact.

## See Also
- [Standard Library — §3. String Functions](../../../guides/getting-started.md#3-string-functions)
