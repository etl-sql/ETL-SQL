# ETL-SQL Cookbook: Production ETL Patterns

This document provides self-contained, high-fidelity recipes for real-world ETL tasks. These patterns demonstrate the full lifecycle of data movement, from inception to archival. Every recipe is runnable as-is with correctly provisioned connections.

---

## 1. The Staged Ingestion (Classical ETL)
This pattern extracts data from a remote source, stages it in the Engine workspace for validation, and performs an atomic `MERGE` into the production database.

**Pattern Scenario:** Update `Public.Customers` from a legacy Postgres source.

```sql
-- 1. Setup Infrastructure
CREATE CONNECTION pg_legacy AS POSTGRES('Host=legacy;Database=Sales;Username=etl;Password=...');
CREATE OR ALTER CONNECTION prod_sql AS MSSQL('ENC:U2FsdGVkX1+...');

BEGIN TRY
    -- 2. Extract & Stage (Isolation)
    SELECT 
        ID, 
        UPPER(FullName) AS FullName, 
        Email, 
        GETDATE() AS LastSeen 
    INTO #Staging
    FROM pg_legacy.Customers
    WHERE LastUpdate > DATEADD(DAY, -1, GETDATE());

    -- 3. Validate / Data Cleansing
    UPDATE #Staging SET Email = 'INVALID' WHERE Email NOT LIKE '%@%';

    -- 4. Atomic Load (Merge)
    MERGE INTO prod_sql.Customers AS Target
    USING #Staging AS Source ON Target.ID = Source.ID

    WHEN MATCHED THEN 
        UPDATE SET FullName = Source.FullName, LastSeen = Source.LastSeen
    
    WHEN NOT MATCHED THEN 
        INSERT (ID, FullName, Email, LastSeen)
        VALUES (Source.ID, Source.FullName, Source.Email, Source.LastSeen);

    PRINT 'Ingestion complete.';

END TRY
BEGIN CATCH
    PRINT 'CRITICAL FAILURE: ' + ERROR_MESSAGE();
    THROW;
END CATCH;
```

---

## 2. The Secure Vendor Handshake (Export & Transmit)
A robust pattern for exporting sensitive internal data, securing it, and transmitting it to a vendor SFTP.

**Pattern Scenario:** Monthly Ledger Export → Zip → Encrypt → SFTP.

```sql
-- Setup connections
-- Note: SFTP uses PASSWORD, not PASS
CREATE CONNECTION sftp_vendor AS SFTP('vendor.corp.com', USER='ext_user', PASSWORD='...');
CREATE CONNECTION ledger_out AS FLATFILE('C:\Exports\monthly_ledger.csv', HEADER=ON);

BEGIN TRY
    -- 1. Extract to local formatted file via a FLATFILE connection
    INSERT INTO ledger_out
    SELECT AccountID, Balance, Status
    FROM #ProdLedger
    WHERE MonthID = 202604;

    -- 2. Post-Processing (Archive & Secure)
    -- Note: WITH() options use = for assignment
    COMPRESS FILE 'C:\Exports\monthly_ledger.csv' TO 'C:\Exports\ledger.zip' WITH(OVERWRITE=ON);
    ENCRYPT FILE  'C:\Exports\ledger.zip' TO 'C:\Exports\ledger.zip.enc' PASSWORD('MasterSecret2026') WITH(OVERWRITE=ON);

    -- 3. Transmit
    SEND FILE 'C:\Exports\ledger.zip.enc' TO '/inbox/incoming/' AT sftp_vendor;

    -- 4. Local Cleanup
    DELETE FILE 'C:\Exports\monthly_ledger.csv';
    DELETE FILE 'C:\Exports\ledger.zip';
    
    PRINT 'Vendor transmission successful.';
END TRY
BEGIN CATCH
    -- Alert on failure — use a string expression for BODY
    SEND EMAIL
        FROM    'alerts@corp.com'
        TO      'admin@corp.com'
        SUBJECT 'VENDOR EXPORT FAILED'
        BODY    ('Export pipeline failed: ' + ERROR_MESSAGE())
        AT      alerts_smtp;
    THROW;
END CATCH;
```

