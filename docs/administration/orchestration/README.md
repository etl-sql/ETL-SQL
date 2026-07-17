# Orchestration

Schedule, run, and monitor ETL-SQL jobs from the command line, including DAGs and CI/CD.

## Pages

- [CI/CD Integration](ci-cd.md) - ```powershell
- [CLI Command Reference](cli-commands.md) - ```
- [DAGs and Advanced Orchestration](dags.md) - As your data ecosystem grows, you will inevitably need to orchestrate complex dependencies where scripts must run in a specific order, sometimes in parallel, and often gated by the appearance of external data.
- [VS Code and Deployment](ide-and-deployment.md) - ETL-SQL ships with a dedicated VS Code extension (`src/etl-sql-vscode/`) that enhances the development experience. The extension communicates with the engine via the JSON REPL protocol (`ETL-SQL ui repl`).
- [Job Scheduling](job-scheduling.md) - Jobs are scheduled from within your `.etlsql` scripts using the `CREATE JOB` statement. Once registered, they are stored in a SQLite database and executed automatically by the background scheduler — no cron job or Windows Task Scheduler entry is required.
- [Logging and Performance Tuning](logging-and-performance.md) - ```bash
- [Orchestrator Management Portal](orchestrator-portal.md) - The **Orchestrator Management Portal** is a browser-based dashboard embedded in the ETL-SQL Portal that gives administrators full visibility and control over scheduled jobs without needing the CLI or a SQLite viewer.
- [Resource Governance](resource-governance.md) - To prevent Out-Of-Memory (OOM) errors and database connection exhaustion in multi-user environments, the Orchestrator employs a **Buffer Manager**.
- [Sessions and Variable Injection](sessions-and-variables.md) - Sessions let connections and variables defined in one run survive into the next. This is most useful when you split your pipeline across multiple scripts or F5 runs.
- [Troubleshooting](troubleshooting.md) - 1. Check that the executable is running (`ETL-SQL ui repl` or as a service). The scheduler only runs while the process is live.

## See Also

- [Administration](../README.md) - the full admin area.
- [Platform Administration](../platform/README.md) · [Portal Administration](../portal/README.md) · [Orchestration](../orchestration/README.md)
- [CLI Reference](../../reference/cli/README.md) - every `etl-sql` command.
