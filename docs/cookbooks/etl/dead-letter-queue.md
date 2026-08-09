# Dead-Letter Queue (Error Row Routing)
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
