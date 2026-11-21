# Secure PII Masking & Hashing
<!-- description: **▶ Runs as-is.** Anonymize sensitive customer data with `MASK_EMAIL` and `HASHBYTES` before moving it from PROD to a Dev/QA environment. -->
Anonymize sensitive customer data for compliance before moving it from PROD to a Dev/QA environment.

## Try It Now (No Database Needed)

Paste into a `.etlsql` file and press **F5**. The built-in `MOCKDB()` users stand in for a production customer table, and `#anonymized` holds the masked and SHA-256 hashed output:

```sql
CREATE CONNECTION demo AS MOCKDB();

-- 1. Mask and hash PII into a staging table
SELECT
    UserID,
    UPPER(LEFT(UserName, 1)) + '*****'   AS UserName_Masked,
    MASK_EMAIL(Email)                    AS Email_Masked,
    HASHBYTES('SHA256', Email)           AS Email_Hash,
    GETDATE()                            AS MaskedDate
INTO #anonymized
FROM demo.Users;

-- 2. Inspect the anonymized rows (binary hashes render as 0x... hex)
SELECT TOP 5 UserID, UserName_Masked, Email_Masked, Email_Hash
FROM #anonymized
ORDER BY UserID;

PRINT 'PII masking complete: ' + CAST((SELECT COUNT(*) FROM #anonymized) AS STRING) + ' rows anonymized.';
```

All 150 users are staged into `#anonymized` with masked names, masked email addresses, and irreversible SHA-256 hashes rendered as `0x...` hex.

## Production Version

**Pattern Scenario:** Anonymize `prod.Customers` and load into `qa_env.Customers`.

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
