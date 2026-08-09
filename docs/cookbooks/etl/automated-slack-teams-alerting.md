# Automated Slack/Teams Alerting
Centralized error reporting pattern using `SEND EMAIL` configured for webhook-style SMTP.

```sql
CREATE CONNECTION alerts_smtp AS SMTP('smtp.company.com', PORT=587, USERNAME='alerts@company.com', PASSWORD='apppassword', USE_SSL=TRUE);

CREATE PROCEDURE NotifyTeam @Msg STRING, @Level STRING
AS
BEGIN
    DECLARE @Subj = '[' + @Level + '] ETL Pipeline Alert';
    SEND EMAIL 
        FROM    'etl@company.com'
        TO      'dev-alerts@company.slack.com'
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
END CATCH;
```
