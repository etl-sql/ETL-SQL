SET assigns a value to a declared variable or changes an engine execution option.

Syntax — variable assignment:
  SET @variable = <expression>;

Syntax — engine option:
  SET <OPTION> = ON | OFF | <value>;

Engine options:
- **WHAT_IF = ON|OFF** — parse and plan without executing DML (default OFF)
- **PROFILING = ON|OFF** — collect per-statement timing; view with SHOW PROFILE
- **SHOW_SECRETS = ON|OFF** — display SENSITIVE values in output/log views (default OFF, alias: SHOW_PASSWORD)
  ALLOW_PLAINTEXT_SECRETS = ON|OFF
- **** — unsafe: allow plaintext secrets to remain in saved source
- **NO_SAVE_SENSITIVE = ON|OFF** — scrub sensitive literals from saved source
- **NO_SAVE_CONNECTION = ON|OFF** — replace CREATE CONNECTION details with placeholders
  CONNECTION_ENCRYPTION = ON|OFF
- **** — encrypt CREATE CONNECTION target/options on save
- **BATCHSIZE = n** — rows per remote fetch batch for SELECT ... FROM connection
- **WEEK_START_DAY = '<day>'** — anchor day for RELDATE week expressions (default Monday) Valid: Monday, Tuesday, Wednesday, Thursday, Friday, Saturday, Sunday
- **JOIN_SPILL_THRESHOLD = n** — row count before hash join spills to disk
- **SORT_SPILL_THRESHOLD = n** — row count before sort spills to disk

```sql
-- Variable assignment
SET @region  = 'North';
SET @cutoff  = DATEADD(DAY, -30, GETDATE());

-- Dry-run mode: parse and plan only, no DML executed
SET WHAT_IF = ON;

-- Collect timing
SET PROFILING = ON;
SELECT * FROM dbo.BigTable INTO #data;
SHOW PROFILE;

-- Display masking only; does not change save behavior
SET SHOW_SECRETS = ON;
SHOW VARIABLES;

-- Unsafe local-dev escape hatch for source persistence
SET ALLOW_PLAINTEXT_SECRETS = ON;
USE PASSWORD = 'dev-only';

-- Save-time source hardening
SET NO_SAVE_SENSITIVE = ON;
SET NO_SAVE_CONNECTION = ON;

-- Preserve connection details, but encrypted
SET CONNECTION_ENCRYPTION = ON;
USE PASSWORD = 'dev-only';

-- Override week start for RELDATE expressions
SET WEEK_START_DAY = 'Sunday';
```

References:
- [Grammar](../../guides/getting-started.md)
