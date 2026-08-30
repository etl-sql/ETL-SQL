# SELECT

Retrieves, transforms, and projects rows from connections, in-memory `#temp` tables, subqueries, files, or scalar expressions. Supports inline output routing via `INTO #temp` or `INTO @variable`, named window definitions, data quality annotations, and pushdown execution.

---

## Syntax

```sql
SELECT [DISTINCT] [TOP n [PERCENT] [WITH TIES]]
  <column_expressions...>
  [INTO #destination_table | @scalar_variable]
  FROM <source_reference>
  [<join_type> JOIN <source_reference> ON <join_condition>]
  [WHERE <filter_predicate>]
  [GROUP BY [ALL] <group_by_expressions...>]
  [HAVING <group_filter_predicate>]
  [WINDOW <window_name> AS (<window_specification>) [, ...]]
  [QUALIFY <window_filter_predicate>]
  [ORDER BY [ALL] <sort_expressions...> [ASC | DESC]]
  [OFFSET n ROWS]
  [FETCH NEXT n ROWS ONLY | LIMIT n]
  [ON FAILURE <failure_action>];
```

---

## Clauses & Output Routing

| Clause | Description |
| :--- | :--- |
| `INTO #temp` | Materializes the query result into engine memory as a temporary table. |
| `INTO @variable` | Assigns a scalar query result (single row, single column) directly to a script variable. |
| `WINDOW name AS (...)` | Defines a reusable named window clause for multiple `OVER name` function projections. |
| `QUALIFY <cond>` | Filters calculated window function outputs without wrapping in a subquery or CTE. |
| `ON FAILURE ...` | Routes records violating inline `EXPECT` data quality rules (e.g. `QUARANTINE` or `WARN`). |

---

## Examples

### 1. Cross-Source Ingestion into In-Memory Staging (`INTO #temp`)

Extract records from a remote SQL Server connection, apply transformations, and stage in engine memory:

```sql
CREATE CONNECTION src AS MSSQL(SERVER='sql01.internal', DATABASE='SalesDB');

DECLARE @start_date DATE = '2026-08-01';

SELECT 
    order_id,
    customer_id,
    amount,
    currency_code,
    order_date
INTO #staged_orders
FROM src.dbo.Orders
WHERE order_date >= @start_date AND status = 'COMPLETED';

PRINT 'Staged ' + CAST(@@ROWCOUNT AS VARCHAR) + ' orders into engine memory.';
```

### 2. Scalar Variable Assignment (`INTO @variable`)

Capture summary statistics, watermarks, or record counts into session variables:

```sql
DECLARE @max_order_id INT;
DECLARE @total_revenue DECIMAL;

SELECT MAX(order_id) INTO @max_order_id FROM #staged_orders;
SELECT SUM(amount)   INTO @total_revenue FROM #staged_orders;

PRINT 'Max Order ID: ' + CAST(@max_order_id AS VARCHAR);
PRINT 'Total Staged Revenue: $' + CAST(@total_revenue AS VARCHAR);
```

### 3. Named Window Specifications & `QUALIFY`

Compute multi-tier analytical ranks across regional partitions using reusable `WINDOW` blocks:

```sql
SELECT 
    region,
    sales_rep,
    quarterly_sales,
    RANK() OVER regional_window AS sales_rank,
    AVG(quarterly_sales) OVER regional_window AS regional_average
INTO #top_performers
FROM #sales_data
WINDOW regional_window AS (PARTITION BY region ORDER BY quarterly_sales DESC)
QUALIFY sales_rank <= 3;
```

### 4. Production Data Quality Gate (`EXPECT` & `ON FAILURE`)

Enforce strict schema validation rules during ingestion; invalid rows are automatically diverted to a quarantine store:

```sql
import_customers:
SELECT 
    customer_id EXPECT NOT NULL ON FAILURE QUARANTINE,
    email       EXPECT MATCHES '^[^@]+@[^@]+$' ON FAILURE QUARANTINE,
    credit_score EXPECT BETWEEN 300 AND 850 ON FAILURE WARN,
    registered_at
INTO #clean_customers
FROM source_feed.raw_customers
ON FAILURE QUARANTINE TO #quarantine_customers;
```

### 5. String Concatenation with an Alias

Use `||` inside any scalar expression. Arithmetic binds more tightly than concatenation, and
concatenation binds more tightly than comparisons. If either operand is `NULL`, the result is `NULL`.

```sql
SELECT 'Dept ' || department_id AS department_label
FROM #departments;
```

---

## Pushdown vs. Engine Execution

- **Engine Context**: Queries against `#temp` tables, files, or multi-source cross-joins execute inside the ETL-SQL engine. Features like star modifiers (`* EXCLUDE`), `QUALIFY`, and `EXPECT` rules are evaluated in-engine.
- **Remote Pushdown**: Single-source queries against database connections (`MSSQL`, `POSTGRES`, `ORACLE`, `SNOWFLAKE`) are pushed down to the target database when syntax is compatible. Use `EXECUTE <conn> BEGIN ... END` for vendor-proprietary SQL statements.

---

## References & Related Recipes

- [DML Statements Reference](README.md)
- [Query Syntax Reference](../query-syntax/README.md)
- [Data Quality Rules](data-quality-rules.md)
- [QUALIFY](../query-syntax/qualify.md)
- [SELECT Modifiers](../query-syntax/select-modifiers.md)
- [ETL Cookbook: Staged Ingestion](../../../cookbooks/etl/staged-ingestion.md)
- [ETL Cookbook: Data Quality Gate](../../../cookbooks/etl/data-quality-gate.md)
- [ETL Cookbook: Multi-Context Join](../../../cookbooks/etl/multi-context-join.md)
- [Syntax Index](../../../syntax-index.md)
