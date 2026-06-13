# DATALENGTH
Returns the number of bytes used to represent an expression.

**Category:** String / System

## Syntax
`sql
DATALENGTH(expression)
`

## Returns
INT â€” The byte size of the value. Returns NULL if input is NULL.

## Example
`sql
SELECT DATALENGTH('hello'); -- â†’ 5 (ASCII/UTF8)
`

## See Also
- Related: [LEN](LEN.md), [CHAR_LENGTH](CHAR_LENGTH.md)