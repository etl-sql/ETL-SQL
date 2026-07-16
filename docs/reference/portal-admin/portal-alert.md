# Portal Report Alerts
Create portal alert definitions through a `REPORTPORTAL` connection.

## Syntax
```sql
CREATE ALERT 'AlertName' FOR REPORT '/Folder/ReportName'
  WHEN VISUAL 'VisualName' >= 1000
  [DELIVER TO 'recipient@corp.com']
  [AT smtp_alias]
  [ENABLE | DISABLE];

SHOW ALERTS FOR REPORT '/Folder/ReportName' [INTO #alerts];
DROP ALERT 'AlertName' FOR REPORT '/Folder/ReportName';
```

## Example
```sql
EXECUTE portal BEGIN
  CREATE ALERT 'RevenueFloor' FOR REPORT '/Finance/Finance Dashboard'
    WHEN VISUAL 'Revenue' < 50000
    DELIVER TO 'finance@corp.com'
    AT corporate_smtp
    DISABLE;
END;
```

## Notes
- Alerts are definition-only portal metadata in v0.11.0; the portal does not yet evaluate or send them.
- New alerts are enabled unless `DISABLE` is specified.
- Configuration import uses report path and alert name as its stable key. Replay updates drifted
  definitions and skips equal definitions.

References:
- [Data Connectors](../../guides/administration.md)
- [Portal Admin Commands](README.md)
