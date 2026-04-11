# ETL-SQL Cookbook: Production ETL Patterns

This document provides self-contained, high-fidelity recipes for real-world ETL tasks. These patterns demonstrate the full lifecycle of data movement, from inception to archival.

---

## 1. The Staged Ingestion (Classical ETL)
This pattern extract data from a remote source, stages it in the Engine workspace for validation, and performs an atomic `MERGE` into the production database.

**Pattern Scenario:** Update `Public.Customers` from a legacy Postgres source.

```sql
-- 1. Setup Infrastructure
CREATE CONNECTION pg_legacy ON POSTGRES('Host=legacy;DB=Sales');
CREATE OR ALTER CONNECTION prod_sql ON MSSQL('ENC:U2FsdGVkX1+...');

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

    PRINT 'Ingestion complete: ' + CAST(@@ROWCOUNT AS STRING) + ' rows processed.';

END TRY
BEGIN CATCH
    PRINT 'CRITICAL FAILURE: ' + ERROR_MESSAGE();
    THROW;
END CATCH;
```

---

## 2. The Secure Vendor Handshake (Export & Transmit)
A robust pattern for exporting sensitive internal data, securing it, and transmitting it to a vendor SFTP.

**Pattern Scenario:** Monthly Ledger Export -> Zip -> Encrypt -> SFTP.

```sql
-- Setup connections
CREATE CONNECTION sftp_vendor ON SFTP('vendor.corp.com') WITH(USER='ext_user', PASS='...');

BEGIN TRY
    -- 1. Extract to local formatted file
    SELECT AccountID, Balance, Status 
    INTO LocalFile.Ledger('C:\Exports\monthly_ledger.csv')
    FROM #ProdLedger -- Sourcing from an engine temp table
    WHERE MonthID = 202604;

    -- 2. Post-Processing (Archive & Secure)
    COMPRESS_FILE 'C:\Exports\monthly_ledger.csv', 'C:\Exports\ledger.zip' OVERWRITE ON;
    ENCRYPT_FILE 'C:\Exports\ledger.zip', 'C:\Exports\ledger.zip.enc' PASS 'MasterSecret2026' OVERWRITE ON;

    -- 3. Transmit
    SEND FILE 'C:\Exports\ledger.zip.enc' TO '/inbox/incoming/' AT sftp_vendor;

    -- 4. Local Cleanup
    DELETE FILE 'C:\Exports\monthly_ledger.csv';
    DELETE FILE 'C:\Exports\ledger.zip';
    
    PRINT 'Vendor transmission successful.';
END TRY
BEGIN CATCH
    -- Alert on failure
    SEND EMAIL TO 'admin@corp.com' SUBJECT 'VENDOR EXPORT FAILED' BODY ERROR_MESSAGE();
    THROW;
END CATCH;
```

---

## 3. The Batch Directory Ingester (Automation)
Processes all new files in a directory, loads them into a central store, and moves them to an archive folder.

**Pattern Scenario:** Process inbound daily CSV drops.

```sql
-- 1. Get the list of pending drops
DECLARE @Drops LIST = FILE_LIST('C:\Inbound\*.csv');

IF LENGTH(@Drops) = 0
BEGIN
    PRINT 'No files found. Exiting.';
    RETURN;
END

FOREACH @File IN @Drops
BEGIN
    BEGIN TRY
        -- 2. Bulk Load directly to Staging
        BULK INSERT #DailyRaw 
        FROM @File.Path 
        WITH (FORMAT = 'CSV', HEADER = ON, STRICT_SCHEMA = ON);
        
        -- 3. Archive the processed file
        DECLARE @ArchiveDir = 'C:\Archive\' + FORMAT(GETDATE(), 'yyyyMMdd');
        IF NOT DIRECTORY_EXISTS(@ArchiveDir) CREATE DIRECTORY @ArchiveDir;
        
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
        SELECT * INTO #DimCurr FROM api.Rates;
        INSERT INTO dw.DimCurrency SELECT * FROM #DimCurr;
    END
END;

PRINT 'All dimensions refreshed.';
```

---

## 5. SCD Type 2 (History Tracking)
Tracks changes in a dimension table by expiring old records and inserting new ones with effective dating.

```sql
-- Pattern Scenario: Manage Customer Address History
BEGIN TRANSACTION;

-- 1. Identify Changes (Source vs Target)
SELECT S.CustID, S.Address, S.City, GETDATE() AS EffectiveDate
INTO #NewVersions
FROM #Inbound S
JOIN DimCustomer T ON S.CustID = T.CustID
WHERE T.IsCurrent = 1 AND (S.Address <> T.Address OR S.City <> T.City);

-- 2. Expire old records
UPDATE DimCustomer 
SET IsCurrent = 0, EndDate = GETDATE()
WHERE IsCurrent = 1 AND CustID IN (SELECT CustID FROM #NewVersions);

-- 3. Insert new versions
INSERT INTO DimCustomer (CustID, Address, City, StartDate, IsCurrent)
SELECT CustID, Address, City, EffectiveDate, 1 FROM #NewVersions;

COMMIT;
```

---

## 6. Cross-Platform Reconciliation
Compare local flat files against a remote production database to identify missing sync records.

