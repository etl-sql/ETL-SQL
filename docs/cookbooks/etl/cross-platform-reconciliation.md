# Cross-Platform Reconciliation
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