---

## 3. The Batch Directory Ingester (Automation)
Processes all new files in a directory, loads them into a central store, and moves them to an archive folder.

**Pattern Scenario:** Process inbound daily CSV drops.

```sql
-- FILE_LIST takes a directory path and an optional filter as separate arguments
DECLARE @Drops LIST = FILE_LIST('C:\Inbound', '*.csv');

IF LENGTH(@Drops) = 0
BEGIN
    PRINT 'No files found. Exiting.';
    RETURN;
END

FOREACH @File IN @Drops
BEGIN
    BEGIN TRY
        -- 2. Bulk Load directly to Staging
        -- BULK INSERT uses FIRSTROW=2 to skip a header row, not HEADER=ON
        BULK INSERT #DailyRaw 
        FROM @File.Path 
        WITH (FORMAT='CSV', FIRSTROW=2, STRICT_SCHEMA=ON);
        
        -- 3. Archive the processed file
        DECLARE @ArchiveDir = 'C:\Archive\' + FORMAT(GETDATE(), 'yyyyMMdd');
        IF NOT DIRECTORY_EXISTS(@ArchiveDir)
        BEGIN
            CREATE DIRECTORY @ArchiveDir;
        END
        
        MOVE FILE @File.Path TO @ArchiveDir + '\' + @File.Name;
        
        PRINT 'Processed and Archived: ' + @File.Name;
    END TRY
    BEGIN CATCH
        PRINT 'Error processing ' + @File.Name + ': ' + ERROR_MESSAGE();
        -- Move to error folder instead of archive
        MOVE FILE @File.Path TO 'C:\Errors\' + @File.Name;
    END CATCH;
END;
```

---

## 4. The Parallel Dimension Loader
Optimizes runtime by loading independent, non-conflicting dimension tables simultaneously.

**Pattern Scenario:** High-volume refresh of data warehouse dimensions.

```sql
PARALLEL
BEGIN
    -- Branch 1: Geography
    BEGIN
        SELECT * INTO #DimGeo FROM pg.Geography;
        INSERT INTO dw.DimGeography SELECT * FROM #DimGeo;
    END

    -- Branch 2: Products
    BEGIN
        SELECT * INTO #DimProd FROM pg.Products;
        INSERT INTO dw.DimProduct SELECT * FROM #DimProd;
    END

    -- Branch 3: Currency Rates
    BEGIN
        SELECT * INTO #DimCurr FROM rates_api.Rates;
        INSERT INTO dw.DimCurrency SELECT * FROM #DimCurr;
    END
END;

PRINT 'All dimensions refreshed.';
```

> [!TIP]
> Each branch in `PARALLEL` should write to a **unique** `#temp` table name. Branches sharing a temp table name will produce undefined results.

---

## 5. SCD Type 2 (History Tracking)
Tracks changes in a dimension table by expiring old records and inserting new ones with effective dating.

```sql
-- Pattern Scenario: Manage Customer Address History
BEGIN TRANSACTION;

-- 1. Identify Changes (Source vs Target)
SELECT S.CustID, S.Address, S.City, GETDATE() AS EffectiveDate
INTO #NewVersions
FROM #Inbound AS S
JOIN prod_db.DimCustomer AS T ON S.CustID = T.CustID
WHERE T.IsCurrent = 1 AND (S.Address <> T.Address OR S.City <> T.City);

-- 2. Expire old records
UPDATE prod_db.DimCustomer 
SET IsCurrent = 0, EndDate = GETDATE()
WHERE IsCurrent = 1 AND CustID IN (SELECT CustID FROM #NewVersions);

-- 3. Insert new versions
INSERT INTO prod_db.DimCustomer (CustID, Address, City, StartDate, IsCurrent)
SELECT CustID, Address, City, EffectiveDate, 1 FROM #NewVersions;

COMMIT;
```