```sql
-- Pattern Scenario: Bank Statement Reconciliation
CREATE CONNECTION bank_csv ON FLATFILE('inbox/bank_stmt.csv');
CREATE CONNECTION local_db ON MSSQL('Server=Prod;DB=Accounts');

-- 1. Stage both sides into Engine memory
SELECT TranID, Amount INTO #Bank FROM bank_csv.Main;
SELECT TranID, Amount INTO #Internal FROM local_db.Transactions;

-- 2. Find Gaps (Full Outer Join)
SELECT 
    B.TranID AS BankID, 
    I.TranID AS LocalID, 
    ABS(COALESCE(B.Amount,0) - COALESCE(I.Amount,0)) AS Variance
INTO #ReconReport
FROM #Bank B
FULL OUTER JOIN #Internal I ON B.TranID = I.TranID
WHERE B.TranID IS NULL OR I.TranID IS NULL OR B.Amount <> I.Amount;

-- 3. Export Discrepancies
IF (SELECT COUNT(*) FROM #ReconReport) > 0
    SELECT * INTO LocalFile.Report('out/recon_error.csv') FROM #ReconReport;
```

---

## 7. IoT Ingestion with Regex Filtering
Filter and clean high-frequency sensor data using regular expressions before batch loading.

```sql
DECLARE @Logs LIST = FILE_LIST('iot/logs/*.raw');

FOREACH @Log IN @Logs
BEGIN
    -- Load only lines matching the 'TEMP_VAL: [digits]' pattern
    SELECT 
        REGEXP_EXTRACT(LineContent, 'ID:(\d+)', 1) AS SensorID,
        CAST(REGEXP_EXTRACT(LineContent, 'TEMP:([\d\.]+)', 1) AS FLOAT) AS Reading
    INTO #Staging
    FROM FLATFILE(@Log.Path)
    WHERE REGEXP_LIKE(LineContent, '^\[VALID\].*TEMP:');

    INSERT INTO dw.SensorHistory SELECT * FROM #Staging;
    MOVE FILE @Log.Path TO 'iot/archive/';
END
```

---

## 8. Secure PII Masking & Hashing
Anonymize sensitive customer data for compliance before moving it from PROD to a Dev/QA environment.

```sql
SELECT 
    ID,
    UPPER(LEFT(LastName, 1)) + '*****' AS LastName_Masked,
    HASHBYTES('SHA256', Email) AS Email_Hash, -- Irreversible
    GETDATE() AS MaskedDate
INTO #Anonymized
FROM prod.Customers;

BULK INSERT qa_env.Customers FROM #Anonymized;
```

---

## 9. Multi-Context Join
Join data from three different platforms (SQL, Postgres, and CSV) in a single engine statement.

```sql
SELECT 
    S.ID, S.Name, 
    P.Region, 
    C.DiscountCode
FROM mssql_conn.Sales S
JOIN pg_conn.Territories P ON S.TerritoryID = P.ID
JOIN csv_conn.Coupons C ON S.PromoID = C.ID
WHERE S.Total > 5000;
```

---

## 10. Automated Slack/Teams Alerting
Centralized error reporting pattern using the `SEND EMAIL` block configured for webhook-style SMTP.

```sql
CREATE PROCEDURE NotifyTeam @Msg STRING, @Level STRING
AS
BEGIN
    DECLARE @Subj = '[' + @Level + '] ETL Pipeline Alert';
    SEND EMAIL 
        TO 'dev-alerts@company.slack.com'
        SUBJECT @Subj
        BODY @Msg
        AT alerts_smtp;
END;

-- Usage in a script
IF @@TRANCOUNT > 0 ROLLBACK;
EXEC NotifyTeam 'Nightly Load Failed at Stage 3', 'CRITICAL';
```

---

## 11. Financial Reporting (PIVOT)
Rotate vertical transaction logs into a horizontal quarterly summary for executive reporting.

```sql
SELECT Category, [Q1], [Q2], [Q3], [Q4]
INTO #Report
FROM (SELECT Category, Quarter, Amount FROM #MonthlySales) src
PIVOT (SUM(Amount) FOR Quarter IN ([Q1], [Q2], [Q3], [Q4])) pvt;

SELECT * INTO LocalFile.Excel('reports/Quarterly_Summary.xlsx') FROM #Report;
```

---

## 12. Automated SFTP Bursting
Split a single large production table into multiple encrypted country-specific CSV files and send them to separate vendor folders.

```sql
DECLARE @Countries LIST = (SELECT DISTINCT Country FROM prod.Sales);

FOREACH @C IN @Countries
BEGIN
    DECLARE @OutFile = 'export/' + @C + '_Sales.csv';
    
    SELECT * INTO LocalFile.CSV(@OutFile) FROM prod.Sales WHERE Country = @C;
    
    ENCRYPT_FILE @OutFile, @OutFile + '.enc' PASS 'Secret' OVERWRITE ON;
    
    SEND FILE @OutFile + '.enc' TO '/inbox/' + @C + '/' AT vendor_sftp;
    
    DELETE FILE @OutFile;
    DELETE FILE @OutFile + '.enc';
END
```

---
*Refer to [Reference/Standard_Library.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Standard_Library.md) for function signatures and [User_Manual.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/User_Manual.md) for the mental model.*
