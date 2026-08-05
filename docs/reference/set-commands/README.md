# SET-COMMANDS Reference

[« Back to parent](../README.md)

| Page | Description |
| :--- | :--- |
| [SET ALLOW_FILE_OPERATIONS](set-allow-file-operations.md) | Overrides the runaway file-operation protection limit for the current session. |
| [SET ALLOW_FILE_TYPE_ACCESS](set-allow-file-type-access.md) | <!-- SetSecurityOverrideStatement --> |
| [SET ALLOW_PLAINTEXT_SECRETS](set-allow-plaintext-secrets.md) | Unsafe local-development escape hatch. Controls whether plaintext secrets may remain in saved source files. |
| [SET ALLOW_RECURSIVE_LAYERS](set-allow-recursive-layers.md) | Overrides the directory recursion depth limit for the current session. |
| [SET BATCHSIZE](set-batchsize.md) | Sets the number of rows per remote fetch batch for `SELECT ... FROM connection`. |
| [SET CONNECTION_ENCRYPTION](set-connection-encryption.md) | Controls whether `CREATE CONNECTION` targets and quoted option values are encrypted on save using the script/master password. |
| [SET EXTERNAL_HASH_PARTITIONS](set-external-hash-partitions.md) | Sets the number of partitions used for spilled hash operations. |
| [SET EXTERNAL_SORT_CHUNK_SIZE](set-external-sort-chunk-size.md) | Sets the number of rows per sort chunk when sort operations spill to disk. |
| [SET FOREACH_PAGE_SIZE](set-foreach-page-size.md) | Sets the batch size when `FOREACH` iterates over a `#temp` table. |
| [SET JOIN_SPILL_THRESHOLD](set-join-spill-threshold.md) | Sets the row count before a hash join spills intermediate results to disk. |
| [SET MAX_GENERATE_ROWS](set-max-generate-rows.md) | Sets the maximum number of rows that `GENERATE` is allowed to produce. |
| [SET MAX_LAST_RESULT_ROWS](set-max-last-result-rows.md) | Sets the maximum number of rows retained in the interactive display buffer. |
| [SET MAX_PARALLEL_DEGREE](set-max-parallel-degree.md) | Sets the thread limit inside `PARALLEL BEGIN...END` blocks. |
| [SET MAX_SMTP_EMAILS_PER_SCRIPT](set-max-smtp-emails.md) | Sets the anti-spam limit capping the number of emails a single script run may send. |
| [SET MAX_STRING_RESULT_SIZE](set-max-string-result-size.md) | Sets the maximum length in bytes allowed for string results. |
| [SET NO_SAVE_CONNECTION](set-no-save-connection.md) | Controls whether `CREATE CONNECTION` targets and quoted option values are replaced with placeholders on save. Use for source-controlled templates w... |
| [SET NO_SAVE_SENSITIVE](set-no-save-sensitive.md) | Controls whether sensitive literals are scrubbed from saved source. When enabled, rewrites `USE PASSWORD` literals to `PROMPT` and replaces SENSITI... |
| [SET PROFILING](set-profiling.md) | Enables or disables per-statement timing collection. View results through `eng.profile`. |
| [SET REGEX_MATCH_TIMEOUT](set-regex-match-timeout.md) | Sets the execution duration cap in milliseconds for regex evaluations to prevent denial-of-service from catastrophic backtracking. |
| [SET SHOW_SECRETS](set-show-secrets.md) | Controls whether SENSITIVE/ENCRYPTED variable values are unmasked in `eng.variables` output. This is a display-only setting and does not affect sav... |
| [SET SPILL_COMPRESSION](set-spill-compression.md) | <!-- SetSpillOptionStatement --> |
| [SET SPILL_ENCRYPTION](set-spill-encryption.md) | Controls whether data buffers spilled to local disk during heavy queries are encrypted at rest. |
| [SET TEMP_TABLE_SPILL_THRESHOLD](set-temp-table-spill-threshold.md) | Sets the row count before a `#temp` table spills its data to disk. |
| [SET @variable](set-variable.md) | Assigns a value to a declared or implicitly declared session variable. |
| [SET WEEK_START_DAY](set-week-start-day.md) | Sets the first day of the week for RELDATE week-boundary expressions (`W`, `W-1`, `WE`, etc.). |
| [SET WHAT_IF](set-what-if.md) | Enables or disables dry-run mode. When enabled, side-effecting operations (INSERT, UPDATE, DELETE, MERGE, file writes, SEND EMAIL, Docker) are logg... |
| [SET WINDOW_SPILL_THRESHOLD](set-window-spill-threshold.md) | Sets the row count before window function operations spill intermediate results to disk. |
| [SET WITH_PROMPT](set-with-prompt.md) | Marks a named variable set so activating it prompts for confirmation. |
| [SET](set.md) | <!-- SetThresholdStatement --> |
