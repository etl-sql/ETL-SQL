# ETL Pipeline & Orchestration Guides

[« Back to Guides](../README.md)

ETL-SQL treats data pipelines as plain-text, source-controlled `.etlsql` scripts. These focused guides cover ingestion patterns, modular script coordination, concurrency, workflow dependencies, error handling, session resilience, and unit testing.

---

## Guides in this Section

| Guide | Description |
| :--- | :--- |
| [Staged vs. Streaming Ingestion](staged-vs-streaming-ingestion.md) | When to use in-memory `#temp` staging vs. direct single-pass streams. |
| [Modular Scripts & Parameters](modular-scripts-and-parameters.md) | Break monolithic jobs into reusable sub-scripts with `RUN SCRIPT` and `INPUT`/`OUTPUT` parameters. |
| [Parallel Execution](parallel-execution.md) | Execute tasks concurrently across worker threads with `PARALLEL(n)` concurrency limits. |
| [DAG Dependencies & Signals](dag-dependencies-and-signals.md) | Coordinate multi-stage task graphs, data-driven branches, and file trigger signals with `WAIT UNTIL`. |
| [Error Handling, Alerting & Retries](error-handling-and-retries.md) | Catch errors with `TRY...CATCH`, dispatch email/webhook alerts, and configure automated job retries. |
| [Script Resilience & Checkpoints](script-resilience-and-checkpoints.md) | Dry-run validation with `SET WHAT_IF ON`, transaction blocks, and session checkpoint-resume (`--session`/`--resume`). |
| [Pipeline Unit Testing & Mocking](pipeline-unit-testing.md) | Author fast, zero-dependency unit tests using `MOCKDB` and `ASSERT` statements. |

---

## Related References

- [Statement Reference](../../reference/statements/README.md)
- [Control Flow Reference](../../reference/control-flow/README.md)
- [Job Orchestration Guide](../../administration/orchestration/README.md)
