# Dead-Letter Queue (Error Row Routing)
<!-- description: **▶ Runs as-is.** Route bad rows to a dead-letter table with a reason for later reprocessing, while good rows keep loading. -->
Instead of failing an entire load when individual rows are bad, route problem rows to a dead-letter table for later inspection and reprocessing. Good rows continue loading unaffected.

## Try It Now (No Database Needed)

Paste into a `.etlsql` file and press **F5**. The built-in `MOCKDB()` connector supplies orders and a product dimension:

```sql
CREATE CONNECTION demo AS MOCKDB();

-- 1. Stage the inbound feed and the product dimension
SELECT SaleID, ProductID, Quantity, UnitPrice, OrderDate INTO #inbound FROM demo.Orders;
-- Discontinued products are not in the active dimension, so their orders are rejected
SELECT ProductID INTO #dim_product FROM demo.Products WHERE Discontinued = 0;

-- 2. Split: rows with a known product load; the rest go to the dead-letter table
SELECT o.SaleID, o.ProductID, o.Quantity, o.UnitPrice, o.OrderDate
INTO #good_rows
FROM #inbound AS o
WHERE EXISTS (SELECT 1 FROM #dim_product AS p WHERE p.ProductID = o.ProductID);

SELECT GETDATE() AS ReceivedAt, 'UNKNOWN_OR_DISCONTINUED_PRODUCT' AS ErrorReason,
       o.SaleID, o.ProductID, o.Quantity, o.UnitPrice
INTO #dead_letter
FROM #inbound AS o
WHERE NOT EXISTS (SELECT 1 FROM #dim_product AS p WHERE p.ProductID = o.ProductID);

PRINT 'Loaded: ' + CAST((SELECT COUNT(*) FROM #good_rows) AS STRING) + ' rows. Dead-lettered: '
    + CAST((SELECT COUNT(*) FROM #dead_letter) AS STRING) + ' rows.';
SELECT ReceivedAt, ErrorReason, SaleID, ProductID, Quantity FROM #dead_letter;
```

Orders for discontinued products fail the lookup and are routed to `#dead_letter` with a reason, while the rest continue. In production, write the dead-letter rows to a durable table so they can be fixed and reprocessed:

## Production Version

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
