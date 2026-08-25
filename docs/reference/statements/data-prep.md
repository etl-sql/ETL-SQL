# Data Prep Helpers

High-level declarative data preparation statements that generate inspectable in-memory `#temp` tables for common ETL, time-series, reconciliation, and BI reporting workflows.

---

## `GENERATE CALENDAR` {#generate-calendar}

Generates a comprehensive date dimension table populated with fiscal, calendar, weekday, and ISO date attributes:

## Syntax

```sql
GENERATE CALENDAR FROM <start_date> TO <end_date> INTO <destination_table>;
```

### Generated Table Schema

| Column | Type | Example | Description |
| :--- | :--- | :--- | :--- |
| `Date` | `DATE` | `2026-08-16` | Normalized ISO date |
| `DateKey` | `INT` | `20260816` | Numeric integer surrogate key (`YYYYMMDD`) |
| `Year` | `INT` | `2026` | Calendar year |
| `Quarter` | `INT` | `3` | Calendar quarter (1–4) |
| `YearQuarter` | `VARCHAR` | `'2026-Q3'` | Formatted year and quarter |
| `Month` | `INT` | `8` | Calendar month (1–12) |
| `MonthName` | `VARCHAR` | `'August'` | Full month name |
| `MonthShortName` | `VARCHAR` | `'Aug'` | 3-letter month abbreviation |
| `YearMonth` | `VARCHAR` | `'2026-08'` | Year and month string |
| `Day` | `INT` | `16` | Day of the month (1–31) |
| `DayOfWeek` | `INT` | `7` | Day of week (1=Monday, 7=Sunday) |
| `DayName` | `VARCHAR` | `'Sunday'` | Full day name |
| `DayShortName` | `VARCHAR` | `'Sun'` | 3-letter day abbreviation |
| `DayOfYear` | `INT` | `228` | Day of the year (1–366) |
| `ISOWeek` | `INT` | `33` | ISO 8601 week number |
| `IsWeekend` | `BIT` | `1` | `1` if Saturday or Sunday, else `0` |
| `IsWeekday` | `BIT` | `0` | `1` if Monday–Friday, else `0` |
| `IsMonthStart` | `BIT` | `0` | `1` on the 1st of each month |
| `IsMonthEnd` | `BIT` | `0` | `1` on the last day of each month |
| `FiscalYear` | `INT` | `2026` | Fiscal year |
| `FiscalQuarter` | `INT` | `3` | Fiscal quarter |
| `RelativeDays` | `INT` | `0` | Difference in days relative to current execution date |

### Example
```sql
GENERATE CALENDAR FROM '2026-01-01' TO '2026-12-31' INTO #calendar;

-- Align sparse sales with calendar dates to guarantee uninterrupted time-series
SELECT c.Date, c.DayName, c.IsWeekend, COALESCE(SUM(s.Amount), 0.0) AS DailyRevenue
FROM #calendar AS c
LEFT JOIN #sales AS s ON c.Date = s.OrderDate
GROUP BY c.Date, c.DayName, c.IsWeekend
ORDER BY c.Date;
```

---

## `TRANSFORM ... USING FILL_DATES` {#transform}

Fills missing date gaps across partitioned time-series datasets. Generated rows preserve grouping keys and fill metric columns with `GAPS_FILL` (default `0`).

```sql
TRANSFORM <target_table>
FROM <source_table>
USING FILL_DATES (
  DATE_COL = 'column_name',
  GAPS_FILL = 0,
  BY_GROUP = 'partition_column'
);
```

### Example
```sql
-- #daily_sales has gaps where no sales occurred on holidays or weekends
TRANSFORM #daily_sales_filled
FROM #daily_sales
USING FILL_DATES (
  DATE_COL = 'OrderDate',
  GAPS_FILL = 0,
  BY_GROUP = 'Region'
);

SELECT Region, OrderDate, Amount FROM #daily_sales_filled ORDER BY Region, OrderDate;
```

---

## `COMPARE DATASETS` {#compare-datasets}

Compares two datasets by primary key, identifying created, modified, and deleted records:

```sql
COMPARE DATASETS <source_table> WITH <baseline_table>
KEY (KeyColumn1, KeyColumn2)
EXCLUDE (IgnoredColumn1)
INTO <diff_table>;
```

### Generated Diff Schema

- Key columns from source/baseline
- `_change_type` (`'INSERT'`, `'UPDATE'`, `'DELETE'`)
- `_changed_columns` (comma-separated list of altered column names)
- `<column>_old` and `<column>_new` comparison attributes for all non-excluded columns

### Example: Daily Reconciliation & Audit Capture
```sql
-- Compare current snapshot with previous day snapshot
COMPARE DATASETS #today_customers WITH #yesterday_customers
KEY (CustomerId)
EXCLUDE (LastSeenAt, IngestedAt)
INTO #customer_diff;

-- 1. Route newly created customers
SELECT CustomerId, Email_new AS Email, Status_new AS Status
INTO #new_signups
FROM #customer_diff
WHERE _change_type = 'INSERT';

-- 2. Audit modified accounts
SELECT CustomerId, _changed_columns, Status_old, Status_new
INTO #status_changes
FROM #customer_diff
WHERE _change_type = 'UPDATE' AND _changed_columns LIKE '%Status%';
```

---

## References & Related Recipes

- [Statement Reference](README.md)
- [ASOF JOIN](query-syntax/asof-join.md)
- [PIVOT / UNPIVOT](query-syntax/pivot.md)
- [ETL Cookbook: Cross-Platform Reconciliation](../../cookbooks/etl/cross-platform-reconciliation.md)
- [ETL Cookbook: Time Series Gap Filling](../../cookbooks/etl/time-series-gap-filling.md)
- [Syntax Index](../../syntax-index.md)
