# RELDATEPICKER

A filter control for selecting relative or absolute date values. Combines a free-text input that accepts relative date expressions (e.g. `D-7`, `M-1`, `Y-1`) with a calendar picker that writes an ISO date, and quick-pick buttons for common offsets.

Use `DATEPICKER` instead when the variable is typed `DATE` or `DATETIME` and only absolute dates are expected.

## Syntax

```sql
CREATE VISUAL MyPicker AS RELDATEPICKER (
    OPTIONS (
        DEFAULT = 'D-7'
    ),
    ACTIONS (
        ON_CHANGE = SET_PARAMETER(@StartDate, VALUE)
    )
);
```

## Options

| Option  | Description                          | Example         |
|---------|--------------------------------------|-----------------|
| DEFAULT | Initial value shown in the text box  | `'D-30'`        |
| MIN     | Earliest selectable calendar date    | `'2020-01-01'`  |
| MAX     | Latest selectable calendar date      | `'2030-12-31'`  |

## Actions

| Action                                         | Description                             |
|------------------------------------------------|-----------------------------------------|
| `ON_CHANGE = SET_PARAMETER(@Name, VALUE)`      | Updates parameter when the value changes |

## Relative Date Syntax

The text box accepts any string your ETL-SQL script reads as a parameter. Relative date expressions follow this pattern:

| Expression | Meaning              |
|------------|----------------------|
| `D-0`      | Today                |
| `D-7`      | 7 days ago           |
| `D-30`     | 30 days ago          |
| `M-1`      | 1 month ago          |
| `M-3`      | 3 months ago         |
| `Y-1`      | 1 year ago           |
| `2026-04-27` | Absolute ISO date  |

The quick-pick buttons (Today, D-1, D-7, D-30, M-1, M-3, Y-1) write directly to the text box and trigger `ON_CHANGE`. Clicking the 📅 button opens the system date picker; selecting a date writes the ISO date (`YYYY-MM-DD`) to the text box.

## Example - Date range with relative defaults

```sql
CREATE VISUAL StartPicker AS RELDATEPICKER (
    OPTIONS ( DEFAULT = 'M-1' ),
    ACTIONS (ON_CHANGE = SET_PARAMETER(@Start, VALUE))
);

CREATE VISUAL EndPicker AS RELDATEPICKER (
    OPTIONS ( DEFAULT = 'D-0' ),
    ACTIONS (ON_CHANGE = SET_PARAMETER(@End, VALUE))
);
```

Your script then resolves `@Start` and `@End` using `RELDATE()`:

```sql
DECLARE @StartDate DATE = RELDATE(@Start);
DECLARE @EndDate   DATE = RELDATE(@End);

SELECT * FROM orders WHERE order_date BETWEEN @StartDate AND @EndDate;
```

References:
- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
