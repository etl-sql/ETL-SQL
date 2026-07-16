# DATETIMEOFFSETSFROMPARTS
Constructs a DATETIMEOFFSET value from individual date, time, and timezone offset components.

**Category:** Date & Time

## Syntax
`sql
DATETIMEOFFSETSFROMPARTS(year, month, day, hour, minute, second, fractions, hour_offset, minute_offset, precision)
`

## Returns
DATETIMEOFFSET â€” The constructed datetimeoffset value.

## Example
`sql
SELECT DATETIMEOFFSETSFROMPARTS(2026, 6, 12, 14, 30, 0, 0, -5, 0, 0); -- â†’ '2026-06-12 14:30:00 -05:00'
`

## See Also
- Related: [DATETIMEFROMPARTS](DATETIMEFROMPARTS.md), [TIMEFROMPARTS](TIMEFROMPARTS.md)

References:
- [Standard Library](../../../../../Docs/Reference/Standard_Library.md)
