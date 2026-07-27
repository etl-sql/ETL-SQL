# SESSION-CONTROL Reference

[« Back to parent](../README.md)

| Page | Description |
| :--- | :--- |
| [ANALYZE](analyze.md) | Collects statistics about a temp table or result set, including row count, column cardinality, null ratios, and min/max values. |
| [ASSERT JOB](assert-job.md) | Asserts on the **run's own metrics** — how many rows it processed, what fraction was quarantined, |
| [ASSERT](assert.md) | Validates a condition at runtime and halts execution with an error if it is false. Used for data quality checks and script contracts. |
| [CLEAR SESSION](clear.md) | Cleans up session state: temp files, recovery manifests, encrypted session data, and disk-spill artifacts. |
| [CONFIG](config.md) | CONFIG is a keyword used exclusively with the SHOW CONNECTION command to inspect the configuration options and parameters of a data source connection. |
| [EXPLAIN](explain.md) | Shows the execution plan for a SELECT or DML statement without running it. |
| [GENERATE](generate.md) | Creates synthetic or mock data rows and loads them into a #temp table. Useful for testing, seeding, and load simulation. |
| [HELP](help.md) | Displays documentation for a keyword, function, connector, or option directly in the REPL or output pane. |
| [LINEAGE](lineage.md) | Tracks column-level data provenance across all SELECT, INSERT, UPDATE, and MERGE operations in a script. Records how each output column was derived... |
| [LINT](lint.md) | Runs static analysis on an ETL-SQL script and reports rule violations without executing the script. |
| [print](print.md) | PRINT writes a message to the console output or execution log. |
| [REQUIRE](require.md) | Declares a minimum ETL-SQL engine version required to run this script. Fails fast with a clear error if the runtime is too old. |
| [TRANSACTION](transaction.md) | Transactions group multiple DML operations into an atomic unit. If any statement fails the entire group can be rolled back. |
