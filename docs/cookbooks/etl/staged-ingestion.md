# The Staged Ingestion (Classical ETL)
<!-- description: **▶ Runs as-is.** Extract from a remote source, stage and validate in the engine workspace, then `MERGE` atomically into the production table. -->
This pattern extracts data from a remote source, stages it in the Engine workspace for validation, and performs an atomic `MERGE` into the production database.

## Try It Now (No Database Needed)

Paste into a `.etlsql` file and press **F5**. The built-in `MOCKDB()` users stand in for the legacy source, and `#customers` for the production table:

```sql
CREATE CONNECTION demo AS MOCKDB();

-- Target table that already holds the first 10 users
SELECT UserID AS ID, UserName AS FullName, Email, RegistrationDate AS LastSeen
INTO #customers
FROM demo.Users
WHERE UserID <= 10;

-- 1. Extract & stage
SELECT UserID AS ID, UPPER(UserName) AS FullName, Email, GETDATE() AS LastSeen
INTO #staging
FROM demo.Users;

-- 2. Validate / cleanse in the engine workspace
UPDATE #staging SET Email = 'INVALID' WHERE Email NOT LIKE '%@%';

-- 3. Merge: update the 10 existing customers, insert the rest
MERGE INTO #customers AS Target
USING #staging AS Source ON Target.ID = Source.ID
WHEN MATCHED THEN
    UPDATE SET FullName = Source.FullName, LastSeen = Source.LastSeen
WHEN NOT MATCHED THEN
    INSERT (ID, FullName, Email, LastSeen)
    VALUES (Source.ID, Source.FullName, Source.Email, Source.LastSeen);

SELECT COUNT(*) AS CustomerRows FROM #customers;
SELECT TOP 5 ID, FullName, Email FROM #customers ORDER BY ID;
```

The 10 existing customers are updated and the other 140 inserted, so `#customers` ends with 150 rows. The production version reads from Postgres and merges into SQL Server inside `TRY...CATCH`:

## Production Version

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
