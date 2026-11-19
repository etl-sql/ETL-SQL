# Automated Slack/Teams Alerting
Send pipeline status and failure alerts directly to Slack and Microsoft Teams channels using `SLACK` and `TEAMS` connections (aliases of the write-only `WEBHOOK` connector).

**Pattern Scenario:** Extract daily orders, stage and validate rows in the engine, merge clean rows into the warehouse, and notify Slack or Teams on warnings, completion, or failure.

## Direct Webhook Alerting (Standalone or Orchestrated)

`SLACK` and `TEAMS` connections are write-only sinks (`INSERT INTO`).
- `FORMAT = 'slack'` sends `{"text": "..."}` from the inserted `Text` column.
- `FORMAT = 'teams'` sends a MessageCard built from the `Title` and `Text` columns.
- Webhook URLs embed their auth token, so store them as `SECRET:` references.

```sql
-- 1. Declare source, warehouse, and Slack/Teams connections
CREATE CONNECTION crm_src AS POSTGRES(
    HOST     = 'crm-db.internal',
    DATABASE = 'crm',
    USER     = 'etl_reader',
    PASSWORD = 'SECRET:crm_db_password'
);

CREATE CONNECTION dw_dest AS MSSQL(
    SERVER             = 'dw-sql.internal',
    DATABASE           = 'Warehouse',
    TRUSTED_CONNECTION = TRUE
);

CREATE CONNECTION slack_alerts AS SLACK(
    URL             = 'SECRET:slack_webhook_url',
    FORMAT          = 'slack',
    TIMEOUT_SECONDS = 10,
    RETRY_COUNT     = 2
);

CREATE CONNECTION teams_alerts AS TEAMS(
    URL             = 'SECRET:teams_webhook_url',
    FORMAT          = 'teams',
    TIMEOUT_SECONDS = 10,
    RETRY_COUNT     = 2
);

BEGIN TRY
    -- 2. Extract and stage inbound rows
    SELECT
        OrderId,
        CustomerId,
        Amount,
        UpdatedAt
    INTO #staged_orders
    FROM crm_src.public.orders
    WHERE UpdatedAt >= DATEADD(DAY, -1, GETDATE());

    -- 3. Validate staged rows
    SELECT * INTO #clean_orders
    FROM #staged_orders
    WHERE CustomerId IS NOT NULL AND Amount >= 0;

    SELECT * INTO #invalid_orders
    FROM #staged_orders
    WHERE CustomerId IS NULL OR Amount < 0;

    DECLARE @clean_count   INT = (SELECT COUNT(*) FROM #clean_orders);
    DECLARE @invalid_count INT = (SELECT COUNT(*) FROM #invalid_orders);

    -- 4. Atomic merge into the destination warehouse
    BEGIN TRANSACTION;
        MERGE INTO dw_dest.dbo.FactOrders AS T
        USING #clean_orders AS S ON T.OrderId = S.OrderId
        WHEN MATCHED THEN
            UPDATE SET T.Amount = S.Amount, T.UpdatedAt = S.UpdatedAt
        WHEN NOT MATCHED THEN
            INSERT (OrderId, CustomerId, Amount, UpdatedAt)
            VALUES (S.OrderId, S.CustomerId, S.Amount, S.UpdatedAt);
    COMMIT;

    -- 5. Cleanup staging tables
    DROP TABLE #staged_orders;
    DROP TABLE #clean_orders;
    DROP TABLE #invalid_orders;

    -- 6. Notify Teams on warnings, or Slack on a clean run
    IF @invalid_count > 0
    BEGIN
        DECLARE @warn_text STRING = CONCAT(
            'Loaded ', @clean_count, ' clean rows; skipped ', @invalid_count, ' invalid rows.'
        );
        INSERT INTO teams_alerts (Title, Text)
        VALUES ('Nightly Orders Load Completed with Warnings', @warn_text);
    END
    ELSE
    BEGIN
        DECLARE @ok_text STRING = CONCAT(
            '[OK] Nightly Orders load succeeded: ', @clean_count, ' rows merged.'
        );
        INSERT INTO slack_alerts (Text)
        VALUES (@ok_text);
    END;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;

    DECLARE @err_text STRING = CONCAT('[CRITICAL] Nightly Orders load failed: ', ERROR_MESSAGE());

    -- Dispatch failure alerts to both Slack and Teams before re-throwing
    INSERT INTO slack_alerts (Text)
    VALUES (@err_text);

    INSERT INTO teams_alerts (Title, Text)
    VALUES ('CRITICAL: Nightly Orders Load Failed', @err_text);

    THROW;
END CATCH;
```

## Orchestrator Transition Alerts (`ASSERT JOB`)

When running under the Orchestrator, register a notification object on top of a `SLACK` or `TEAMS` connection so `ASSERT JOB` notifies only on pass-to-fail and recovery transitions instead of repeating every run:

```sql
CREATE CONNECTION dq_slack AS SLACK(
    URL    = 'SECRET:slack_webhook_url',
    FORMAT = 'slack'
);

CREATE NOTIFICATION dq_slack_channel USING dq_slack
    WITH (DESCRIPTION = 'Data quality alerts in #data-ops');

ASSERT JOB nightly_orders (
    ROW_COUNT > 0,
    QUARANTINE_PERCENT < 0.01
)
ON FAILURE NOTIFY dq_slack_channel
ON FAILURE THROW;
```

## References

- [WEBHOOK Connector](../../reference/connectors/services/webhook.md)
- [ASSERT JOB](../../reference/statements/session-control/assert-job.md)
- [TRY...CATCH](../../reference/control-flow/try-catch.md)
- [Automated Email Alerting](automated-email-alerting.md)
