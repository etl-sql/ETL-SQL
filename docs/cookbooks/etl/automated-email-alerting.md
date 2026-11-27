# Automated Email Alerting
Centralized error reporting pattern using an `SMTP` connection and `SEND EMAIL`.

**Pattern Scenario:** Wrap a nightly load in `TRY...CATCH` and send an operational alert email on failure.

```sql
CREATE CONNECTION alerts_smtp AS SMTP(
    HOST     = 'smtp.company.com',
    PORT     = 587,
    USERNAME = 'alerts@company.com',
    PASSWORD = 'SECRET:smtp_password',
    USE_SSL  = TRUE
);

CREATE PROCEDURE NotifyTeam @Msg STRING, @Level STRING
AS
BEGIN
    DECLARE @Subj = '[' + @Level + '] ETL Pipeline Alert';
    SEND EMAIL
        FROM    'etl@company.com'
        TO      'oncall@company.com'
        SUBJECT @Subj
        BODY    @Msg
        AT      alerts_smtp;
END;

-- Usage in an error handler
BEGIN TRY
    RUN SCRIPT 'nightly_load.etlsql';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    EXEC NotifyTeam @Msg = ('Nightly Load Failed: ' + ERROR_MESSAGE()), @Level = 'CRITICAL';
    THROW;
END CATCH;
```

## References

- [SEND EMAIL](../../reference/file-operations/send-email.md)
- [SMTP Connector](../../reference/connectors/services/smtp.md)
- [TRY...CATCH](../../reference/control-flow/try-catch.md)
- [Automated Slack/Teams Alerting](automated-slack-teams-alerting.md)
