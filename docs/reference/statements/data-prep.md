# Data Prep Helpers

Built-in helpers create inspectable `#temp` tables for common reporting and ETL prep work.

## GENERATE CALENDAR

```sql
GENERATE CALENDAR FROM <start_date> TO <end_date> INTO #calendar;
```

Creates a comprehensive date dimension table with `DateKey`, `Date`, `FullDateISO`, `Year`, `Quarter`, `YearQuarter`, `Month`, `MonthName`, `MonthShortName`, `YearMonth`, `Day`, `DayOfWeek`, `DayName`, `DayShortName`, `DayOfYear`, `ISOWeek`, `IsWeekend`, `IsWeekday`, `IsMonthStart`, `IsMonthEnd`, `IsQuarterStart`, `IsQuarterEnd`, `IsYearStart`, `IsYearEnd`, `FiscalYear`, `FiscalQuarter`, and `RelativeDays`.

```sql
GENERATE CALENDAR FROM '2026-01-01' TO '2026-01-31' INTO #calendar;
SELECT * FROM #calendar;
```

## FILL_DATES

```sql
FILL_DATES(
  #source,
  DATE_COL = 'date_column',
  GAPS_FILL = <value>,
  BY_GROUP = 'group_column[, group_column...]'
) INTO #filled;
```

Fills missing daily rows between the minimum and maximum date in each group. Existing rows are copied unchanged. Generated rows keep the date and group values; all other columns receive `GAPS_FILL` (default `0`).

```sql
FILL_DATES(
  #daily_sales,
  DATE_COL = 'OrderDate',
  GAPS_FILL = 0,
  BY_GROUP = 'Region'
) INTO #daily_sales_filled;
```

## COMPARE DATASETS

```sql
COMPARE DATASETS #source WITH #baseline
KEY (Id [, ...])
[EXCLUDE (IgnoredColumn [, ...])]
INTO #diff;
```

Compares two datasets by key and writes only inserted, updated, and deleted rows to `#diff`. Output includes key columns, `_change_type`, `_changed_columns`, and `<column>_old` / `<column>_new` pairs for compared attributes.

```sql
COMPARE DATASETS #today WITH #yesterday
KEY (CustomerId)
EXCLUDE (LastSeenAt)
INTO #customer_delta;
```

## References

- [Statement Reference](README.md)
- [Syntax Index](../../syntax-index.md)
