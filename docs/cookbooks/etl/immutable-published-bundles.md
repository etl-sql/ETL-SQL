# Immutable Published Script Bundles (CI/CD Deployment)
This pattern compiles and packages a multi-file script folder into an immutable versioned bundle inside the Orchestrator lockbox. It then registers a recurring Orchestrator job that locks execution to that specific version, ensuring production runs are shielded from disk changes.

**Pattern Scenario:** Package `C:\ETL\finance` (entry script: `main.etlsql`, references `child.etlsql`) → Publish → Validate → Schedule versioned execution.

```sql
-- 1. Infrastructure Connections
CREATE CONNECTION src_db AS MSSQL('Server=prod_db;Database=Finance;Trusted_Connection=True;');
CREATE CONNECTION pg_dest AS POSTGRES('Host=dest_db;Database=Analytics;Username=loader;Password=...');

-- Note: Remote administration connections must use ENCRYPT/ENC options
CREATE CONNECTION local_orch AS ORCHESTRATOR(HOST = 'http://localhost:5001', API_KEY = 'ENC:U2FsdGVkX1+...');

BEGIN TRY
    -- 2. Publish the source directory as an immutable script bundle
    -- This scans C:\ETL\finance, parses and packages main.etlsql, child.etlsql, etc.
    PUBLISH BUNDLE 'finance-pipeline'
        FROM 'C:\ETL\finance'
        ENTRY 'main.etlsql'
        WITH (PASSWORD = 'lockbox-master-password', ENCRYPT = MACHINE);

    -- 3. Verify the published bundle structure and dependencies
    VALIDATE BUNDLE 'finance-pipeline'
        FROM 'C:\ETL\finance'
        ENTRY 'main.etlsql'
        WITH (PASSWORD = 'lockbox-master-password');

    -- 4. Schedule the job on the remote Orchestrator.
    -- Pinned version resolution occurs at job creation time (e.g. resolves to version 1).
    EXECUTE local_orch BEGIN
        CREATE SCHEDULE FinanceReconciliationNightly
            ON '30 2 * * *'
            AT TIME ZONE 'UTC';

        CREATE OR REPLACE JOB RunFinanceReconciliation
            FOR SCRIPT 'orch://finance-pipeline/main.etlsql'
            WITH (MAX_RETRIES = 3, RETRY_DELAY = 60);

        ALTER JOB RunFinanceReconciliation
            ADD SCHEDULE FinanceReconciliationNightly;
    END;

    PRINT 'Bundle published and job scheduled successfully.';

END TRY
BEGIN CATCH
    PRINT 'Deployment Failed: ' + ERROR_MESSAGE();
    THROW;
END CATCH;
```
