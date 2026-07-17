# Orchestration

Schedule, run, and monitor ETL-SQL jobs from the command line, including DAGs and CI/CD.

## Pages

- [CI/CD Integration](ci-cd.md) - ```powershell
- [CLI Command Reference](../../reference/cli/README.md) - Command-line interface for the ETL-SQL engine, including syntax, arguments, options, examples, and exit codes.
- [Pipelines and DAGs](../../guides/pipelines-and-dags.md) - Compose modular scripts, execute branches in parallel, and set up gating.
- [VS Code Extension](../../guides/vscode-extension.md) - Edit, lint, preview, and debug scripts directly in VS Code.
- [Job Scheduling](job-scheduling.md) - Jobs are scheduled from within your `.etlsql` scripts using the `CREATE JOB` statement. Once registered, they are stored in a SQLite database and executed automatically by the background scheduler — no cron job or Windows Task Scheduler entry is required.
- [Logging and Performance Tuning](../../guides/logging-and-performance.md) - Tune batch size, logging, and metrics for script runs.
- [Orchestrator Management Portal](orchestrator-portal.md) - The **Orchestrator Management Portal** is a browser-based dashboard embedded in the ETL-SQL Portal that gives administrators full visibility and control over scheduled jobs without needing the CLI or a SQLite viewer.
- [Resource Governance](resource-governance.md) - To prevent Out-Of-Memory (OOM) errors and database connection exhaustion in multi-user environments, the Orchestrator employs a **Buffer Manager**.
- [Sessions and Variable Injection](sessions-and-variables.md) - Sessions let connections and variables defined in one run survive into the next. This is most useful when you split your pipeline across multiple scripts or F5 runs.
- [Troubleshooting](troubleshooting.md) - 1. Check that the executable is running (`ETL-SQL ui repl` or as a service). The scheduler only runs while the process is live.

## See Also

- [Administration](../README.md) - the full admin area.
- [Platform Administration](../platform/README.md) · [Portal Administration](../portal/README.md) · [Orchestration](../orchestration/README.md)
- [CLI Reference](../../reference/cli/README.md) - every `etl-sql` command.
