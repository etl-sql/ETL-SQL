# SCD Type 2 (History Tracking)
<!-- description: **▶ Runs as-is.** Track changes in a dimension table by expiring old records and inserting new ones with effective dating. -->
Tracks changes in a dimension table by expiring old records and inserting new ones with effective dating.

## Try It Now (No Database Needed)

Paste into a `.etlsql` file and press **F5**. The built-in `MOCKDB()` users seed the dimension, and `#Inbound` is the next feed: two users changed email, one did not, and one is new.

```sql
CREATE CONNECTION demo AS MOCKDB();

-- The dimension as of the last load: one current row per user
SELECT UserID, Email, CAST('2026-01-01' AS DATE) AS StartDate, CAST(NULL AS DATE) AS EndDate, 1 AS IsCurrent
INTO #DimUser
FROM demo.Users
WHERE UserID <= 3;

-- Today's feed
CREATE TABLE #Inbound (UserID INT, Email VARCHAR(100));
INSERT INTO #Inbound VALUES
    (1, 'user1@new-domain.com'),   -- changed
    (2, 'user2@new-domain.com'),   -- changed
    (3, 'user3@example.com'),      -- unchanged
    (151, 'user151@example.com');  -- new

DECLARE @Today DATE = CAST(GETDATE() AS DATE);

-- 1. Identify changes against the current version
SELECT S.UserID, S.Email
INTO #NewVersions
FROM #Inbound AS S
JOIN #DimUser AS T ON S.UserID = T.UserID
WHERE T.IsCurrent = 1 AND S.Email <> T.Email;

-- 2. Expire the old versions
UPDATE #DimUser
SET IsCurrent = 0, EndDate = @Today
WHERE IsCurrent = 1 AND UserID IN (SELECT UserID FROM #NewVersions);

-- 3. Insert the new versions
INSERT INTO #DimUser (UserID, Email, StartDate, EndDate, IsCurrent)
SELECT UserID, Email, @Today, NULL, 1 FROM #NewVersions;

-- 4. Insert brand-new users
INSERT INTO #DimUser (UserID, Email, StartDate, EndDate, IsCurrent)
SELECT S.UserID, S.Email, @Today, NULL, 1
FROM #Inbound AS S
WHERE NOT EXISTS (SELECT 1 FROM #DimUser AS T WHERE T.UserID = S.UserID);

SELECT UserID, Email, IsCurrent, EndDate IS NULL AS IsOpen
FROM #DimUser
ORDER BY UserID, IsCurrent;
```

Users 1 and 2 now have two rows each: the expired version (`IsCurrent = 0`, `EndDate` set) and the current one. User 3 is untouched and user 151 is added. Run it again with a different email for user 3 to see a third history row appear. The production version runs the same steps against a real dimension, inside a transaction:

## Production Version

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
