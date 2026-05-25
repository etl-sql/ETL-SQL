# Portal Report Alerts
Create condition-based alerts that notify recipients when report data meets a threshold inside an `EXECUTE portal` block.

## Syntax
```sql
EXECUTE portal BEGIN
  CREATE ALERT 'AlertName' FOR REPORT 'ReportName'
    WHEN 'condition_expression'
    NOTIFY 'recipient@corp.com'
    WITH (SCHEDULE = 'cron_expression');

  SHOW ALERTS FOR REPORT 'ReportName';
  DROP ALERT 'AlertName';
END;
```

## Examples
```sql
-- Alert when total revenue drops below threshold on weekday mornings
EXECUTE portal BEGIN
  CREATE ALERT 'RevenueAlert' FOR REPORT 'Finance Dashboard'
    WHEN 'total_revenue < 50000'
    NOTIFY 'finance-team@corp.com'
    WITH (SCHEDULE = '0 8 * * 1-5');
END;

-- Alert with multiple recipients
EXECUTE portal BEGIN
  CREATE ALERT 'InventoryAlert' FOR REPORT 'Supply Chain Dashboard'
    WHEN 'units_on_hand < reorder_point'
    NOTIFY 'ops@corp.com', 'warehouse@corp.com'
    WITH (SCHEDULE = '0 6 * * *');
END;

-- Alert checking a percentage condition
EXECUTE portal BEGIN
  CREATE ALERT 'ChurnAlert' FOR REPORT 'Customer Health'
    WHEN 'churn_rate > 0.05'
    NOTIFY 'cso@corp.com'
    WITH (SCHEDULE = '0 9 * * 1');
END;

-- List all alerts configured for a report
EXECUTE portal BEGIN
  SHOW ALERTS FOR REPORT 'Finance Dashboard' INTO #alerts;
END;
SELECT alert_name, condition, schedule, last_checked, last_triggered FROM #alerts;

-- Remove an alert
EXECUTE portal BEGIN
  DROP ALERT 'RevenueAlert';
END;
```

## Notes
- Alerts evaluate the `WHEN` condition against the report's current dataset on the specified cron schedule.
- The `WHEN` clause is a SQL-style boolean expression. Column names reference the columns exposed by the report's primary dataset.
- The `SCHEDULE` uses standard 5-field cron syntax: `minute hour day-of-month month day-of-week`.
- `NOTIFY` accepts one or more comma-separated email addresses. Each address receives a notification email when the condition evaluates to true.
- Alerts require a working SMTP connection configured in the portal's `appsettings.json` under `Portal:Smtp`.
- An alert fires at most once per scheduled evaluation — it does not re-fire until the next scheduled check, even if the condition remains true.
- `SHOW ALERTS` returns the alert name, condition expression, schedule, last check timestamp, and last trigger timestamp.
- Dropping a report does not automatically remove its alerts; drop alerts before dropping the report to avoid orphaned scheduler entries.
- See: PORTAL_REPORT, PORTAL_SUBSCRIPTION, PORTAL_SHOW

References:
- [Data Connectors](../../../../../Docs/Reference/Data_Connectors.md)
- [Grammar](../../../../../Docs/Reference/Grammar.md)