---

## 6. Cross-Platform Reconciliation
Compare local flat files against a remote production database to identify missing sync records.

```sql
-- Pattern Scenario: Bank Statement Reconciliation
-- Always use absolute paths for file connections
CREATE CONNECTION bank_csv    AS FLATFILE('C:\Inbox\bank_stmt.csv', HEADER=ON);
CREATE CONNECTION local_db    AS MSSQL(SERVER='Prod', DATABASE='Accounts', TRUSTED_CONNECTION=TRUE);
CREATE CONNECTION recon_out   AS FLATFILE('C:\Out\recon_errors.csv', HEADER=ON);

-- 1. Stage both sides into Engine memory
SELECT TranID, Amount INTO #Bank     FROM bank_csv;
SELECT TranID, Amount INTO #Internal FROM local_db.Transactions;

-- 2. Find Gaps (Full Outer Join)
SELECT 
    B.TranID AS BankID, 
    I.TranID AS LocalID, 
    ABS(COALESCE(B.Amount, 0) - COALESCE(I.Amount, 0)) AS Variance
INTO #ReconReport
FROM #Bank AS B
FULL OUTER JOIN #Internal AS I ON B.TranID = I.TranID
WHERE B.TranID IS NULL OR I.TranID IS NULL OR B.Amount <> I.Amount;

-- 3. Export Discrepancies
IF (SELECT COUNT(*) FROM #ReconReport) > 0
BEGIN
    INSERT INTO recon_out SELECT * FROM #ReconReport;
    PRINT 'Discrepancies exported to recon_errors.csv';
END
ELSE
    PRINT 'Reconciliation passed — no discrepancies found.';
```

---

## 7. IoT Ingestion with Regex Filtering
Filter and clean high-frequency sensor data using regular expressions before batch loading.

```sql
-- FILE_LIST: directory first, glob filter second
DECLARE @Logs LIST = FILE_LIST('C:\IoT\logs', '*.raw');

FOREACH @Log IN @Logs
BEGIN
    -- REGEXP_SUBSTR extracts a capture group match — use group index 1
    -- (REGEXP_EXTRACT is not supported; use REGEXP_SUBSTR)
    SELECT 
        CAST(REGEXP_SUBSTR(LineContent, 'ID:(\d+)', 1, 1) AS INT)       AS SensorID,
        CAST(REGEXP_SUBSTR(LineContent, 'TEMP:([\d\.]+)', 1, 1) AS FLOAT) AS Reading
    INTO #Staging
    FROM @Log.Path   -- FLATFILE connection can be created inline from a variable path
    WHERE REGEXP_LIKE(LineContent, '^\[VALID\].*TEMP:');

    INSERT INTO dw.SensorHistory SELECT * FROM #Staging;
    MOVE FILE @Log.Path TO 'C:\IoT\archive\';
END
```

---

## 8. Secure PII Masking & Hashing
Anonymize sensitive customer data for compliance before moving it from PROD to a Dev/QA environment.

```sql
CREATE CONNECTION prod     AS MSSQL(SERVER='prod-db', DATABASE='Customers', TRUSTED_CONNECTION=TRUE);
CREATE CONNECTION qa_env   AS MSSQL(SERVER='qa-db',   DATABASE='Customers', TRUSTED_CONNECTION=TRUE);

-- 1. Mask and hash into a staging table
SELECT 
    ID,
    UPPER(LEFT(LastName, 1)) + '*****'   AS LastName_Masked,
    HASHBYTES('SHA256', Email)           AS Email_Hash,  -- Irreversible
    GETDATE()                            AS MaskedDate
INTO #Anonymized
FROM prod.Customers;

-- 2. Insert into QA — use INSERT INTO, not BULK INSERT (which expects a file, not a temp table)
INSERT INTO qa_env.Customers (ID, LastName_Masked, Email_Hash, MaskedDate)
SELECT ID, LastName_Masked, Email_Hash, MaskedDate FROM #Anonymized;

PRINT 'PII masking complete: ' + CAST((SELECT COUNT(*) FROM #Anonymized) AS STRING) + ' rows anonymized.';
```

