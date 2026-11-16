# SET DATEFORMAT
Sets how text dates written with slashes, such as `08/01/2026`, are read for the rest of the script.

## Syntax
```sql
SET DATEFORMAT = 'MDY' | 'DMY';
SET DATEFORMAT MDY | DMY;   -- SQL Server form
```

## Parameters
- **MDY** — month first: `08/01/2026` is 1 August 2026. This is the default, as in SQL Server (us_english) and PostgreSQL.
- **DMY** — day first: `08/01/2026` is 8 January 2026.

## Example
```sql
-- A supplier sends day-first dates
SET DATEFORMAT DMY;
SELECT CAST(order_date_text AS DATE) AS order_date FROM supplier.FILE;

-- Back to the default
SET DATEFORMAT MDY;
```

## Notes
- Applies everywhere text is read as a date: `CAST`/`CONVERT`, `DATEPART`, `DATENAME`, `EXTRACT`, `DATEDIFF`, comparisons with dates, and the queries behind report visuals.
- When the first number cannot be a month (`25/01/2026`), the other order is used rather than failing.
- ISO text (`2026-08-01`, `2026-08-01 14:30:00`) reads the same under either setting. Prefer it in scripts that others will run.
- The installation default comes from `Engine:DateFormat` in appsettings (`MDY` when absent).
- The setting never depends on the server's locale. It does not change output: dates cast to text are always ISO. Use `FORMAT` for other layouts.

## References
- [SET Commands](README.md)
- [CAST](../functions/conversion/cast.md)
- [Engine configuration](../../administration/platform/config/engine-configuration.md)
