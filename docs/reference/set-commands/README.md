# SET Commands

`SET` assigns a value to a session variable or changes an engine execution option. All options are session-scoped and reset when the session ends.

```sql
-- Variable assignment
SET @variable = <expression>;

-- Engine option
SET <OPTION> = ON|OFF|<value>;
```

## Variable Assignment

| Command | Description |
| :--- | :--- |
| [SET @variable](set-variable.md) | Assign a value to a session variable |

## Execution Mode

| Command | Description |
| :--- | :--- |
| [SET WHAT_IF](set-what-if.md) | Dry-run mode — plan and log without executing DML |
| [SET PROFILING](set-profiling.md) | Collect per-statement timing; view with `SHOW PROFILE` |
| [SET WITH_PROMPT](set-with-prompt.md) | Prompt for confirmation before applying SET operations |

## Display & Secret Handling

| Command | Description |
| :--- | :--- |
| [SET SHOW_SECRETS](set-show-secrets.md) | Unmask SENSITIVE/ENCRYPTED variables in output (alias: `SHOW_PASSWORD`) |
| [SET ALLOW_PLAINTEXT_SECRETS](set-allow-plaintext-secrets.md) | Unsafe: allow plaintext secrets in saved source |
| [SET NO_SAVE_SENSITIVE](set-no-save-sensitive.md) | Scrub sensitive literals from saved source |
| [SET NO_SAVE_CONNECTION](set-no-save-connection.md) | Replace connection details with placeholders on save |
| [SET CONNECTION_ENCRYPTION](set-connection-encryption.md) | Encrypt connection details on save |

## Date & Locale

| Command | Description |
| :--- | :--- |
| [SET WEEK_START_DAY](set-week-start-day.md) | Anchor day for RELDATE week expressions (default: Monday) |

## Performance Thresholds

| Command | Description | Default |
| :--- | :--- | :--- |
| [SET BATCHSIZE](set-batchsize.md) | Pipeline batch size | 10,000 |
| [SET JOIN_SPILL_THRESHOLD](set-join-spill-threshold.md) | Rows before join spills to disk | 100,000 |
| [SET SORT_SPILL_THRESHOLD](set-sort-spill-threshold.md) | Rows before sort spills to disk | 100,000 |
| [SET WINDOW_SPILL_THRESHOLD](set-window-spill-threshold.md) | Rows before window functions spill | 100,000 |
| [SET TEMP_TABLE_SPILL_THRESHOLD](set-temp-table-spill-threshold.md) | Rows before `#temp` spills to disk | 1,000,000 |
| [SET EXTERNAL_HASH_PARTITIONS](set-external-hash-partitions.md) | Partitions for spilled hash operations | 32 |
| [SET EXTERNAL_SORT_CHUNK_SIZE](set-external-sort-chunk-size.md) | Rows per sort chunk when spilling | 50,000 |
| [SET MAX_LAST_RESULT_ROWS](set-max-last-result-rows.md) | Rows in the interactive display buffer | 50,000 |
| [SET MAX_GENERATE_ROWS](set-max-generate-rows.md) | Max rows GENERATE may produce | 1,000,000 |
| [SET MAX_PARALLEL_DEGREE](set-max-parallel-degree.md) | Thread limit for `PARALLEL BEGIN...END` | CPU count |
| [SET FOREACH_PAGE_SIZE](set-foreach-page-size.md) | Batch size for FOREACH iteration | — |

## Security Overrides

All produce an audit entry. Paths must be within a Safe Zone.

| Command | Description |
| :--- | :--- |
| [SET ALLOW_FILE_TYPE_ACCESS](set-allow-file-type-access.md) | Allow file extensions outside the global whitelist |
| [SET ALLOW_FILE_OPERATIONS](set-allow-file-operations.md) | Override runaway file-op protection limit (default: 100) |
| [SET ALLOW_RECURSIVE_LAYERS](set-allow-recursive-layers.md) | Override directory recursion depth limit (default: 5) |
| [SET MAX_SMTP_EMAILS_PER_SCRIPT](set-max-smtp-emails.md) | Anti-spam limit on emails per script (default: 100) |
| [SET MAX_STRING_RESULT_SIZE](set-max-string-result-size.md) | Maximum string result size in bytes (default: 100 MB) |
| [SET REGEX_MATCH_TIMEOUT](set-regex-match-timeout.md) | Regex evaluation timeout in ms (default: 1,000) |

## Spill Security

| Command | Description | Default |
| :--- | :--- | :--- |
| [SET SPILL_ENCRYPTION](set-spill-encryption.md) | Encrypt spilled buffers at rest | ON |
| [SET SPILL_COMPRESSION](set-spill-compression.md) | Compress spilled buffers | ON |
| [SET SPILL_FORMAT](set-spill-format.md) | Serialization format for spills | Arrow |

## References

- [Configuration Settings Reference](../../administration/platform/appsettings-reference.md)
- [Statement Reference](../statements/README.md)
- [Syntax Index](../../syntax-index.md)
