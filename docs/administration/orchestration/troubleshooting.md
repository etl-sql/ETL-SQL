# Troubleshooting

Diagnosing the common Orchestrator problems: jobs not firing, jobs failing silently, and state that looks wrong.

## The scheduler isn't firing my job

1. Check that the executable is running (`ETL-SQL ui repl` or as a service). The scheduler only runs while the process is live.
2. Query `eng.jobs` — verify `IsEnabled = 1` and `NextRun` is in the past.
3. Check `logs/` for scheduler error entries at the `Error` level.
4. If using process spawning (`UseProcessSpawning = true`), verify `ExecutablePath` points to a valid executable.

## A scheduled job shows `FAILURE` with no error message

Run the job's script manually first to reproduce the error interactively:

```bash
ETL-SQL run C:\ETL\Scripts\nightly.etlsql --verbose --log
```

This surfaces the full error with line numbers. Fix the script, then let it be picked up by the scheduler on its next `NextRun`.

## `ENC:` strings fail to decrypt

The master password used to encrypt must match the one passed at runtime (`--pass` or `USE PASSWORD`). Passwords are case-sensitive. Re-encrypt with the correct password:

```bash
ETL-SQL encrypt "Server=prod;Database=DW;..." --pass CorrectPassword
```

## Session state is stale or corrupt

Clear the session and let it rebuild:

```bash
ETL-SQL session clear <session-id>
```

## Performance is slower than expected

1. Use `--perf` to identify which phase (Lex/Parse/Execute) takes the most time.
2. Use `SET PROFILING ON` plus `eng.profile` inside the script to find slow statements.
3. Reduce `--batch-size` if you are hitting memory pressure (large rows); increase it for small rows with fast I/O.
4. For cross-database `INSERT INTO ... SELECT FROM` pipelines, ensure the source connection implements SQL pushdown (`IDatabaseSource` with `SupportsSqlPushdown = true`) to avoid row-by-row transfer.

---