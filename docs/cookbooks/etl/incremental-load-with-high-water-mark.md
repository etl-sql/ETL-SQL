# Incremental Load with High-Water Mark
The most fundamental production ETL pattern. Store a watermark (the last successfully loaded timestamp or ID), extract only rows newer than it on each run, then advance the watermark on success. This is the basis of virtually every scheduled ETL job.

**Pattern Scenario:** Incremental customer sync from a source database.

```sql
CREATE CONNECTION src    AS MSSQL(SERVER='src-db',     DATABASE='CRM',       TRUSTED_CONNECTION=TRUE);
CREATE CONNECTION dest   AS MSSQL(SERVER='dest-db',    DATABASE='Warehouse',  TRUSTED_CONNECTION=TRUE);
CREATE CONNECTION ctl_db AS MSSQL(SERVER='control-db', DATABASE='ETLControl', TRUSTED_CONNECTION=TRUE);

BEGIN TRY
    -- 1. Read the last successful watermark
    DECLARE @LastRun DATETIME;
    SELECT @LastRun = LastSuccessfulRun
    INTO #watermark
    FROM ctl_db.ETL_Watermarks
    WHERE JobName = 'CustomerSync';

    SET @LastRun = (SELECT LastSuccessfulRun FROM #watermark);
    IF @LastRun IS NULL SET @LastRun = '1900-01-01';  -- first-run bootstrap

    PRINT 'Extracting changes since: ' + CAST(@LastRun AS STRING);

    -- 2. Extract only changed rows (delta only — not full table)
    SELECT Id, Name, Email, Phone, UpdatedAt
    INTO #delta
    FROM src.dbo.Customers
    WHERE UpdatedAt > @LastRun;

    DECLARE @DeltaCount INT = (SELECT COUNT(*) FROM #delta);
    PRINT 'Delta rows: ' + CAST(@DeltaCount AS STRING);

    IF @DeltaCount = 0
    BEGIN
        PRINT 'No changes detected. Exiting.';
        RETURN;
    END

    -- 3. Merge into destination
    MERGE INTO dest.dbo.Customers AS T
    USING #delta AS S ON T.Id = S.Id
    WHEN MATCHED THEN
        UPDATE SET T.Name = S.Name, T.Email = S.Email, T.Phone = S.Phone, T.UpdatedAt = S.UpdatedAt
    WHEN NOT MATCHED THEN
        INSERT (Id, Name, Email, Phone, UpdatedAt)
        VALUES (S.Id, S.Name, S.Email, S.Phone, S.UpdatedAt);

    -- 4. Advance the watermark ONLY on success
    UPDATE ctl_db.ETL_Watermarks
    SET LastSuccessfulRun = GETDATE()
    WHERE JobName = 'CustomerSync';

    PRINT 'Watermark advanced to: ' + CAST(GETDATE() AS STRING);
END TRY
BEGIN CATCH
    PRINT 'Incremental load failed: ' + ERROR_MESSAGE();
    -- DO NOT advance the watermark on failure — next run will retry the same window
    THROW;
END CATCH;
```

> [!TIP]
> Never advance the watermark inside the catch block. If the load fails, the next run should retry the same window. This gives you idempotent, self-healing pipelines.
