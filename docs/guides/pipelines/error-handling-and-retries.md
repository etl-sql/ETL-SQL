# Error Handling, Alerting, and Retries

ETL-SQL provides structured error handling with **`TRY...CATCH`** blocks, programmatic error inspection functions, outbound email/webhook alerts, and automated retry policies within scheduled jobs.

---

> **Applies to:** every deployment profile (Solo, Team, Enterprise, SaaS).

## Structured Error Handling (`TRY...CATCH`)

```sql
BEGIN TRY
    -- Critical transactional logic
    INSERT INTO target_db.dbo.Accounts SELECT * FROM #staged_accounts;
END TRY
BEGIN CATCH
    -- Error remediation & alerting
    PRINT 'Error occurred: ' + ERROR_MESSAGE();
    THROW; -- Re-throw to halt execution and return non-zero exit code
END CATCH
```

### Error Inspection Functions

| Function | Return Type | Description |
| :--- | :--- | :--- |
| `ERROR_MESSAGE()` | `STRING` | The human-readable error description. |
| `ERROR_NUMBER()` | `INT` | The numeric error code. |
| `ERROR_LINE()` | `INT` | The script line number where the failure occurred. |
| `ERROR_STATE()` | `INT` | The state identifier of the error. |

---

## Example 1: Catch Block with Outbound Email Notification

When a critical task fails, send an alert email with the error message before terminating the script.

```sql
CREATE CONNECTION mailer AS SMTP(
    HOST     = 'smtp.company.local',
    PORT     = 587,
    USERNAME = 'etl_runner',
    PASSWORD = 'SECRET:smtp_password'
);

BEGIN TRY
    RUN SCRIPT 'critical_monthly_close.etlsql';
    PRINT 'Monthly close succeeded.';
END TRY
BEGIN CATCH
    DECLARE @errMsg STRING = ERROR_MESSAGE();
    PRINT 'Critical task failed: ' + @errMsg;

    SEND EMAIL
        TO      'data-ops@company.com'
        FROM    'etl-alerts@company.com'
        SUBJECT 'URGENT: Monthly Close Pipeline Failed'
        BODY    @errMsg
        AT      mailer;

    THROW; -- Halt pipeline execution
END CATCH
```

---

## Example 2: Transient Network Failure Retries in Scheduled Jobs

For jobs subject to transient network blips or temporary database connection limits, configure an automatic retry policy on the Orchestrator job definition.

```sql
-- Define schedule
CREATE SCHEDULE DailyAtMidnight ON '0 0 * * *' AT TIME ZONE 'UTC';

-- Define job with 3 retries spaced 60 seconds apart
CREATE JOB NightlySync FOR SCRIPT 'pipelines/nightly_sync.etlsql'
    WITH (
        MAX_RETRIES = 3,
        RETRY_DELAY = 60
    );

ALTER JOB NightlySync ADD SCHEDULE DailyAtMidnight;
```

If the script throws an error, the Orchestrator waits 60 seconds and re-executes the job up to 3 times before declaring a final failure state.

---

## Common Pitfalls

- **Leaking Secrets in Alert Bodies**: Never concatenate raw connection strings, decrypted passwords, or API keys into `BODY` parameters for `SEND EMAIL` or `SEND WEBHOOK`.
- **Swallowing Errors**: Omitting `THROW` inside a `CATCH` block marks the script execution as successful (`Exit Code 0`). Always re-throw unless the error was completely remediated.

---

## Related Topics

- [Script Resilience and Checkpoints](script-resilience-and-checkpoints.md) — WHAT_IF dry runs and session resume.
- [TRY...CATCH Reference](../../reference/control-flow/try-catch.md) — Detailed statement syntax.
- [SEND EMAIL Reference](../../reference/file-operations/send-email.md) — SMTP options and attachment syntax.
