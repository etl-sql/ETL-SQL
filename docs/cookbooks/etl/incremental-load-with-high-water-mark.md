# Incremental Load with High-Water Mark
<!-- description: **▶ Runs as-is.** Store a watermark, extract only rows newer than it on each run, then advance it on success — the basis of almost every scheduled ETL job. -->
The most fundamental production ETL pattern. Store a watermark (the last successfully loaded timestamp or ID), extract only rows newer than it on each run, then advance the watermark on success. This is the basis of virtually every scheduled ETL job.

## Try It Now (No Database Needed)

Paste into a `.etlsql` file and press **F5**. A `#watermarks` control table and the built-in `MOCKDB()` orders stand in for your control database and source:

```sql
CREATE CONNECTION demo AS MOCKDB();

-- Control table: the last successful load ran 7 days ago
SELECT 'OrderSync' AS JobName, DATEADD(DAY, -7, GETDATE()) AS LastSuccessfulRun
INTO #watermarks;

-- Target table, as it stood after the previous run
CREATE TABLE #warehouse_orders (SaleID BIGINT, Region VARCHAR(50), Total DECIMAL(18,2), OrderDate DATETIME);

-- 1. Read the watermark
DECLARE @LastRun DATETIME = (SELECT LastSuccessfulRun FROM #watermarks WHERE JobName = 'OrderSync');

-- 2. Extract only rows newer than the watermark
SELECT SaleID, Region, Total, OrderDate
INTO #delta
FROM demo.Orders
WHERE OrderDate > @LastRun;

PRINT 'Delta rows since last run: ' + CAST((SELECT COUNT(*) FROM #delta) AS STRING);

-- 3. Merge the delta into the target
MERGE INTO #warehouse_orders AS T
USING #delta AS S ON T.SaleID = S.SaleID
WHEN MATCHED THEN UPDATE SET T.Total = S.Total
WHEN NOT MATCHED THEN INSERT (SaleID, Region, Total, OrderDate)
                      VALUES (S.SaleID, S.Region, S.Total, S.OrderDate);

-- 4. Advance the watermark only after the load succeeded
UPDATE #watermarks
SET LastSuccessfulRun = (SELECT MAX(OrderDate) FROM #delta)
WHERE JobName = 'OrderSync';

SELECT JobName, LastSuccessfulRun FROM #watermarks;
```

Only orders newer than the watermark are extracted and merged, then the watermark moves to the newest loaded row. Run it again against a real control table and the second run picks up only what changed since. The production version keeps the watermark in a control database and never advances it on failure:

## Production Version

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

---

## Source-Specific Watermark Recipes (Full Lifecycle)

Each recipe below follows the full **Extract → Stage → Validate → Merge → Cleanup → Notify**
lifecycle using each connector's verified write path and transaction behavior (see
[Database Connectors](../../reference/connectors/databases/README.md) and
[Type Mapping](../../reference/connectors/type-mapping.md)).

### 1. SQL Server (`rowversion`)

SQL Server's `rowversion` column is a database-wide monotonic counter stored as `binary(8)` (`byte[]`
in the engine). Cast it to `BIGINT` in pushdown so it can be compared and stored cleanly as an integer
watermark. Staging into a table on `dest` enables native same-connection `MERGE` pushdown.

