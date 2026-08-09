# The Staged Ingestion (Classical ETL)
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
