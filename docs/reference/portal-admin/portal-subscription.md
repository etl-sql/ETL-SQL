# Portal Report Subscriptions
Schedule report delivery through a `PORTAL` connection.

## Syntax
```sql
CREATE SUBSCRIPTION ['SubscriptionName']
  FOR REPORT '/Folder/ReportName'
  DELIVER TO 'recipient@corp.com' | GROUP 'GroupName'
  SCHEDULE 'cron_expression' | ON REFRESH
  FORMAT PDF | CSV | BOTH
  AT smtp_alias
  [PARAMETERS (@parameter = 'value', ...)]
  [ENABLE | DISABLE];

ALTER SUBSCRIPTION <id> SET
  SCHEDULE = 'cron_expression' | FORMAT = PDF | CSV | BOTH |
  SMTP = 'smtp_alias' | ENABLE | DISABLE | PARAMETERS (...);

DROP SUBSCRIPTION <id>;
SHOW SUBSCRIPTIONS [FOR REPORT '/Folder/ReportName'] [INTO #subscriptions];
```

## Example
```sql
EXECUTE portal BEGIN
  CREATE SUBSCRIPTION 'WeeklyFinance'
    FOR REPORT '/Finance/Finance Dashboard'
    DELIVER TO 'finance@corp.com'
    SCHEDULE '0 7 * * 1'
    FORMAT PDF
    AT corporate_smtp
    PARAMETERS (@region = 'All')
    DISABLE;
END;
```

## Notes
- New subscriptions are enabled unless `DISABLE` is specified.
- Remote portal creation currently supports one recipient and `PDF` or `CSV`. `GROUP` and `BOTH`
  parse but are rejected by the connector until those delivery forms are implemented.
- Configuration import uses report path and subscription name as its stable key. Anonymous
  subscriptions additionally use the recipient to avoid collapsing unrelated definitions. Replay
  updates drifted definitions and skips equal definitions.
- RELDATE parameter strings are resolved when delivery runs.

References:
- [Data Connectors](../../administration/platform/README.md)
- [Portal Admin Commands](README.md)
