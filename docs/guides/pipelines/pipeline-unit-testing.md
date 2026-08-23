# Pipeline Unit Testing and Mocking

ETL-SQL enables script-first testing of data pipelines, business logic, transformations, and assertions without connecting to external databases or cloud networks. Using the built-in **`MOCKDB`** connector and **`ASSERT`** statements, you can author reproducible unit tests that run in milliseconds locally and in CI/CD.

---

> **Applies to:** every deployment profile (Solo, Team, Enterprise, SaaS).

## Core Testing Components

| Component | Syntax | Description |
| :--- | :--- | :--- |
| **Mock Database** | `CREATE CONNECTION test_db AS MOCKDB();` | Fast in-memory connector requiring zero external configuration. |
| **Data Seeding** | `INSERT INTO ... VALUES (...)` | Seeds deterministic test fixtures directly within the test script. |
| **Logic Assertion** | `ASSERT (condition), 'failure message';` | Evaluates a scalar condition and throws if false. |

---

## Example 1: Unit Testing an Order Cleansing Rule

This unit test verifies that dirty customer data (unformatted emails, missing values) is transformed into standardized output.

```sql
-- test_order_cleansing.etlsql
PRINT 'Running Unit Test: Order Cleansing Logic...';

-- 1. Setup in-memory test fixture
CREATE CONNECTION test_src AS MOCKDB();

SELECT 
    1 AS OrderId, '  ALEX@EXAMPLE.COM ' AS RawEmail, 150.00 AS Amount
INTO #test_input;

INSERT INTO #test_input (OrderId, RawEmail, Amount) VALUES
    (2, 'bad-email', -50.00),
    (3, 'sam@company.org', 300.00);

-- 2. Execute Transformation under Test
SELECT
    OrderId,
    LOWER(TRIM(RawEmail)) AS CleanEmail,
    CASE WHEN Amount < 0 THEN 0.00 ELSE Amount END AS CleanAmount
INTO #test_output
FROM #test_input;

-- 3. Assert Expectations
ASSERT (SELECT COUNT(*) FROM #test_output) = 3, 
    'Expected 3 rows in output';

ASSERT (SELECT CleanEmail FROM #test_output WHERE OrderId = 1) = 'alex@example.com', 
    'Expected trimmed and lowercased email for OrderId 1';

ASSERT (SELECT CleanAmount FROM #test_output WHERE OrderId = 2) = 0.00, 
    'Expected negative amount to be normalized to 0.00';

PRINT 'All unit test assertions passed successfully.';
```

Run the unit test from the command line:

```bash
etl-sql run test_order_cleansing.etlsql
```

If any assertion fails, the command prints the custom error message and exits with code `1`.

---

## Example 2: Integration Testing a Modular Sub-Script

Test that a child script processes input parameters and sets expected output variables.

```sql
-- test_modular_child.etlsql
DECLARE @status  STRING;
DECLARE @rowsOut INT;

-- Execute the modular script under test
RUN SCRIPT 'pipelines/transform_daily.etlsql' WITH (
    @status  = @status,
    @rowsOut = @rowsOut
);

-- Assert contract fulfillment
ASSERT @status = 'Success', 'Sub-script should complete with Success status';
ASSERT @rowsOut > 0, 'Sub-script should process at least one row';

PRINT 'Modular pipeline integration test passed.';
```

---

## Related Topics

- [Error Handling and Retries](error-handling-and-retries.md) — Handling unexpected failures.
- [ASSERT Statement Reference](../../reference/statements/session-control/assert.md) — Assertion syntax.
- [Automating Quality Gates in CI](../data-quality/automating-quality-gates.md) — CI execution.
