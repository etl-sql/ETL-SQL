# Outbound REST API Submission (Sink)
Submit rows to a REST API destination using INSERT INTO, and capture status/response metadata in a temporary table for validation, retry, and auditing.

**Pattern Scenario:** Stage bed usage data in a temporary table, validate it, perform a dry-run (WHAT_IF), and then send the data to a remote hospital API.

```sql
DECLARE @api_token STRING = 'ENC:U2FsdGVk...';

-- 1. Stage local data
CREATE TABLE #bed_usage (
    submission_id VARCHAR,
    location VARCHAR,
    total_beds INT,
    occupied_beds INT
);

INSERT INTO #bed_usage VALUES
    ('sub-001', 'ICU', 24, 19),
    ('sub-002', 'ER', 16, 12);

-- 2. Define outbound API connection
CREATE CONNECTION bed_api AS API(
    URL = 'https://example.org/api/bed-usage',
    METHOD = 'POST',
    AUTH_TYPE = 'BEARER',
    TOKEN = @api_token,
    BODY_MODE = 'ROW_OBJECT',
    RESPONSE_TABLE = '#bed_api_results',
    RESPONSE_CORRELATION_COLUMNS = 'submission_id,location',
    SUCCESS_STATUS = '200,201,202,204',
    ERROR_MODE = 'CONTINUE',
    IDEMPOTENCY_KEY_COLUMN = 'submission_id',
    RETRY_COUNT = 3,
    RETRY_BACKOFF_MS = 500
);

-- 3. Dry-run validation (WHAT_IF)
SET WHAT_IF ON;
INSERT INTO bed_api (submission_id, location, totalBeds, occupiedBeds)
SELECT submission_id, location, total_beds, occupied_beds
FROM #bed_usage;
SET WHAT_IF OFF;

-- 4. Actual execution
BEGIN TRY
    INSERT INTO bed_api (submission_id, location, totalBeds, occupiedBeds)
    SELECT submission_id, location, total_beds, occupied_beds
    FROM #bed_usage;

    -- 5. Audit failed submissions
    SELECT
        submission_id,
        location,
        status_code,
        JSON_VALUE(response_body, '$.error.message') AS api_error
    FROM #bed_api_results
    WHERE success = FALSE;

    PRINT 'Submissions processed. Total failures: ' + 
          CAST((SELECT COUNT(*) FROM #bed_api_results WHERE success = FALSE) AS STRING);
END TRY
BEGIN CATCH
    PRINT 'Submission pipeline failed: ' + ERROR_MESSAGE();
    THROW;
END CATCH;
```
