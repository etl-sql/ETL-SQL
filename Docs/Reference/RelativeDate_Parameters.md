# Relative Date Parameters & Subscription Parameters

This document specifies the `RELDATE` type, the `LIST` type, and the week-start configuration that together enable time-aware, reusable subscription parameters.

---

## Overview

Subscriptions need parameters that mean "yesterday" or "last month" without hardcoding a date. The `RELDATE` type lets a report writer express a time offset once in a `DECLARE` statement; the engine resolves it to an actual datetime at execution time — whether that execution comes from a subscription, a scheduled job, or a manual run.

```sql
DECLARE @start RELDATE = D-1;   -- yesterday at midnight
DECLARE @end   RELDATE = D;     -- today at midnight
```

When the subscription fires at 6 AM on April 17, `@start` = `2026-04-16 00:00:00`, `@end` = `2026-04-17 00:00:00`. Every run resolves fresh.

---

## The RELDATE Type

### Anchors

Each anchor snaps to the **start or end of a calendar period** in the server's local time zone (see [Time Zone](#time-zone)).

| Anchor | Resolves to | Arithmetic unit |
| :--- | :--- | :--- |
| `D` | Today at midnight | Days |
| `W` / `WS` | Start of current week (configurable day, default Monday) | Weeks |
| `WE` | Last day of current week (day before next week start) | Weeks |
| `M` / `MS` | 1st of current month at midnight | Months |
| `ME` | Last day of current month at midnight | Months |
| `Q` / `QS` | 1st of current quarter at midnight | Quarters |
| `QE` | Last day of current quarter at midnight | Quarters |
| `Y` / `YS` | January 1 of current year at midnight | Years |
| `YE` | December 31 of current year at midnight | Years |
| `N` | Exact current local datetime (floating) | `H` hours, `I` minutes, `S` seconds |
| `NU` | Exact current UTC datetime (floating) | `H` hours, `I` minutes, `S` seconds |

`W` and `WS` are synonyms; `M` and `MS` are synonyms; `Q` and `QS` are synonyms; `Y` and `YS` are synonyms. The explicit `S` suffix is available for readability.

### Arithmetic

Append `+n` or `-n` to shift by *n* units:

```
D-1        yesterday at midnight
D+1        tomorrow at midnight
W-1        start of last week
ME-1       last day of last month
QE-2       last day of Q2 quarters ago
Y-1        January 1 of last year
YE-1       December 31 of last year
N-2H       exactly 2 hours ago
N-30I      exactly 30 minutes ago
N+1H       1 hour from now
```

#### The period-shift rule (important)

Arithmetic **shifts the period anchor**, then applies the start/end modifier. It does **not** shift the resolved date.

- `ME-1` = last day of *(current month − 1)* = last day of last month ✓  
- It does **not** mean last day of this month minus 1 day ✗

This applies consistently to all anchors: `QE-1` = last day of last quarter, `YE-2` = December 31 two years ago.

#### `N` and `NU` arithmetic units

For the floating-point anchors, the arithmetic unit is specified inline:

| Modifier | Meaning |
| :--- | :--- |
| `nH` | n hours |
| `nI` | n minutes |
| `nS` | n seconds |

`H`, `I`, and `S` are **not** standalone anchors — they only appear as modifiers on `N` or `NU`. `N-2H` is valid; `H-2` is not.

### Week start day

`W` snaps to the configured start-of-week day. If `WEEK_START_DAY = 'Wednesday'` and today is Thursday April 17:

- `W` = Wednesday April 16 (most recent start-of-week day, including today if today is that day)
- `W-1` = Wednesday April 9 (one full week period back)
- `WE` = Tuesday April 22 (the day before the next week starts)
- `WE-1` = Tuesday April 15

On the start day itself: `W` = today, `W-1` = one week ago. The start day is the first day of the new period.

Configuration (two-tier, script overrides global):

**`appsettings.json`** — applies to all scripts unless overridden:
```json
"Engine": {
    "StartOfWeek": "Monday"
}
```

**`SET WEEK_START_DAY`** — applies from that point in the script for the duration of the session:
```sql
SET WEEK_START_DAY = 'Sunday';
```

Valid values: `Monday`, `Tuesday`, `Wednesday`, `Thursday`, `Friday`, `Saturday`, `Sunday` (case-insensitive). Any other value is a runtime error. Default: `Monday` (ISO 8601).

### Time zone

All period anchors (`D`, `W`, `M`, `Q`, `Y` and their variants) resolve in **server local time** by default. `N` resolves in server local time; `NU` resolves in UTC.

