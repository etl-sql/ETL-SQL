# SELECT
SELECT retrieves rows from a connection, `#temp` table, subquery, or inline expression. Use `INTO` to write results to a `#temp` table or variable instead of returning them to the caller.

```sql
SELECT [DISTINCT] [TOP n [PERCENT] [WITH TIES]]
  <column_list>
  [INTO #table | @variable]
  FROM <source>
  [JOIN <source> ON <condition>]
  [WHERE <condition>]
  [GROUP BY <cols>]
  [HAVING <condition>]
  [WINDOW <name> AS (<window_spec>) [, ...]]
  [QUALIFY <condition>]
  [ORDER BY <cols> [ASC | DESC]]
  [OFFSET n ROWS]
  [FETCH NEXT n ROWS ONLY]
  [LIMIT n];
```

- **`#temp_table`** - In-memory engine working set.
- **`connection.schema.table`** - Qualified remote table.
- **`(subquery)`** - Inline subquery.
- **`WINDOW name AS (...)`** - Reusable named window specification for `OVER name` or `OVER (name ...)`.

```sql
SELECT order_id, customer, amount
  INTO #orders
  FROM SalesDB.dbo.Orders
  WHERE order_date >= @start;
```

```sql
SELECT
  customer,
  amount,
  RANK() OVER regional_amounts AS rnk
INTO #ranked
FROM #orders
WINDOW regional_amounts AS (PARTITION BY region ORDER BY amount DESC)
QUALIFY rnk <= 10;
```

```sql
-- Validate column values inline; failing rows are quarantined instead of loaded
import_users:
SELECT
  UserId /* @expect: 'NOT NULL'; @fail: 'QUARANTINE'; */,
  Email  /* @expect: 'MATCHES ^[^@]+@[^@]+$'; @fail: 'QUARANTINE'; */
INTO clean_users
FROM raw_users
ON FAILURE QUARANTINE TO quarantine_users WITH (RETENTION = '30 DAYS');
```

References:
- [Statements](../README.md)
- [Data Quality Rules](data-quality-rules.md)
