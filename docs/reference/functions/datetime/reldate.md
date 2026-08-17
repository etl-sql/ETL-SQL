# RELDATE
Resolves a relative date expression string into a concrete `DATETIME` value at execution time. Used in `WHERE` clauses, report filter parameters, and `DECLARE RELDATE` variables to express dates like "start of last month" or "7 days ago" without hardcoding dates.

## Syntax

```sql
RELDATE(expression)
```

## Expression Syntax

A relative date expression is a **unit letter** followed by an optional **offset**:

```
<unit>[<sign><n>]
```

### Unit letters

| Letter | Anchors to | Example | Resolves to |
| :--- | :--- | :--- | :--- |
| `D` | Today (start of day) | `D` | Today 00:00:00 |
| `N` | Now (current timestamp) | `N` | Current date and time |
| `W` | Start of current ISO week (Monday) | `W-1` | Start of last week |
| `M` | Start of current month | `M-1` | Start of last month |
| `Q` | Start of current quarter | `Q-1` | Start of last quarter |
| `Y` | Start of current year | `Y` | 1 Jan of current year |
| `H` | Start of current hour | `H-3` | Three hours ago |

### Offset arithmetic

Append `+n` or `-n` to shift by that many units:

| Expression | Meaning |
| :--- | :--- |
| `D` | Today |
| `D-1` | Yesterday |
| `D-7` | 7 days ago |
| `D+1` | Tomorrow |
| `M` | Start of this month |
| `M-1` | Start of last month |
| `M+1` | Start of next month |
| `Y-1` | Start of last year |
| `Q-2` | Start of two quarters ago |
| `H-24` | 24 hours ago (same as `D-1` for hour-resolution) |

## Returns

`DATETIME` — the resolved point in time. For unit anchors like `D`, `W`, `M`, `Q`, `Y`, the time component is `00:00:00`. For `N` and `H`, the time component is precise.

## Null behavior

Returns `NULL` when `expression` is `NULL`, empty, or does not match the expected pattern.

## Examples

```sql
-- Yesterday's orders
SELECT * FROM #orders
WHERE order_date >= RELDATE('D-1')
  AND order_date <  RELDATE('D');

-- Last month's revenue
SELECT SUM(amount) AS last_month_revenue
FROM #sales
WHERE sale_date >= RELDATE('M-1')
  AND sale_date <  RELDATE('M');

-- Rolling 7-day window
SELECT order_date, COUNT(*) AS orders
FROM #orders
WHERE order_date >= RELDATE('D-7')
GROUP BY order_date
ORDER BY order_date;
```

## DECLARE RELDATE variable type

Use `DECLARE @var RELDATE` to hold a relative date expression as a typed variable. The expression is resolved at each point of use, not at declaration time.

```sql
DECLARE @window RELDATE = 'D-30';

SELECT * FROM #events
WHERE event_date >= RELDATE(@window);
```

## Use in report filters (RELDATEPICKER)

In `.rptsql` files, bind a `RELDATEPICKER` control to a `RELDATE` parameter to let report viewers select relative date ranges without needing to know absolute dates.

```sql
DECLARE @startDate RELDATE = 'M-1';

CREATE VISUAL DateFilter AS RELDATEPICKER (
  LABEL = 'Start Date'
)
ACTIONS (ON_CHANGE = SET_PARAMETER(@startDate, value));

CREATE VISUAL RevenueChart AS LINE (
  SOURCE = #revenue,
  ...
)
FILTER (@startDate);
```

## References

- [Functions](../README.md)
- [Relative Date Parameters Guide](../../../guides/feature-guides/report-sql.md)
- [GETDATE](../datetime/getdate.md)
- [NOW](../datetime/now.md)
- [DECLARE](../../variables-parameters/declare.md)