```sql
CREATE CONNECTION src  AS MSSQL(SERVER='src-sql01',  DATABASE='OrdersOLTP', USER='etl_reader', PASSWORD='SECRET:prod/mssql/etl_reader', ENCRYPT=TRUE);
CREATE CONNECTION dest AS MSSQL(SERVER='dest-sql01', DATABASE='Warehouse',  USER='etl_writer', PASSWORD='SECRET:prod/mssql/etl_writer', ENCRYPT=TRUE);
CREATE CONNECTION mail AS SMTP(HOST='smtp.company.com', PORT=587, USERNAME='etl@company.com', PASSWORD='SECRET:prod/smtp/password', USE_SSL=TRUE);

BEGIN TRY
    -- 1. Read last rowversion watermark (0 on first run)
    DECLARE @LastRv BIGINT = COALESCE(
        (SELECT LastRowVersion FROM dest.dbo.ETL_Watermarks WHERE JobName = 'MssqlOrdersSync'),
        0
    );

    -- 2. Extract delta from SQL Server using monotonic rowversion
    EXECUTE src INTO #delta WITH (@LastRv)
    BEGIN
        SELECT OrderId, CustomerId, Amount, UpdatedAt, CAST(RowVer AS BIGINT) AS RowVerNum
        FROM dbo.Orders
        WHERE CAST(RowVer AS BIGINT) > @p0
    END;

    DECLARE @DeltaCount INT = (SELECT COUNT(*) FROM #delta);
    IF @DeltaCount = 0
    BEGIN
        PRINT 'No new SQL Server rows since rowversion ' + CAST(@LastRv AS STRING);
        DROP TABLE IF EXISTS #delta;
        RETURN;
    END;

    -- 3. Validate staged delta before touching the destination
    DECLARE @BadRows INT = (SELECT COUNT(*) FROM #delta WHERE OrderId IS NULL OR Amount < 0);
    IF @BadRows > 0
        THROW 50001, 'Validation failed: null OrderId or negative Amount in #delta.', 1;

    DECLARE @NewRv BIGINT = (SELECT MAX(RowVerNum) FROM #delta);

    -- 4. Stage via SqlBulkCopy + same-connection MERGE pushdown in one transaction
    BEGIN TRANSACTION;
        TRUNCATE TABLE dest.dbo.Stage_Orders;
        INSERT INTO dest.dbo.Stage_Orders (OrderId, CustomerId, Amount, UpdatedAt)
        SELECT OrderId, CustomerId, Amount, UpdatedAt FROM #delta;

        MERGE INTO dest.dbo.Orders AS T
        USING dest.dbo.Stage_Orders AS S ON T.OrderId = S.OrderId
        WHEN MATCHED THEN
            UPDATE SET T.CustomerId = S.CustomerId, T.Amount = S.Amount, T.UpdatedAt = S.UpdatedAt
        WHEN NOT MATCHED THEN
            INSERT (OrderId, CustomerId, Amount, UpdatedAt)
            VALUES (S.OrderId, S.CustomerId, S.Amount, S.UpdatedAt);

        UPDATE dest.dbo.ETL_Watermarks
        SET LastRowVersion = @NewRv, UpdatedAt = GETDATE()
        WHERE JobName = 'MssqlOrdersSync';
    COMMIT;

    -- 5. Cleanup
    DROP TABLE IF EXISTS #delta;

    -- 6. Notify on success
    DECLARE @OkMsg STRING = 'MSSQL Orders sync succeeded: ' + CAST(@DeltaCount AS STRING) + ' rows (rv=' + CAST(@NewRv AS STRING) + ').';
    SEND EMAIL FROM 'etl@company.com' TO 'data-ops@company.com' SUBJECT '[OK] MSSQL Orders Sync' BODY @OkMsg AT mail;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    DROP TABLE IF EXISTS #delta;
    DECLARE @ErrMsg STRING = 'MSSQL Orders sync failed: ' + ERROR_MESSAGE();
    SEND EMAIL FROM 'etl@company.com' TO 'oncall@company.com' SUBJECT '[FAILED] MSSQL Orders Sync' BODY @ErrMsg AT mail;
    THROW;
END CATCH;
```

### 2. PostgreSQL (`updated_at` timestamp or `xmin`)

Use an indexed `updated_at` (`timestamptz`) column for durable watermarks across `VACUUM FREEZE`
cycles. If a source table lacks an update timestamp and you only need short-window change capture,
you can filter on `xmin::text::bigint > $1` instead (keep a periodic full reconciliation because
PostgreSQL freezes old `xmin` values to `FrozenTransactionId = 2`).

