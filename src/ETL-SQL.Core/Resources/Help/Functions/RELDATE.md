# RELDATE
Resolves a relative date expression string into a standard DATETIME value.

**Category:** Date & Time

## Syntax
`sql
RELDATE(expression)
`

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| expression | VARCHAR / STRING | The expression to resolve (e.g., 'D' = today, 'D-1' = yesterday, 'W-1' = start of last week, 'M-1' = start of last month) |

## Returns
DATETIME â€” The resolved datetime. Returns NULL if input is NULL or invalid.

## Example
`sql
SELECT RELDATE('D-7'); -- â†’ Seven days ago
`

## See Also
- Related: [GETDATE](GETDATE.md), [NOW](NOW.md)

References:
- [Standard Library](../../../../../Docs/Reference/Standard_Library.md)
