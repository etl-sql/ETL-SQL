# SET WEEK_START_DAY
Sets the first day of the week for RELDATE week-boundary expressions (`W`, `W-1`, `WE`, etc.).

## Syntax
```sql
SET WEEK_START_DAY = '<day>';
```

## Parameters
- **day** — The day name. Valid values: `Monday` (default), `Tuesday`, `Wednesday`, `Thursday`, `Friday`, `Saturday`, `Sunday`.

## Example
```sql
-- Use Sunday as the start of the week
SET WEEK_START_DAY = 'Sunday';

-- RELDATE expressions now use Sunday as the week boundary
SELECT * FROM #sales WHERE sale_date >= RELDATE('W');
```

## Notes
- Affects only RELDATE week-boundary expressions, not `DATEPART(WEEKDAY, ...)`.
- See `HELP RELDATE` for full RELDATE expression syntax.
- Default: `Monday`.

## References
- [SET Commands](README.md)
- [Relative Date Parameters](../functions/datetime/reldate.md)
