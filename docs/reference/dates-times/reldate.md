RELDATE — Relative date type. Stores a date expression that is resolved to a concrete date each time the script executes.

Syntax: DECLARE @var RELDATE [INPUT] = '<expression>'

Expressions:
  D        Today at midnight            D-7     Seven days ago
  D-1      Yesterday                    W-1     Start of last week
  M-1      Start of last month          ME-1    End of last month
  Q-1      Start of last quarter        QE-1    End of last quarter
  Y-1      January 1 of last year       YE-1    December 31 of last year
  N-2H     2 hours ago (Now - 2 Hours)  N-30M   30 minutes ago
  2026-01-01  Fixed ISO date (never changes)

Week boundaries (W, WE) use Monday as week-start by default.
Override: SET WEEK_START_DAY = 'Sunday';

Use INPUT to let callers (CLI, parent script, subscription) supply the expression:
  DECLARE @start RELDATE INPUT = 'M-1';

CLI override: etlsql run report.etlsql --var @start=W-1

References:
- [Dates and Times](dates-times.md)
