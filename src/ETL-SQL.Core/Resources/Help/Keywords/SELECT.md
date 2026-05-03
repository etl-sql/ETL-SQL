SELECT retrieves rows from a connection, #temp table, or inline expression. The optional INTO clause writes results to a #temp table or variable instead of returning them to the caller.

Syntax:
  SELECT [TOP n] <column_list>
    [INTO #table | @variable]
    FROM <source>
    [JOIN <source> ON <condition>]
    [WHERE <condition>]
    [GROUP BY <cols>]
    [HAVING <condition>]
    [ORDER BY <cols> [ASC | DESC]]
    [OFFSET n ROWS FETCH NEXT n ROWS ONLY];

Sources:
  #temp_table              — in-memory working set
  connection.schema.table  — qualified remote table
  (subquery)               — inline subquery

```sql
-- Load from a remote connection
SELECT order_id, customer, amount
  INTO #orders
  FROM SalesDB.dbo.Orders
  WHERE order_date >= @start;

-- Aggregate into a scalar variable
DECLARE @total DECIMAL;
SELECT SUM(amount) INTO @total FROM #orders;

-- Window function
SELECT
    customer,
    amount,
    RANK() OVER (PARTITION BY region ORDER BY amount DESC) AS rnk
  INTO #ranked
  FROM #orders;

-- Pagination with OFFSET/FETCH
SELECT name, score
  INTO #page3
  FROM #results
  ORDER BY score DESC
  OFFSET 20 ROWS FETCH NEXT 10 ROWS ONLY;
```