---

## 9. Multi-Context Join
Join data from three different platforms (SQL, Postgres, and CSV) in a single engine statement.

```sql
-- Pre-requisite: connections named mssql_conn, pg_conn, csv_conn must be established
CREATE CONNECTION mssql_conn AS MSSQL(SERVER='sql01', DATABASE='Sales', TRUSTED_CONNECTION=TRUE);
CREATE CONNECTION pg_conn    AS POSTGRES(HOST='pg01', DATABASE='Geo', USER='etl', PASSWORD='...');
CREATE CONNECTION csv_conn   AS FLATFILE('C:\Data\coupons.csv', HEADER=ON);

-- The engine stages each source and joins them in engine memory
SELECT 
    S.ID, S.Name, 
    P.Region, 
    C.DiscountCode
INTO #CrossPlatformResult
FROM mssql_conn.Sales AS S
JOIN pg_conn.Territories AS P ON S.TerritoryID = P.ID
JOIN csv_conn             AS C ON S.PromoID     = C.ID
WHERE S.Total > 5000;

SELECT * FROM #CrossPlatformResult ORDER BY S.ID;
```

---

## 10. Automated Slack/Teams Alerting
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

---

## 11. Financial Reporting (PIVOT)
Rotate vertical transaction logs into a horizontal quarterly summary for executive reporting.

```sql
-- Pivot rows to columns
SELECT Category, [Q1], [Q2], [Q3], [Q4]
INTO #Report
FROM (SELECT Category, Quarter, Amount FROM #MonthlySales) AS src
PIVOT (SUM(Amount) FOR Quarter IN ([Q1], [Q2], [Q3], [Q4])) AS pvt;

-- Export to Excel — always create a named connection first
CREATE CONNECTION xl_out AS EXCEL('C:\Reports\Quarterly_Summary.xlsx', HEADER=ON);
INSERT INTO xl_out SELECT * FROM #Report;

PRINT 'Quarterly report exported.';
```

---

## 12. Automated SFTP Bursting
Split a single large production table into multiple encrypted country-specific CSV files and SFTP them to separate vendor folders.

```sql
CREATE CONNECTION prod        AS MSSQL(SERVER='prod-db', DATABASE='Sales', TRUSTED_CONNECTION=TRUE);
CREATE CONNECTION vendor_sftp AS SFTP(HOST='sftp.vendor.com', USER='upload', PASSWORD='...');

DECLARE @Countries LIST = (SELECT DISTINCT Country FROM prod.Sales);

FOREACH @C IN @Countries
BEGIN
    DECLARE @OutFile     = 'C:\Exports\' + @C + '_Sales.csv';
    DECLARE @EncFile     = @OutFile + '.enc';
    DECLARE @RemotePath  = '/inbox/' + @C + '/';

    -- Create the per-country CSV connection and write
    CREATE OR ALTER CONNECTION country_out AS FLATFILE(@OutFile, HEADER=ON);
    INSERT INTO country_out SELECT * FROM prod.Sales WHERE Country = @C;

    -- Encrypt and transmit (SQL style — includes password)
    ENCRYPT FILE @OutFile TO @EncFile PASSWORD('ExportSecret2026') WITH(OVERWRITE=ON);
    SEND FILE @EncFile TO @RemotePath AT vendor_sftp;

    -- Cleanup local files
    DELETE FILE @OutFile;
    DELETE FILE @EncFile;

    PRINT 'Exported and transmitted: ' + @C;
END
```


---

## 13. Incremental Load with High-Water Mark
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

---