```sql
CREATE CONNECTION pg_src  AS POSTGRES(HOST='pg-src.internal',  PORT=5432, DATABASE='app_db',    USER='etl_reader', PASSWORD='SECRET:prod/pg/etl_reader', SSL_MODE='Require');
CREATE CONNECTION pg_dest AS POSTGRES(HOST='pg-dest.internal', PORT=5432, DATABASE='warehouse', USER='etl_writer', PASSWORD='SECRET:prod/pg/etl_writer', SSL_MODE='Require');
CREATE CONNECTION mail    AS SMTP(HOST='smtp.company.com', PORT=587, USERNAME='etl@company.com', PASSWORD='SECRET:prod/smtp/password', USE_SSL=TRUE);

BEGIN TRY
    -- 1. Read last UTC watermark
    DECLARE @LastUpdated DATETIMEOFFSET = COALESCE(
        (SELECT last_watermark FROM pg_dest.etl_watermarks WHERE job_name = 'PgOrdersSync'),
        CAST('1970-01-01T00:00:00Z' AS DATETIMEOFFSET)
    );

    -- 2. Extract delta (swap WHERE to xmin::text::bigint > @p0 if using xmin)
    EXECUTE pg_src INTO #delta WITH (@LastUpdated)
    BEGIN
        SELECT order_id, customer_id, amount, updated_at
        FROM public.orders
        WHERE updated_at > @p0
    END;

    DECLARE @DeltaCount INT = (SELECT COUNT(*) FROM #delta);
    IF @DeltaCount = 0
    BEGIN
        PRINT 'No new PostgreSQL rows since ' + CAST(@LastUpdated AS STRING);
        DROP TABLE IF EXISTS #delta;
        RETURN;
    END;

    -- 3. Validate staged rows in engine
    DECLARE @BadRows INT = (SELECT COUNT(*) FROM #delta WHERE order_id IS NULL OR amount < 0);
    IF @BadRows > 0
        THROW 50002, 'Validation failed: invalid PostgreSQL delta rows.', 1;

    DECLARE @NewWatermark DATETIMEOFFSET = (SELECT MAX(updated_at) FROM #delta);

    -- 4. Stage via COPY FROM STDIN + native ON CONFLICT upsert inside one transaction
    BEGIN TRANSACTION;
        TRUNCATE TABLE pg_dest.stage_orders;
        INSERT INTO pg_dest.stage_orders (order_id, customer_id, amount, updated_at)
        SELECT order_id, customer_id, amount, updated_at FROM #delta;

        EXECUTE pg_dest WITH (@NewWatermark)
        BEGIN
            INSERT INTO public.orders (order_id, customer_id, amount, updated_at)
            SELECT order_id, customer_id, amount, updated_at FROM public.stage_orders
            ON CONFLICT (order_id) DO UPDATE
            SET customer_id = EXCLUDED.customer_id,
                amount      = EXCLUDED.amount,
                updated_at  = EXCLUDED.updated_at;

            UPDATE public.etl_watermarks
            SET last_watermark = @p0, updated_at = NOW()
            WHERE job_name = 'PgOrdersSync';
        END;
    COMMIT;

    -- 5. Cleanup
    DROP TABLE IF EXISTS #delta;

    -- 6. Notify
    DECLARE @OkMsg STRING = 'PostgreSQL Orders sync succeeded: ' + CAST(@DeltaCount AS STRING) + ' rows.';
    SEND EMAIL FROM 'etl@company.com' TO 'data-ops@company.com' SUBJECT '[OK] Postgres Orders Sync' BODY @OkMsg AT mail;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    DROP TABLE IF EXISTS #delta;
    DECLARE @ErrMsg STRING = 'PostgreSQL Orders sync failed: ' + ERROR_MESSAGE();
    SEND EMAIL FROM 'etl@company.com' TO 'oncall@company.com' SUBJECT '[FAILED] Postgres Orders Sync' BODY @ErrMsg AT mail;
    THROW;
END CATCH;
```

### 3. Oracle (`ORA_ROWSCN` / SCN)

`ORA_ROWSCN` returns the conservative upper-bound System Change Number for each row (block-level
unless the table was created with `ROWDEPENDENCIES`, which is safe because `MERGE` is idempotent).
Because `OracleDataSource` does not enlist in ETL-SQL `BEGIN TRANSACTION`, bulk-load `#delta` into
an Oracle staging table via `OracleBulkCopy`, then run native Oracle `MERGE` and the watermark update
together in `EXECUTE o_dest BEGIN ... END`.

