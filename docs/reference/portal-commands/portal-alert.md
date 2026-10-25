# ALERT

Create portal alert definitions through a `PORTAL` connection. Delivery uses named Orchestrator notification attachments.

<!-- CreatePortalAlertStatement -->
<!-- AlterPortalAlertNotificationStatement -->
<!-- AlterPortalAlertStatement -->
<!-- DropPortalAlertStatement -->
<!-- SetPortalAlertEnabledStatement -->
<!-- ShowPortalAlertsStatement -->

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

SELECT * [INTO #alerts] FROM eng.alerts('/Folder/ReportName');
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
- **Names** — Alert names and visual names are identifiers. Report names remain string paths.
- **Delivery** — Attach named Orchestrator notifications; inline `DELIVER TO ... AT ...`
  on `CREATE ALERT` is retired.
- **HTTP contract** — Alert responses omit `recipient` and `smtpAlias`. Create/update requests reject these fields, including explicit nulls, and other unknown fields. Use notification attachment operations for delivery.
- **Initial state** — New alerts are enabled by default. Use `DISABLE ALERT` to pause one.
- **Configuration import** — Alert name is the stable key. Replay updates drifted definitions and skips
  equal definitions.

## References

- [Data Connectors](../connectors/README.md)
- [Portal Admin Commands](README.md)
