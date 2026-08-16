# TRY...CATCH

Structured exception handling block. Intercepts runtime exceptions occurring within the `BEGIN TRY` block, transfers execution immediately to `BEGIN CATCH`, and exposes error diagnostic functions for logging, cleanup, transactional rollback, and alert routing.

---

## Syntax

```sql
BEGIN TRY
  -- Statements executed under exception protection
END TRY
BEGIN CATCH
  -- Statements executed when a runtime error occurs in the TRY block
END CATCH;
```

---

## Diagnostic Functions & Session Variables

The following diagnostic functions and session variables are populated inside the `BEGIN CATCH` block:

| Identifier | Return Type | Description |
| :--- | :--- | :--- |
| `ERROR_MESSAGE()` | `VARCHAR` | Complete descriptive text of the exception |
| `ERROR_NUMBER()` | `INT` | Specific error code or error number |
| `ERROR_LINE()` | `INT` | Line number in the script where the exception was thrown |
| `ERROR_STATE()` | `INT` | Engine state code associated with the error |
| `@@ERROR` | `INT` | Non-zero integer error code (`0` if no error occurred) |
| `@@TRANCOUNT` | `INT` | Number of active transactions; use to determine if `ROLLBACK` is needed |

---

## Examples

### 1. Basic Ingestion Guard with Staging Table Fallback

```sql
BEGIN TRY
  SELECT customer_id, email INTO #incoming FROM remote_crm.customers;
  PRINT 'Successfully ingested ' + CAST(@@ROWCOUNT AS VARCHAR) + ' records.';
END TRY
BEGIN CATCH
  PRINT 'Ingestion failed on line ' + CAST(ERROR_LINE() AS VARCHAR) + ': ' + ERROR_MESSAGE();
  -- Fallback to empty schema
  CREATE TABLE #incoming (customer_id INT, email VARCHAR);
END CATCH;
```

### 2. Transaction Rollback & Re-Throwing (`THROW`)

```sql
BEGIN TRY
  BEGIN TRANSACTION;
    UPDATE dw.dbo.Accounts SET balance = balance - 100.0 WHERE account_id = 101;
    UPDATE dw.dbo.Accounts SET balance = balance + 100.0 WHERE account_id = 202;
  COMMIT;
END TRY
BEGIN CATCH
  IF @@TRANCOUNT > 0 ROLLBACK;
  PRINT 'Transfer failed. Transaction rolled back: ' + ERROR_MESSAGE();
  THROW; -- Re-throws the original exception to notify the orchestrator
END CATCH;
```

### 3. Production ETL: Dead-Letter Queue (DLQ) & Webhook Alert Routing

Wrap mission-critical multi-source ingestion in an atomic transaction boundary. On failure, record the incident in a Dead-Letter Queue and send an automated alert:

```sql
CREATE CONNECTION prod_db AS MSSQL(SERVER='sql01.internal', DATABASE='production');
CREATE CONNECTION alerts  AS SMTP(HOST='smtp.corp.internal', PORT=587, USERNAME='alerts@corp.com', PASSWORD='SECRET:AlertPassword', USE_SSL=TRUE);

DECLARE @batch_id VARCHAR = 'BATCH_20260816_01';

BEGIN TRY
  BEGIN TRANSACTION;

  -- 1. Extract and stage data
  SELECT invoice_id, amount, customer_id, billed_date 
  INTO #staged_invoices
  FROM prod_db.dbo.Invoices
  WHERE billed_date = CAST(GETDATE() AS DATE);

  -- 2. Validate mandatory business invariants
  IF (SELECT COUNT(*) FROM #staged_invoices) = 0
    THROW 50001, 'No invoice records found for current billing cycle.', 1;

  -- 3. Publish to warehouse
  INSERT INTO prod_db.dbo.ProcessedInvoices SELECT * FROM #staged_invoices;

  COMMIT;
  PRINT 'Batch ' + @batch_id + ' processed successfully.';
END TRY
BEGIN CATCH
  -- 1. Clean rollback of any partial writes
  IF @@TRANCOUNT > 0 ROLLBACK;

  DECLARE @err_msg VARCHAR = ERROR_MESSAGE();
  DECLARE @err_line INT   = ERROR_LINE();

  PRINT 'CRITICAL: Batch ' + @batch_id + ' failed at line ' + CAST(@err_line AS VARCHAR) + ': ' + @err_msg;

  -- 2. Record incident into persistent Dead-Letter Queue (DLQ)
  INSERT INTO prod_db.dbo.DeadLetterQueue (BatchId, ErrorLine, ErrorMessage, OccurredAt)
  VALUES (@batch_id, @err_line, @err_msg, GETDATE());

  -- 3. Broadcast incident notification to operations team
  SEND EMAIL
    TO 'oncall@corp.com'
    FROM 'alerts@corp.com'
    SUBJECT 'ETL Failure: ' + @batch_id
    BODY 'Pipeline failure at line ' + CAST(@err_line AS VARCHAR) + ': ' + @err_msg
    AT alerts;

  -- 4. Fail the orchestrator job execution
  THROW;
END CATCH;
```

---

## Best Practices & Zero-Trust Guidelines

- **Always Check `@@TRANCOUNT`**: Before issuing a `ROLLBACK` in a `CATCH` block, check `IF @@TRANCOUNT > 0` to prevent secondary rollback errors when failures occur outside an active transaction.
- **Re-throw for Schedulers**: Use `THROW;` without arguments inside `CATCH` blocks so that the Orchestrator marks the job run as failed and activates retry policies.

---

## References & Related Recipes

- [Control Flow Reference](README.md)
- [THROW Statement](throw.md)
- [Transactions](../statements/session-control/transaction.md)
- [ETL Cookbook: Dead-Letter Queue (DLQ)](../../cookbooks/etl/dead-letter-queue.md)
- [ETL Cookbook: Automated Slack/Teams Alerting](../../cookbooks/etl/automated-slack-teams-alerting.md)
- [Syntax Index](../../syntax-index.md)
