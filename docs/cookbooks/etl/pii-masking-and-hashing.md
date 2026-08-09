# Secure PII Masking & Hashing
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
