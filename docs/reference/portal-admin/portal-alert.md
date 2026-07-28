# Portal Report Alerts
Create portal alert definitions through a `PORTAL` connection.

## Syntax
```sql
CREATE [OR REPLACE | OR ALTER] ALERT AlertName
  FOR REPORT '/Folder/ReportName'
  WHEN VISUAL VisualName >= 1000
  [WITH (DISPLAY_NAME = 'Alert label', DESCRIPTION = 'Alert description')];

ALTER ALERT AlertName ADD NOTIFICATION orchestrator_alias.NotificationName;
ALTER ALERT AlertName REMOVE NOTIFICATION orchestrator_alias.NotificationName;
ALTER ALERT AlertName SET (DESCRIPTION = 'Updated description');

ENABLE ALERT AlertName;
DISABLE ALERT AlertName;

SHOW ALERTS FOR REPORT '/Folder/ReportName' [INTO #alerts];
DROP ALERT [IF EXISTS] AlertName;
```

## Example
```sql
EXECUTE portal BEGIN
  CREATE OR REPLACE ALERT RevenueFloor
    FOR REPORT '/Finance/Finance Dashboard'
    WHEN VISUAL Revenue < 50000
    WITH (DESCRIPTION = 'Finance KPI floor');

  ALTER ALERT RevenueFloor ADD NOTIFICATION orchestrator.FinanceOps;
  DISABLE ALERT RevenueFloor;
END;
```

## Notes
- Alert names and visual names are identifiers. Report names remain string paths.
- Delivery is attached through named Orchestrator notifications; inline `DELIVER TO ... AT ...`
  on `CREATE ALERT` is retired.
- New alerts are enabled by default. Use `DISABLE ALERT` to pause one.
- Configuration import uses alert name as its stable key. Replay updates drifted definitions and skips
  equal definitions.

References:
- [Data Connectors](../../administration/platform/README.md)
- [Portal Admin Commands](README.md)
