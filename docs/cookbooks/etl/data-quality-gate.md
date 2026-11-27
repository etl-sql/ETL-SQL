# Data Quality Gate
<!-- description: **▶ Runs as-is.** Assert data quality before loading: in-stream EXPECT rules quarantine bad rows (nulls, orphaned keys, out-of-range values) or fail the job. -->
Assert data quality before loading. This pattern catches bad data (nulls, orphaned keys, out-of-range values) and either fails the job or routes bad rows to a quarantine table. Never load first and ask questions later.

## Try It Now (No Database Needed)

Paste into a `.etlsql` file and press **F5**. `EXPECT` rules run in-stream against the built-in `MOCKDB()` sample orders:

```sql
CREATE CONNECTION demo AS MOCKDB();

-- Every row is checked as it streams in. Rows that break a QUARANTINE rule are set
-- aside with the reason; a THROW rule stops the run. The section label is the
-- replay point if you fix quarantined rows and re-run.
stage_orders:
SELECT
    SaleID   EXPECT NOT NULL ON FAILURE THROW
             EXPECT UNIQUE   ON FAILURE THROW,
    Region   EXPECT NOT NULL ON FAILURE QUARANTINE,
    Quantity EXPECT > 0      ON FAILURE QUARANTINE,
    Total    EXPECT <= 1000  ON FAILURE QUARANTINE   -- demo threshold: large orders need review
INTO #clean_orders
FROM demo.Orders
ON FAILURE QUARANTINE TO #quarantine_orders WITH (HANDLING = SCRIPT)
ON FAILURE THROW;

SELECT COUNT(*) AS CleanRows FROM #clean_orders;
SELECT COUNT(*) AS QuarantinedRows FROM #quarantine_orders;
```

About 1 in 6 mock orders exceeds the demo threshold, so they land in `#quarantine_orders` with the failing rule while the rest load into `#clean_orders`. Change `ON FAILURE QUARANTINE` to `ON FAILURE THROW` on the `Total` rule to see the run stop instead. The production version below does the same checks with explicit rule queries, a durable quarantine table, and a `MERGE` into the target.

## Production Version

**Pattern Scenario:** Quality-gate a staging table before merging into production.

```sql
CREATE CONNECTION dest      AS MSSQL(SERVER='prod-db', DATABASE='Sales',   TRUSTED_CONNECTION=TRUE);
CREATE CONNECTION quarantine AS MSSQL(SERVER='dq-db',   DATABASE='DataQuality', TRUSTED_CONNECTION=TRUE);

-- Assume #staging is already loaded by a prior step

-- 1. Null checks
SELECT 'NULL_EMAIL' AS RuleViolation, COUNT(*) AS FailCount
INTO #dq_results
FROM #staging WHERE Email IS NULL OR Email = '';

INSERT INTO #dq_results
SELECT 'NULL_CUSTOMER_ID', COUNT(*) FROM #staging WHERE CustomerId IS NULL;

-- 2. Referential integrity check (every CustomerId must exist in dest)
INSERT INTO #dq_results
SELECT 'ORPHANED_CUSTOMER_ID', COUNT(*)
FROM #staging AS s
WHERE NOT EXISTS (SELECT 1 FROM dest.dbo.Customers WHERE Id = s.CustomerId);

-- 3. Range checks
INSERT INTO #dq_results
SELECT 'AMOUNT_OUT_OF_RANGE', COUNT(*)
FROM #staging WHERE Amount < 0 OR Amount > 1000000;

-- 4. Evaluate results
DECLARE @TotalFailures INT = (SELECT SUM(FailCount) FROM #dq_results WHERE FailCount > 0);

IF @TotalFailures > 0
BEGIN
    PRINT 'DATA QUALITY FAILURES DETECTED:';
    SELECT * FROM #dq_results WHERE FailCount > 0;

    -- Route bad rows to quarantine instead of aborting
    INSERT INTO quarantine.dbo.StagingQuarantine
    SELECT GETDATE() AS QuarantinedAt, 'FAILED_DQ' AS Reason, * FROM #staging
    WHERE Email IS NULL
       OR CustomerId IS NULL
       OR Amount < 0 OR Amount > 1000000;

    -- Remove bad rows from staging before loading good ones
    DELETE FROM #staging
    WHERE Email IS NULL
       OR CustomerId IS NULL
       OR Amount < 0 OR Amount > 1000000;

    PRINT 'Bad rows quarantined. Loading ' + CAST((SELECT COUNT(*) FROM #staging) AS STRING) + ' clean rows.';
END

-- 5. Load only clean rows
IF (SELECT COUNT(*) FROM #staging) > 0
BEGIN
    MERGE INTO dest.dbo.Orders AS T
    USING #staging AS S ON T.OrderId = S.OrderId
    WHEN MATCHED THEN UPDATE SET T.Amount = S.Amount, T.Status = S.Status
    WHEN NOT MATCHED THEN INSERT (OrderId, CustomerId, Amount, Status)
                         VALUES (S.OrderId, S.CustomerId, S.Amount, S.Status);
    PRINT 'Clean rows loaded successfully.';
END
```
