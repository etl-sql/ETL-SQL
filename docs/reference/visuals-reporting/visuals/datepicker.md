# DATEPICKER

An interactive date selection control supporting single-date and date-range modes. The selected date or date range is bound to script variables via ACTIONS.

## Syntax

```sql
CREATE VISUAL VisualName AS DATEPICKER (
  SOURCE = #dataset,
  OPTIONS (
    MODE           = SINGLE|RANGE,
    DEFAULT        = 'YYYY-MM-DD',
    MIN            = 'YYYY-MM-DD' | SOURCE_MIN(col),
    MAX            = 'YYYY-MM-DD' | SOURCE_MAX(col) | 'TODAY',
    FORMAT         = 'YYYY-MM-DD',
    DISABLED_DATES = ('2026-12-25', '2026-01-01'),
    DISABLED_DAYS  = (SAT, SUN),
    WEEK_START     = SUN|MON,
    DISPLAY        = INLINE|DROPDOWN
  ),
  ACTIONS (
    ON_CHANGE = SET_PARAMETER(@start, @end, value)
  )
);
```

## Mappings

Filter controls do not use a `MAPPINGS` clause unless binding dynamic bounds from a `SOURCE` table.

## Options

- **MODE = SINGLE|RANGE** — Selects single date or dual date range mode (default SINGLE).
- **DEFAULT = 'YYYY-MM-DD'** — Initial date or date pair (default current date).
- **MIN = 'YYYY-MM-DD' | SOURCE_MIN(col)** — Earliest selectable date, static string or dynamic column minimum.
- **MAX = 'YYYY-MM-DD' | SOURCE_MAX(col) | 'TODAY'** — Latest selectable date, static string or dynamic column maximum.
- **FORMAT = 'format-pattern'** — Display date formatting hint (e.g. `'YYYY-MM-DD'`).
- **DISABLED_DATES = ('YYYY-MM-DD', ...)** — Explicit calendar dates that cannot be selected.
- **DISABLED_DAYS = (SAT, SUN)** — Days of the week to disable (e.g. weekend blackout).
- **WEEK_START = SUN|MON** — First day of the calendar week (default SUN).
- **DISPLAY = INLINE|DROPDOWN** — Whether the date picker is embedded inline or opened as a dropdown (default DROPDOWN).

## Actions

- **ON_CHANGE = SET_PARAMETER(@variable, value)** — Fires when the date changes in SINGLE mode.
- **ON_CHANGE = SET_PARAMETER(@start, @end, value)** — Fires when either date changes in RANGE mode, binding start and end values.

## Examples

```sql
DECLARE @start_date DATE = DATEADD(DAY, -30, GETDATE());
DECLARE @end_date   DATE = GETDATE();

CREATE VISUAL DateRangeFilter AS DATEPICKER (
  OPTIONS (
    MODE          = RANGE,
    DISABLED_DAYS = (SAT, SUN),
    WEEK_START    = MON,
    DISPLAY       = DROPDOWN
  ),
  ACTIONS (ON_CHANGE = SET_PARAMETER(@start_date, @end_date, value))
);

CREATE VISUAL SalesTrend AS LINE (
  SOURCE   = (SELECT sale_date, SUM(amount) AS total FROM #sales
              WHERE sale_date BETWEEN @start_date AND @end_date
              GROUP BY sale_date),
  MAPPINGS (X = sale_date, Y = total)
);
```

## References

- [RELDATEPICKER](reldatepicker.md)
- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