## 14. Full Refresh (Truncate & Reload)
The simplest load strategy — wipe the target and reload completely from source. Appropriate for small reference/dimension tables where CDC is overkill and a full copy is fast enough.

**Pattern Scenario:** Nightly full reload of a product reference table.

```sql
CREATE CONNECTION src  AS POSTGRES(HOST='pg01', DATABASE='Products', USER='etl', PASSWORD='...');
CREATE CONNECTION dest AS MSSQL(SERVER='dw01', DATABASE='Warehouse', TRUSTED_CONNECTION=TRUE);

BEGIN TRY
    BEGIN TRANSACTION;

    -- 1. Pull the full source table
    SELECT ProductId, Sku, Name, Category, Price, IsActive
    INTO #full_load
    FROM src.products
    WHERE IsActive = 1;

    -- 2. Validate we got something before destroying destination
    DECLARE @SourceCount INT = (SELECT COUNT(*) FROM #full_load);
    IF @SourceCount = 0
        THROW 'Source returned 0 rows — aborting to protect destination data.';

    -- 3. Truncate and reload atomically inside a transaction
    TRUNCATE TABLE dest.dbo.DimProduct;
    INSERT INTO dest.dbo.DimProduct SELECT * FROM #full_load;

    COMMIT;
    PRINT 'Full refresh complete: ' + CAST(@SourceCount AS STRING) + ' rows loaded.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    PRINT 'Full refresh failed: ' + ERROR_MESSAGE();
    THROW;
END CATCH;
```

---

## 15. Data Quality Gate
Assert data quality before loading. This pattern catches bad data (nulls, orphaned keys, out-of-range values) and either fails the job or routes bad rows to a quarantine table. Never load first and ask questions later.

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

---

## 16. REST API Ingestion
Pull data from a REST API and load it into a database table. The `API` connector auto-handles authentication, pagination, and JSON path extraction.

**Pattern Scenario:** Sync GitHub issues from a public repository API into a tracking database.

```sql
CREATE CONNECTION github AS API(
        URL       = 'https://api.github.com/repos/myorg/myrepo/issues',
        AUTH_TYPE = 'Bearer',
        TOKEN     = 'ENC:U2FsdGVk...',    -- GitHub Personal Access Token (encrypted)
        ROOT_PATH = '$',                   -- the response IS the array
        PAG_TYPE  = 'page',               -- GitHub uses ?page=N pagination
        PAG_LIMIT = 100
    );

CREATE CONNECTION dest AS MSSQL(SERVER='tracker-db', DATABASE='Issues', TRUSTED_CONNECTION=TRUE);

BEGIN TRY
    -- 1. Pull all open issues from the API (pagination handled automatically)
    SELECT
        id          AS IssueId,
        number      AS IssueNumber,
        title       AS Title,
        state       AS State,
        created_at  AS CreatedAt,
        updated_at  AS UpdatedAt
    INTO #issues
    FROM github;

    PRINT 'Retrieved ' + CAST((SELECT COUNT(*) FROM #issues) AS STRING) + ' issues from API.';

    -- 2. Upsert into the tracking table
    MERGE INTO dest.dbo.GitHubIssues AS T
    USING #issues AS S ON T.IssueId = S.IssueId
    WHEN MATCHED AND S.UpdatedAt > T.UpdatedAt THEN
        UPDATE SET T.Title = S.Title, T.State = S.State, T.UpdatedAt = S.UpdatedAt
    WHEN NOT MATCHED THEN
        INSERT (IssueId, IssueNumber, Title, State, CreatedAt, UpdatedAt)
        VALUES (S.IssueId, S.IssueNumber, S.Title, S.State, S.CreatedAt, S.UpdatedAt);

    PRINT 'Issue sync complete.';
END TRY
BEGIN CATCH
    PRINT 'API ingestion failed: ' + ERROR_MESSAGE();
    THROW;
END CATCH;
```

---

