# Portal Report Subscriptions
Schedule automated report delivery via email inside an `EXECUTE portal` block.

## Syntax
```sql
EXECUTE portal BEGIN
  CREATE SUBSCRIPTION 'SubscriptionName'
    FOR REPORT 'ReportName'
    TO 'recipient@corp.com' [, 'recipient2@corp.com']
    WITH (SCHEDULE = 'cron_expression', FORMAT = 'PDF' | 'CSV' | 'EXCEL');

  ALTER SUBSCRIPTION 'SubscriptionName'
    SET (TO = 'recipient@corp.com', SCHEDULE = 'cron_expression');

  DROP SUBSCRIPTION 'SubscriptionName';
END;
```

## Examples
```sql
-- Deliver a weekly finance summary every Monday at 7 AM as PDF
EXECUTE portal BEGIN
  CREATE SUBSCRIPTION 'WeeklyFinance'
    FOR REPORT 'Finance Dashboard'
    TO 'cfo@corp.com', 'finance@corp.com'
    WITH (SCHEDULE = '0 7 * * 1', FORMAT = 'PDF');
END;

-- Deliver a daily operations report as an Excel file
EXECUTE portal BEGIN
  CREATE SUBSCRIPTION 'DailyOps'
    FOR REPORT 'Operations Summary'
    TO 'ops-team@corp.com'
    WITH (SCHEDULE = '0 6 * * *', FORMAT = 'EXCEL');
END;

-- Deliver a monthly data export as CSV on the first of each month
EXECUTE portal BEGIN
  CREATE SUBSCRIPTION 'MonthlyDataDump'
    FOR REPORT 'Raw Transaction Data'
    TO 'data-team@corp.com'
    WITH (SCHEDULE = '0 5 1 * *', FORMAT = 'CSV');
END;

-- Update the recipient list and delivery time for an existing subscription
EXECUTE portal BEGIN
  ALTER SUBSCRIPTION 'WeeklyFinance'
    SET (TO = 'cfo@corp.com', SCHEDULE = '0 8 * * 1');
END;

-- Remove a subscription
EXECUTE portal BEGIN
  DROP SUBSCRIPTION 'WeeklyFinance';
END;
```

## Notes
- Subscriptions deliver rendered report snapshots to a list of email recipients on a cron schedule.
- `FORMAT` controls the attachment format: `PDF` renders the full report layout, `CSV` exports tabular data, `EXCEL` exports data as an `.xlsx` workbook.
- The `SCHEDULE` uses standard 5-field cron syntax: `minute hour day-of-month month day-of-week`.
- `TO` accepts one or more comma-separated email addresses. Recipients do not need to have portal accounts.
- Reports are rendered with current data at the time of delivery — a subscription is not a snapshot of the data at creation time.
- `ALTER SUBSCRIPTION ... SET (TO = ...)` replaces the entire recipient list; include all addresses that should receive the report.
- Subscriptions require a working SMTP connection configured in the portal's `appsettings.json` under `Portal:Smtp`.
- Dropping a report does not automatically remove its subscriptions; drop subscriptions before dropping the report to avoid orphaned scheduler entries.
- See: PORTAL_REPORT, PORTAL_ALERT, PORTAL_SHOW

References:
- [Data Connectors](../../../../../Docs/Reference/Data_Connectors.md)
