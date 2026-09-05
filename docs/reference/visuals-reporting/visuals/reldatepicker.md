# RELDATEPICKER

A filter control for selecting relative or absolute date values with preset quick-picks and validation. Supports past, future, and fiscal calendar expressions.

## Syntax

```sql
CREATE VISUAL VisualName AS RELDATEPICKER (
  OPTIONS (
    MODE              = SINGLE|RANGE,
    DEFAULT           = 'D-7',
    QUICK_PICKS       = ('Label' = 'Expr', ...),
    FISCAL_YEAR_START = month_number,
    MIN               = 'YYYY-MM-DD',
    MAX               = 'YYYY-MM-DD'
  ),
  ACTIONS (
    ON_CHANGE = SET_PARAMETER(@start, @end, value)
  )
);
```

## Mappings

Filter controls do not use a `MAPPINGS` clause. Configure choices and behaviour using `OPTIONS` and `ACTIONS`.

## Options

- **MODE = SINGLE|RANGE** — Single relative date or dual relative date range mode (default SINGLE).
- **DEFAULT = 'expr'** — Initial relative expression or ISO date string (default `'D-7'`).
- **QUICK_PICKS = ('Label' = 'Expr', ...)** — Custom quick-pick preset buttons.
- **FISCAL_YEAR_START = month** — Calendar month (1-12) where the fiscal year begins (default 1).
- **MIN = 'YYYY-MM-DD'** — Earliest selectable calendar date.
- **MAX = 'YYYY-MM-DD'** — Latest selectable calendar date.

## Actions

- **ON_CHANGE = SET_PARAMETER(@variable, value)** — Fires when the expression changes in SINGLE mode.
- **ON_CHANGE = SET_PARAMETER(@start, @end, value)** — Fires in RANGE mode, binding start and end parameters. Suppresses emission if either expression is invalid.

## Relative Date Syntax

The control accepts relative date expressions and absolute ISO dates (`YYYY-MM-DD`):

- **D / D-n / D+n** — Day anchor (e.g. `D-0` = today, `D-1` = yesterday, `D+30` = 30 days ahead).
- **W / WS / WE** — Current week start and end.
- **M / MS / ME** — Current month start and end (e.g. `M-1` = start of last month, `ME-1` = end of last month).
- **FQ / FQS / FQE** — Fiscal quarter start and end (e.g. `FQ-1` = previous fiscal quarter).
- **FY / FYS / FYE** — Fiscal year start and end (e.g. `FY-1` = previous fiscal year).
- **Y / YS / YE** — Calendar year start and end.
- **N / N-2H / N+30I** — Exact current timestamp with hour/minute offsets.

## Examples

```sql
DECLARE @start_rel VARCHAR = 'M-1';
DECLARE @end_rel   VARCHAR = 'D-0';

CREATE VISUAL OrdersPeriod AS RELDATEPICKER (
  OPTIONS (
    MODE              = RANGE,
    FISCAL_YEAR_START = 10,
    QUICK_PICKS       = (
      'This Quarter' = 'FQS',
      'Last Quarter' = 'FQ-1',
      'Year to Date' = 'FYS'
    )
  ),
  ACTIONS (ON_CHANGE = SET_PARAMETER(@start_rel, @end_rel, value))
);

SELECT * FROM orders
WHERE order_date BETWEEN RELDATE(@start_rel, 10) AND RELDATE(@end_rel, 10);
```

## References

- [DATEPICKER](datepicker.md)
- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
