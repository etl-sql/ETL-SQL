# SCD Type 2 (History Tracking)
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
