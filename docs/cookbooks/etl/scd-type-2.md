# SCD Type 2 (History Tracking)
Tracks changes in a dimension table by expiring old records and inserting new ones with effective dating.

```sql
-- Pattern Scenario: Manage Customer Address History
-- Assumes: a prod_db connection, and #Inbound (CustID, Address, City) staged by a prior step.
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

-- 4. Insert brand-new customers (no row in the dimension yet)
INSERT INTO prod_db.DimCustomer (CustID, Address, City, StartDate, IsCurrent)
SELECT S.CustID, S.Address, S.City, GETDATE(), 1
FROM #Inbound AS S
WHERE NOT EXISTS (SELECT 1 FROM prod_db.DimCustomer AS T WHERE T.CustID = S.CustID);

COMMIT;
```