```sql
CREATE CONNECTION o_src  AS ORACLE(HOST='ora-src.internal',  PORT=1521, SERVICE_NAME='ERP', USER='etl_reader', PASSWORD='SECRET:prod/oracle/etl_reader');
CREATE CONNECTION o_dest AS ORACLE(HOST='ora-dest.internal', PORT=1521, SERVICE_NAME='EDW', USER='etl_writer', PASSWORD='SECRET:prod/oracle/etl_writer');
CREATE CONNECTION mail   AS SMTP(HOST='smtp.company.com', PORT=587, USERNAME='etl@company.com', PASSWORD='SECRET:prod/smtp/password', USE_SSL=TRUE);

BEGIN TRY
    -- 1. Read last committed SCN watermark
    DECLARE @LastScn BIGINT = COALESCE(
        (SELECT LAST_SCN FROM o_dest.ETL_WATERMARKS WHERE JOB_NAME = 'OracleOrdersSync'),
        0
    );

    -- 2. Extract rows changed since @LastScn
    EXECUTE o_src INTO #delta WITH (@LastScn)
    BEGIN
        SELECT ORDER_ID, CUSTOMER_ID, AMOUNT, UPDATED_AT, ORA_ROWSCN AS ROW_SCN
        FROM ERP.ORDERS
        WHERE ORA_ROWSCN > :p0
    END;

    DECLARE @DeltaCount INT = (SELECT COUNT(*) FROM #delta);
    IF @DeltaCount = 0
    BEGIN
        PRINT 'No new Oracle rows since SCN ' + CAST(@LastScn AS STRING);
        DROP TABLE IF EXISTS #delta;
        RETURN;
    END;

    -- 3. Validate staged rows in engine
    DECLARE @BadRows INT = (SELECT COUNT(*) FROM #delta WHERE ORDER_ID IS NULL OR AMOUNT < 0);
    IF @BadRows > 0
        THROW 50003, 'Validation failed: invalid Oracle delta rows.', 1;

    DECLARE @NewScn BIGINT = (SELECT MAX(ROW_SCN) FROM #delta);

    -- 4. Truncate and bulk-load staging via OracleBulkCopy, then native Oracle MERGE + watermark update
    TRUNCATE TABLE o_dest.STAGE_ORDERS;
    INSERT INTO o_dest.STAGE_ORDERS (ORDER_ID, CUSTOMER_ID, AMOUNT, UPDATED_AT)
    SELECT ORDER_ID, CUSTOMER_ID, AMOUNT, UPDATED_AT FROM #delta;

    EXECUTE o_dest WITH (@NewScn)
    BEGIN
        MERGE INTO EDW.ORDERS T
        USING EDW.STAGE_ORDERS S ON (T.ORDER_ID = S.ORDER_ID)
        WHEN MATCHED THEN
            UPDATE SET T.CUSTOMER_ID = S.CUSTOMER_ID, T.AMOUNT = S.AMOUNT, T.UPDATED_AT = S.UPDATED_AT
        WHEN NOT MATCHED THEN
            INSERT (ORDER_ID, CUSTOMER_ID, AMOUNT, UPDATED_AT)
            VALUES (S.ORDER_ID, S.CUSTOMER_ID, S.AMOUNT, S.UPDATED_AT);

        UPDATE EDW.ETL_WATERMARKS
        SET LAST_SCN = :p0, UPDATED_AT = SYSDATE
        WHERE JOB_NAME = 'OracleOrdersSync';

        COMMIT;
    END;

    -- 5. Cleanup
    DROP TABLE IF EXISTS #delta;

    -- 6. Notify
    DECLARE @OkMsg STRING = 'Oracle Orders sync succeeded: ' + CAST(@DeltaCount AS STRING) + ' rows (SCN=' + CAST(@NewScn AS STRING) + ').';
    SEND EMAIL FROM 'etl@company.com' TO 'data-ops@company.com' SUBJECT '[OK] Oracle Orders Sync' BODY @OkMsg AT mail;
END TRY
BEGIN CATCH
    DROP TABLE IF EXISTS #delta;
    DECLARE @ErrMsg STRING = 'Oracle Orders sync failed: ' + ERROR_MESSAGE();
    SEND EMAIL FROM 'etl@company.com' TO 'oncall@company.com' SUBJECT '[FAILED] Oracle Orders Sync' BODY @ErrMsg AT mail;
    THROW;
END CATCH;
```

## References

- [Database Connectors & Capability Matrix](../../reference/connectors/databases/README.md)
- [Database Native-to-Engine Type Mapping](../../reference/connectors/type-mapping.md)
- [MERGE Statement](../../reference/statements/dml/merge.md)
- [Automated Email Alerting](automated-email-alerting.md)
