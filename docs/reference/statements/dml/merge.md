# MERGE

Atomic, multi-action upsert statement. Evaluates source records against a target table using matching keys, conditionally updating existing rows, inserting new rows, and optionally deleting target rows that no longer exist in the incoming source dataset.

---

## Syntax

```sql
MERGE INTO <target_table> AS tgt
USING <source_table_or_subquery> AS src 
  ON <join_conditions...>
WHEN MATCHED [AND <update_filter_predicate>] THEN
  UPDATE SET tgt.col1 = src.col1, tgt.col2 = src.col2, ...
WHEN NOT MATCHED THEN
  INSERT (col1, col2, ...) VALUES (src.col1, src.col2, ...)
[WHEN NOT MATCHED BY SOURCE [AND <delete_filter_predicate>] THEN
  DELETE];
```

---

## Clauses & Supported Actions

| Clause | Trigger Condition | Action | Typical Use |
| :--- | :--- | :--- | :--- |
| `WHEN MATCHED` | Record exists in both source and target on `ON` key | `UPDATE SET ...` | Synchronize modified attributes |
| `WHEN NOT MATCHED` | Record exists in source but not in target | `INSERT (...) VALUES (...)` | Ingest new entities |
| `WHEN NOT MATCHED BY SOURCE` | Record exists in target but omitted from source | `DELETE` | Full table synchronization / prune stale records |

---

## Examples

### 1. Basic In-Memory Staging Upsert

```sql
MERGE INTO dbo.Inventory AS tgt
USING #incoming_stock AS src 
  ON tgt.sku = src.sku
WHEN MATCHED AND src.quantity <> tgt.quantity THEN
  UPDATE SET tgt.quantity = src.quantity, tgt.last_restocked = GETDATE()
WHEN NOT MATCHED THEN
  INSERT (sku, quantity, last_restocked) 
  VALUES (src.sku, src.quantity, GETDATE());

PRINT 'Updated inventory rows: ' + CAST(@@ROWCOUNT AS VARCHAR);
```

### 2. Production SCD Pipeline with WHAT_IF & Error Boundary

Sync customer records from an external Postgres database into an MSSQL analytics warehouse, protecting against erroneous batch deletions:

```sql
CREATE CONNECTION pg AS POSTGRES(HOST='crm.internal', DATABASE='customers');
CREATE CONNECTION dw AS MSSQL(SERVER='dw.internal', DATABASE='analytics');

BEGIN TRY
    -- 1. Extract and cleanse into staging
    SELECT 
        id AS customer_id,
        LOWER(TRIM(email)) AS email,
        TRIM(name) AS customer_name,
        account_tier,
        updated_at
    INTO #staging_customers
    FROM pg.users
    WHERE is_test_account = 0;

    -- 2. Optional: Validate delta impacts safely in simulation mode
    SET WHAT_IF ON;
    MERGE INTO dw.dbo.DimCustomers AS T
    USING #staging_customers AS S ON T.CustomerId = S.customer_id
    WHEN MATCHED AND S.updated_at > T.UpdatedAt THEN
        UPDATE SET T.Email = S.email, T.CustomerName = S.customer_name, T.AccountTier = S.account_tier, T.UpdatedAt = S.updated_at
    WHEN NOT MATCHED THEN
        INSERT (CustomerId, Email, CustomerName, AccountTier, UpdatedAt)
        VALUES (S.customer_id, S.email, S.customer_name, S.account_tier, S.updated_at);
    SET WHAT_IF OFF;

    -- 3. Execute atomic upsert in live transaction
    BEGIN TRANSACTION;
        MERGE INTO dw.dbo.DimCustomers AS T
        USING #staging_customers AS S ON T.CustomerId = S.customer_id
        WHEN MATCHED AND S.updated_at > T.UpdatedAt THEN
            UPDATE SET T.Email = S.email, T.CustomerName = S.customer_name, T.AccountTier = S.account_tier, T.UpdatedAt = S.updated_at
        WHEN NOT MATCHED THEN
            INSERT (CustomerId, Email, CustomerName, AccountTier, UpdatedAt)
            VALUES (S.customer_id, S.email, S.customer_name, S.account_tier, S.updated_at);
    COMMIT;

    PRINT 'Customer synchronization complete.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    PRINT 'MERGE transaction rolled back due to error: ' + ERROR_MESSAGE();
    THROW;
END CATCH
```

---

## Security & Best Practices

- **Atomic Staging Pattern**: Always stage remote data in `#temp` memory before executing a `MERGE` across database boundaries.
- **Destructive Operation Guards**: Always validate queries containing `WHEN NOT MATCHED BY SOURCE THEN DELETE` with `SET WHAT_IF ON` before promoting to unattended production schedules.

---

## References & Related Recipes

- [DML Statements Reference](README.md)
- [INSERT](insert.md) · [UPDATE](update.md) · [DELETE](delete.md)
- [Transaction Control](../session-control/transaction.md)
- [ETL Cookbook: SCD Type 2](../../../cookbooks/etl/scd-type-2.md)
- [ETL Cookbook: Cross-Platform Reconciliation](../../../cookbooks/etl/cross-platform-reconciliation.md)
- [ETL Cookbook: Incremental Load With High-Water Mark](../../../cookbooks/etl/incremental-load-with-high-water-mark.md)
- [Syntax Index](../../../syntax-index.md)
