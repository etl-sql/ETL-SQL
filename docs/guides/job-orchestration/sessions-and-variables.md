# Sessions and Variable Injection

## 4. Session Persistence

Sessions let connections and variables defined in one run survive into the next. This is most useful when you split your pipeline across multiple scripts or F5 runs.

### 4.1 How sessions work

When you pass `--session <id>`:
1. At **start** of a run: the engine loads the saved state (connections + variables) for `<id>` into the evaluator.
2. At **end** of a run: the engine saves the final evaluator state back to `<id>`.

Session state is stored in an encrypted file on disk (keyed to your `--pass` password if provided).

### 4.2 Usage pattern

```bash
# Step 1: Set up long-lived connections once
ETL-SQL run 01_connect.etlsql --session nightly --pass MyKey

# Step 2: Run the extract (connections from step 1 still live)
ETL-SQL run 02_extract.etlsql --session nightly --pass MyKey

# Step 3: Load (connections still live)
ETL-SQL run 03_load.etlsql --session nightly --pass MyKey

# Cleanup: reset session to force fresh connections next time
ETL-SQL session clear nightly
```

### 4.3 Stale session cleanup

Sessions that have not been used for 7 days are automatically removed on the next `run` invocation. You can clear one manually at any time with `ETL-SQL session clear <id>`.

---

## 5. Variable Injection

You can pass variables from the CLI into your script using `--var`:

```bash
ETL-SQL run monthly_report.etlsql \
  --var @env=PROD \
  --var @startDate=2026-01-01 \
  --var @endDate=2026-03-31
```

Inside the script, use these as normal `@variables`:

```sql
DECLARE @env VARCHAR(10);         -- declared but value comes from CLI
DECLARE @startDate DATE;
DECLARE @endDate DATE;

SELECT * FROM prod.sales
WHERE region = @env
  AND sale_date BETWEEN @startDate AND @endDate;
```

> [!NOTE]
> CLI variables are treated as **input parameters** — they are injected before script execution begins. They do not need to be declared with `DECLARE` (the engine will accept them even without an explicit `DECLARE`), but declaring them is good practice for IDE autocomplete.

**Type coercion:** The CLI automatically converts the string value to the most appropriate type (int, double, bool, DateTime, or string).

---