To evaluate relative date snapping under a specific timezone calendar, append a timezone suffix to the expression (e.g., `D-1 EST`, `D America/New_York`, `N-2H UTC`). When a suffix is provided:
1. The calendar day snap is determined based on the target timezone's clock.
2. The resolved value is returned as a timezone-aware `DATETIMEOFFSET` type, preserving the correct timezone offset for that date (including DST transitions).

For a complete list of timezone abbreviations and behavior, refer to the [Dates and Times Guide](Dates_and_Times.md).

### Fixed date passthrough

A `RELDATE` parameter also accepts a fixed ISO date string (`2026-01-01` or `2026-01-01 00:00:00`). The resolver distinguishes them by the first character: letter = expression, digit = fixed date. The subscription portal should offer both a relative expression input and a fixed date-picker.

---

## The LIST Type

For multi-value parameters passed to `IN` clauses:

```sql
DECLARE @regions LIST(VARCHAR(200)) INPUT;
```

### Encoding

Values are comma-separated. If a value contains a comma, wrap it in double quotes:

```
North,South,East          -- three values
"North, Central",South    -- two values; first contains a comma
```

### NULL semantics

An empty or absent `LIST` parameter arrives as `NULL`. The report writer is responsible for handling it — typically by short-circuiting the filter:

```sql
WHERE (@regions IS NULL OR Region IN (SELECT value FROM STRING_SPLIT(@regions, ',')))
```

This gives subscribers who leave the field blank an unfiltered result rather than an empty one.

### Subscription portal UI

Render `LIST` fields as a tag/chip input. Each chip is one value; chips containing commas are automatically quoted in the serialized form. A plain textarea with CSV is the fallback for accessibility.

---

## NULL for empty INPUT parameters

Any `INPUT` parameter left blank in the subscription form is passed as `NULL`. This applies to all types: `VARCHAR`, `INT`, `RELDATE`, `LIST`. The report writer decides what NULL means for their query — typically "no filter" but explicitly handled, not assumed.

---

## Subscription portal UI notes

**Quick-pick dropdown** (covers 90% of use cases, no syntax knowledge required):

| Label | Expression |
| :--- | :--- |
| Today | `D` |
| Yesterday | `D-1` |
| Start of this week | `W` |
| Start of last week | `W-1` |
| End of last week | `WE-1` |
| Start of this month | `M` |
| Start of last month | `M-1` |
| End of last month | `ME-1` |
| Start of this quarter | `Q` |
| Start of last quarter | `Q-1` |
| End of last quarter | `QE-1` |
| Start of this year | `Y` |
| Start of last year | `Y-1` |
| End of last year | `YE-1` |
| Now | `N` |

An **Advanced** toggle reveals a text input accepting any valid RELDATE expression. The portal validates the expression at save time and rejects unknown units or malformed syntax.

---

## Resolution context

RELDATE expressions resolve at **execution time** regardless of how the report runs — subscription, manual run, scheduled job, or `dotnet run --project src/ETL-SQL.App -- run`. There is no subscription-only context. A developer can test `DECLARE @start RELDATE = D-1` locally and get the same result the subscription will get when it fires.

The expression string (`D-1`) is what gets stored in the subscription's parameter table, not a resolved date. Each execution resolves it fresh against the clock at that moment.

---

## Examples

```sql
-- Daily sales report: always pull yesterday's complete data
DECLARE @start RELDATE = D-1;
DECLARE @end   RELDATE = D;

SELECT * FROM Sales
WHERE SaleDate >= @start AND SaleDate < @end;
```

```sql
-- Monthly executive summary: last full month
DECLARE @period_start RELDATE = M-1;
DECLARE @period_end   RELDATE = ME-1;

SELECT Region, SUM(Revenue) AS Revenue
FROM Sales
WHERE SaleDate BETWEEN @period_start AND @period_end
GROUP BY Region;
```

```sql
-- Regional filter with optional scope (NULL = all regions)
DECLARE @regions LIST(VARCHAR(200)) INPUT;

SELECT * FROM Sales
WHERE (@regions IS NULL OR Region IN (SELECT value FROM STRING_SPLIT(@regions, ',')));
```

```sql
-- Near-real-time: last 2 hours rolling window
DECLARE @since RELDATE = N-2H;

SELECT * FROM Events WHERE EventTime >= @since;
```

```sql
-- Script using US Sunday-start weeks
SET WEEK_START_DAY = 'Sunday';

DECLARE @week_start RELDATE = W-1;   -- last Sunday
DECLARE @week_end   RELDATE = WE-1;  -- last Saturday
```
