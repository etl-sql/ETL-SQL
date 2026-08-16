# FOREACH

Iterates sequentially over a `LIST` variable, the rows of an in-memory `#temp` table, or the results of an inline `(SELECT ...)` subquery. Provides row-scoped property access via `@item.column_name`.

---

## Syntax

```sql
FOREACH @item IN <collection_reference>
BEGIN
  -- Script statements executed per item
  -- Access row properties via @item.column_name
END;
```

---

## Supported Collection Types & Binding

| Collection Source | Syntax Pattern | Value Binding in Body |
| :--- | :--- | :--- |
| `LIST` Variable | `FOREACH @item IN @list_variable` | `@item` contains scalar string / element |
| `#temp` Table | `FOREACH @row IN #temp_table` | `@row.column_name` accesses row fields |
| Inline `SELECT` | `FOREACH @row IN (SELECT ...)` | `@row.column_name` accesses projection columns |

---

## Examples

### 1. Basic Iteration over LIST and Staged Rows

```sql
-- 1. Iterate over comma-separated LIST elements
DECLARE @regions LIST = 'North,South,East,West';
FOREACH @region IN @regions
BEGIN
  PRINT 'Processing partition: ' + @region;
END;

-- 2. Iterate over structured in-memory table rows
SELECT customer_id, email, total_spend 
INTO #pending_rewards 
FROM #customers 
WHERE total_spend > 500.0;

FOREACH @cust IN #pending_rewards
BEGIN
  PRINT 'Issuing reward to: ' + @cust.email + ' (Spend: $' + CAST(@cust.total_spend AS VARCHAR) + ')';
END;
```

### 2. Production ETL: Batch File Processing & Archive Loop

Iterate over all incoming CSV files in an landing directory, ingest each into a staging table, validate, and move to an archive folder:

```sql
CREATE CONNECTION landing_dir AS DIRECTORY(PATH='C:\etl\landing');
CREATE CONNECTION dest_db     AS MSSQL(SERVER='dw.internal', DATABASE='analytics');

-- 1. Discover all unprocessed daily CSV drops
SELECT file_name, file_path, file_size_bytes 
INTO #files_to_process
FROM landing_dir.files
WHERE file_extension = '.csv' AND file_name LIKE 'orders_%';

-- 2. Process each file in an isolated batch loop
FOREACH @f IN #files_to_process
BEGIN
  PRINT 'Ingesting batch file: ' + @f.file_name;

  BEGIN TRY
    -- Ingest and stage current file
    CREATE CONNECTION current_csv AS FLATFILE(PATH=@f.file_path);
    SELECT order_id, customer_id, amount, order_date INTO #batch_stage FROM current_csv.data;

    -- Load into analytics warehouse
    INSERT INTO dest_db.dbo.FactOrders (OrderId, CustomerId, Amount, OrderDate)
    SELECT order_id, customer_id, amount, order_date FROM #batch_stage;

    -- Archive successfully processed file
    MOVE FILE @f.file_path TO 'C:\etl\archive\' + @f.file_name;
    DROP TABLE #batch_stage;
  END TRY
  BEGIN CATCH
    PRINT 'Error processing ' + @f.file_name + ': ' + ERROR_MESSAGE();
    MOVE FILE @f.file_path TO 'C:\etl\quarantine\' + @f.file_name;
  END CATCH
END;

PRINT 'Batch directory ingestion complete.';
```

---

## Loop Control & State Variables

- **`BREAK`** — Terminates the loop immediately and resumes execution at the statement following `END`.
- **`CONTINUE`** — Skips all remaining statements in the current iteration and advances to the next collection element.
- **`@@FETCH_STATUS`** — Returns `0` while elements remain in the loop, and `-1` after the final element has been evaluated.

---

## References & Related Recipes

- [Control Flow Reference](README.md)
- [WHILE Loop](while.md)
- [TRY...CATCH Error Handling](try-catch.md)
- [PARALLEL Execution](parallel.md)
- [ETL Cookbook: Batch Directory Ingestion](../../cookbooks/etl/batch-directory-ingester.md)
- [ETL Cookbook: Parallel Dimension Loader](../../cookbooks/etl/parallel-dimension-loader.md)
- [Syntax Index](../../syntax-index.md)