## 17. Dead-Letter Queue (Error Row Routing)
Instead of failing an entire load when individual rows are bad, route problem rows to a dead-letter table for later inspection and reprocessing. Good rows continue loading unaffected.

**Pattern Scenario:** Process an order feed where some rows have invalid product codes.

```sql
CREATE CONNECTION src  AS MSSQL(SERVER='src', DATABASE='Orders',   TRUSTED_CONNECTION=TRUE);
CREATE CONNECTION dest AS MSSQL(SERVER='dw',  DATABASE='Warehouse', TRUSTED_CONNECTION=TRUE);
CREATE CONNECTION dlq  AS MSSQL(SERVER='dw',  DATABASE='Warehouse', TRUSTED_CONNECTION=TRUE);

-- 1. Stage the inbound feed
SELECT * INTO #inbound FROM src.dbo.OrderFeed WHERE Processed = 0;

-- 2. Separate good rows from bad rows
SELECT o.*
INTO #good_rows
FROM #inbound AS o
WHERE EXISTS (SELECT 1 FROM dest.dbo.DimProduct WHERE ProductCode = o.ProductCode)
  AND o.Quantity > 0
  AND o.UnitPrice >= 0;

SELECT o.*, 'INVALID_PRODUCT_OR_QUANTITY' AS ErrorReason
INTO #bad_rows
FROM #inbound AS o
WHERE NOT EXISTS (SELECT 1 FROM dest.dbo.DimProduct WHERE ProductCode = o.ProductCode)
   OR o.Quantity <= 0
   OR o.UnitPrice < 0;

-- 3. Load good rows to destination
IF (SELECT COUNT(*) FROM #good_rows) > 0
BEGIN
    INSERT INTO dest.dbo.FactOrders
    SELECT OrderId, ProductCode, Quantity, UnitPrice, OrderDate FROM #good_rows;
    PRINT 'Loaded: ' + CAST((SELECT COUNT(*) FROM #good_rows) AS STRING) + ' good rows.';
END

-- 4. Route bad rows to dead-letter queue
IF (SELECT COUNT(*) FROM #bad_rows) > 0
BEGIN
    INSERT INTO dlq.dbo.OrderDLQ (ReceivedAt, ErrorReason, OrderId, ProductCode, Quantity, UnitPrice)
    SELECT GETDATE(), ErrorReason, OrderId, ProductCode, Quantity, UnitPrice FROM #bad_rows;
    PRINT 'Dead-lettered: ' + CAST((SELECT COUNT(*) FROM #bad_rows) AS STRING) + ' bad rows — inspect dlq.dbo.OrderDLQ.';
END

-- 5. Mark all inbound rows as processed regardless
UPDATE src.dbo.OrderFeed SET Processed = 1
WHERE OrderId IN (SELECT OrderId FROM #inbound);
```

---

## 18. Dynamic SQL with EXEC
Build and execute SQL statements at runtime — essential for parameterized table names, dynamic column lists, and multi-tenant pipelines where the schema varies per client.

**Pattern Scenario:** Archive orders for each tenant into their own table.

