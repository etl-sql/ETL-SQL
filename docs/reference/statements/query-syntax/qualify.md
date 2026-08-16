# QUALIFY

Filters the results of window functions directly within the current query block. Evaluated after window calculations are computed, eliminating the need for nested CTE or subquery wrappers for window filtering and deduplication.

---

## Syntax

```sql
SELECT <columns...>,
       <window_function>() OVER (
           [PARTITION BY <partition_columns...>] 
           ORDER BY <sort_columns...>
       ) AS <window_alias>
FROM <source_table>
[WHERE <row_filter_predicate>]
[GROUP BY <group_columns...>]
[HAVING <aggregate_filter_predicate>]
QUALIFY <window_predicate>;
```

---

## Execution Order & Clause Comparison

| Clause | Evaluated At | Filters Against |
| :--- | :--- | :--- |
| `WHERE` | Before aggregation | Raw input row columns |
| `HAVING` | After `GROUP BY` | Aggregate functions (`COUNT`, `SUM`, `AVG`) |
| `QUALIFY` | After Window Functions | Window functions (`ROW_NUMBER()`, `RANK()`, `LAG()`, `LEAD()`) |

---

## Examples

### 1. Top-N Records per Partition (Department Salary Ranks)

Find the top 2 highest-paid personnel in each department without wrapping the query in a subquery:

```sql
SELECT 
    employee_id, 
    full_name, 
    department, 
    salary,
    DENSE_RANK() OVER (PARTITION BY department ORDER BY salary DESC) AS sal_rank
FROM #employees
QUALIFY sal_rank <= 2;
```

### 2. Production ETL: Clean Staging Deduplication (Latest Event Per Key)

Deduplicate multi-source event streams by retaining only the single most recent record per customer ID before upserting into the data warehouse:

```sql
CREATE CONNECTION src_files AS FLATFILE(PATH='C:\data\incoming_events.csv');
CREATE CONNECTION dest_db   AS MSSQL(SERVER='dw.internal', DATABASE='analytics');

-- 1. Extract raw stream with potential duplicates
SELECT 
    customer_id,
    email,
    status,
    event_timestamp,
    ROW_NUMBER() OVER (
        PARTITION BY customer_id 
        ORDER BY event_timestamp DESC, event_id DESC
    ) AS row_num
INTO #deduplicated_staging
FROM src_files.events
WHERE event_timestamp >= '2026-08-01'
QUALIFY row_num = 1;

-- 2. Clean records are ready for direct load with zero duplicate keys
INSERT INTO dest_db.dbo.CustomerCurrentState (CustomerId, Email, Status, LastUpdated)
SELECT customer_id, email, status, event_timestamp
FROM #deduplicated_staging;
```

---

## Remarks & Best Practices

- **Alias vs. Inline Function**: You can filter against the projected column alias (e.g. `QUALIFY row_num = 1`) or declare the window expression directly inside the clause (e.g. `QUALIFY ROW_NUMBER() OVER (PARTITION BY customer_id ORDER BY event_timestamp DESC) = 1`).
- **Simpler Plans**: `QUALIFY` produces cleaner execution plans in the engine compared to wrapping statements in temporary subquery views.

---

## References & Related Recipes

- [Query Syntax Reference](README.md)
- [SELECT Statement](../dml/select.md)
- [Window Functions](window.md)
- [ROW_NUMBER Function](../../functions/window/row_number.md)
- [ETL Cookbook: Incremental Load With High-Water Mark](../../../cookbooks/etl/incremental-load-with-high-water-mark.md)
- [ETL Cookbook: Cross-Platform Reconciliation](../../../cookbooks/etl/cross-platform-reconciliation.md)
- [Syntax Index](../../../syntax-index.md)
