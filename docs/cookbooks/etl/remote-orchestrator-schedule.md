# Scheduling a Recurring Job on a Remote Orchestrator
Once a pipeline is published, register it as a scheduled job on the Orchestrator so it runs unattended with retries. Remote job creation is wrapped in an `EXECUTE <orch> BEGIN ... END` block targeting the orchestrator connection.

**Pattern Scenario:** Run a versioned finance pipeline nightly at 02:30 with up to 3 retries.

```sql
-- 1. Connect to the Orchestrator. An ENC: secret must be a quoted string.
CREATE CONNECTION orch AS ORCHESTRATOR(HOST = 'http://localhost:5001', API_KEY = 'ENC:U2FsdGVkX1+...');

-- 2. Register the named schedule and job on the remote orchestrator, then link them.
EXECUTE orch BEGIN
    CREATE SCHEDULE NightlyReconciliationSchedule
        ON '30 2 * * *'
        AT TIME ZONE 'UTC';

    CREATE OR REPLACE JOB NightlyReconciliation
        FOR SCRIPT 'orch://finance-pipeline/main.etlsql'
        WITH (MAX_RETRIES = 3, RETRY_DELAY = 60);

    ALTER JOB NightlyReconciliation
        ADD SCHEDULE NightlyReconciliationSchedule;
END;
```

> Pair this with recipe 20: publish an immutable bundle, then schedule `orch://<bundle>/<entry>` so production runs are pinned to a specific version. See [Job Scheduling](../../reference/orchestrator-jobs/schedule.md) for `CREATE SCHEDULE`, `CREATE JOB`, `ALTER JOB`, and `eng.job_history`.
