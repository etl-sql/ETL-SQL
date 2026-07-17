# SET
Assigns a value to a session variable or changes an engine execution option. All options are session-scoped and reset when the session ends.

## Syntax
```sql
-- Variable assignment
SET @variable = <expression>;

-- Engine option
SET <OPTION> = ON|OFF|<value>;
```

## Subjects

See the [SET Commands index](README.md) for the full list of options with links to individual reference pages.

### Variable Assignment
- **@variable = expression** — assign a value to a session variable.

### Execution Mode
- **WHAT_IF = ON|OFF** — dry-run mode; plan and log without executing DML (default OFF).
- **PROFILING = ON|OFF** — collect per-statement timing; view with `SHOW PROFILE` (default OFF).
- **WITH_PROMPT = ON|OFF** — prompt for confirmation before applying SET operations (default OFF).

### Display & Secret Handling
- **SHOW_SECRETS = ON|OFF** — unmask SENSITIVE/ENCRYPTED variables in output (alias: `SHOW_PASSWORD`, default OFF).
- **ALLOW_PLAINTEXT_SECRETS = ON|OFF** — unsafe: allow plaintext secrets in saved source (default OFF).
- **NO_SAVE_SENSITIVE = ON|OFF** — scrub sensitive literals from saved source (default OFF).
- **NO_SAVE_CONNECTION = ON|OFF** — replace connection details with placeholders on save (default OFF).
- **CONNECTION_ENCRYPTION = ON|OFF** — encrypt connection details on save (default OFF).

### Date & Locale
- **WEEK_START_DAY = 'day'** — anchor day for RELDATE week expressions (default Monday).

### Performance Thresholds
- **BATCHSIZE = n** — pipeline batch size (default 10,000).
- **JOIN_SPILL_THRESHOLD = n** — rows before join spills to disk (default 100,000).
- **SORT_SPILL_THRESHOLD = n** — rows before sort spills to disk (default 100,000).
- **WINDOW_SPILL_THRESHOLD = n** — rows before window functions spill (default 100,000).
- **TEMP_TABLE_SPILL_THRESHOLD = n** — rows before `#temp` spills to disk (default 1,000,000).
- **EXTERNAL_HASH_PARTITIONS = n** — partitions for spilled hash operations (default 32).
- **EXTERNAL_SORT_CHUNK_SIZE = n** — rows per sort chunk when spilling (default 50,000).
- **MAX_LAST_RESULT_ROWS = n** — rows in the interactive display buffer (default 50,000).
- **MAX_GENERATE_ROWS = n** — max rows GENERATE may produce (default 1,000,000).
- **MAX_PARALLEL_DEGREE = n** — thread limit for `PARALLEL BEGIN...END` (default: CPU count).
- **FOREACH_PAGE_SIZE = n** — batch size for FOREACH iteration.

### Security Overrides
- **ALLOW_FILE_TYPE_ACCESS = ON|OFF|'.ext'** — allow file extensions outside the global whitelist.
- **ALLOW_FILE_OPERATIONS = n** — override runaway file-op protection limit (default 100).
- **ALLOW_RECURSIVE_LAYERS = n** — override directory recursion depth limit (default 5).
- **MAX_SMTP_EMAILS_PER_SCRIPT = n** — anti-spam email limit (default 100).
- **MAX_STRING_RESULT_SIZE = n** — maximum string result size in bytes (default 100 MB).
- **REGEX_MATCH_TIMEOUT = n** — regex evaluation timeout in ms (default 1,000).

### Spill Security
- **SPILL_ENCRYPTION = ON|OFF** — encrypt spilled buffers at rest (default ON).
- **SPILL_COMPRESSION = ON|OFF** — compress spilled buffers (default ON).
- **SPILL_FORMAT = 'AUTO'|'JSON'|'PARQUET'** — serialization format for spills (default Arrow).

## Example
```sql
-- Variable assignment
SET @region = 'North';
SET @cutoff = DATEADD(DAY, -30, GETDATE());

-- Dry-run mode
SET WHAT_IF = ON;
DELETE FROM prod.OldOrders WHERE order_date < '2020-01-01';
SET WHAT_IF = OFF;

-- Profile a slow query
SET PROFILING = ON;
SELECT region, SUM(amount) FROM prod.Sales GROUP BY region;
SHOW PROFILE INTO #timing;
SET PROFILING = OFF;
SELECT * FROM #timing ORDER BY duration_ms DESC;

-- Raise spill threshold before a known large join
SET JOIN_SPILL_THRESHOLD = 500000;
```

## References
- [SET Commands Index](README.md)
- [Configuration Settings Reference](../../administration/platform/settings.md)