```sql
CREATE CONNECTION orders_db AS MSSQL(SERVER='multi-db', DATABASE='Orders', TRUSTED_CONNECTION=TRUE);

DECLARE @Tenants LIST = (SELECT DISTINCT TenantId FROM orders_db.dbo.Orders);

FOREACH @Tenant IN @Tenants
BEGIN
    -- Build the archive table name dynamically
    DECLARE @ArchiveTable = 'Archive_' + @Tenant + '_Orders';
    DECLARE @ArchiveYear  = CAST(YEAR(GETDATE()) AS STRING);

    -- Dynamic DDL — create the archive table if it doesn't exist
    DECLARE @CreateSql = 
        'IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = ''' + @ArchiveTable + ''') ' +
        'CREATE TABLE ' + @ArchiveTable + ' (OrderId INT, Amount DECIMAL(18,2), OrderDate DATETIME, ArchivedAt DATETIME);';

    EXEC (@CreateSql) AT orders_db;   -- Execute against the remote connection

    -- Dynamic INSERT — archive this tenant's old orders
    DECLARE @InsertSql =
        'INSERT INTO ' + @ArchiveTable + ' (OrderId, Amount, OrderDate, ArchivedAt) ' +
        'SELECT OrderId, Amount, OrderDate, GETDATE() ' +
        'FROM dbo.Orders ' +
        'WHERE TenantId = ''' + @Tenant + ''' AND YEAR(OrderDate) < ' + @ArchiveYear + ';';

    EXEC (@InsertSql) AT orders_db;

    PRINT 'Archived orders for tenant: ' + @Tenant;
END

-- Local dynamic execution example (runs in engine context, not remote)
DECLARE @LocalSql = 'SELECT COUNT(*) AS TotalArchived FROM #summary;';
EXEC @LocalSql;   -- No ON clause = runs locally against engine temp tables
```

> [!IMPORTANT]
> `EXEC sql_string ON connection` executes against a remote database. `EXEC sql_string` (no `ON`) parses and runs the string locally in engine context — able to access `#temp` tables and `@variables`. Both forms support `INTO #temp` to capture results.

---

## 19. Master-Detail Cross-Report Drill-through
The most powerful interactive pattern. It allows navigating from a high-level summary report to a completely separate, detailed report file while passing context (like a Region or Order ID) via parameters.

**Pattern Scenario:** High-level Sales Summary → Detailed Regional Transaction Log.

### Summary Report (`summary.rptsql`)
```sql
-- 1. Data Source
CREATE DATASET &SalesSummary AS (
    SELECT Region, SUM(Sales) AS TotalSales FROM #raw GROUP BY Region
);

-- 2. Master Visual
CREATE VISUAL RegionTable AS TABLE (
    SOURCE = &SalesSummary,
    MAPPINGS (COLUMN Region = Region, COLUMN Sales = TotalSales),
    ACTIONS (
        -- Cross-report drill
        ON_CLICK = DRILL_REPORT (
            REPORT = 'SalesDetail',  -- Name defined in reports.json
            PARAMETERS ( @TargetRegion = Region )
        )
    )
);

CREATE PAGE Main AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = RegionTable));
```

### Detail Report (`regional_detail.rptsql`)
```sql
-- 1. Declare the input parameter
DECLARE @TargetRegion STRING INPUT = 'All';

-- 2. Data Source filtered by input
CREATE DATASET &Transactions AS (
    SELECT * FROM #all_tx WHERE Region = @TargetRegion OR @TargetRegion = 'All'
);

-- 3. Detail Visual
CREATE VISUAL TxTable AS TABLE (
    SOURCE = &Transactions,
    TITLE  = ('Transactions for: ' + @TargetRegion)
);

CREATE PAGE Main AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = TxTable));
```

---

## 20. Immutable Published Script Bundles (CI/CD Deployment)
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
    -- Pinned version resolution occurs at job creation time (e.g. resolves to version 1)
    EXECUTE local_orch BEGIN
        CREATE JOB RunFinanceReconciliation
            ON SCHEDULE EVERY 1 DAY AT '02:30'
            WITH (MAX_RETRIES = 3, RETRY_DELAY = 60)
        AS
            RUN SCRIPT 'orch://finance-pipeline/main.etlsql';
    END;

    PRINT 'Bundle published and job scheduled successfully.';

END TRY
BEGIN CATCH
    PRINT 'Deployment Failed: ' + ERROR_MESSAGE();
    THROW;
END CATCH;
```

---

*Refer to [Reference/Standard_Library.md](Reference/Standard_Library.md) for function signatures, [Reference/Data_Connectors.md](Reference/Data_Connectors.md) for connector options, and [User_Manual.md](User_Manual.md) for the mental model.*
