# ETL-SQL Orchestrator's Guide

**Audience:** Operators, data engineers, and pipeline administrators who need to schedule, run, and
monitor ETL-SQL jobs from the command line.

## Overview

The ETL-SQL engine ships as a single executable (`ETL-SQL.exe` on Windows, `ETL-SQL` on Linux/macOS)
that works in three modes:

| Mode | Purpose |
|------|---------|
| **CLI / Headless** | Run a `.etlsql` script from a shell, CI/CD pipeline, or Task Scheduler. |
| **Terminal IDE (TUI)** | Interactive editor with live execution tree, results panel, and autocomplete. |
| **Background Scheduler** | The scheduler starts automatically at launch and continuously polls scheduled jobs. |

All three modes share the same background scheduler. Any `CREATE JOB` statement registered in a
script is persisted and will fire at its next scheduled time even when the Terminal IDE is not open.

## Topics

- [CLI Command Reference](job-orchestration/cli-commands.md) - `run`, `ui`, `encrypt`, `session`,
  `generate`, `gen-script`, `extract-spec`, and exit codes.
- [Job Scheduling](job-orchestration/job-scheduling.md) - `CREATE JOB`, retry policies, `SHOW JOBS`,
  history and host metrics, bundles, and cancellation.
- [Sessions and Variable Injection](job-orchestration/sessions-and-variables.md) - session
  persistence and variable injection.
- [Logging and Performance Tuning](job-orchestration/logging-and-performance.md) - log files,
  configuration, batch size, and per-statement profiling.
- [CI/CD Integration](job-orchestration/ci-cd.md) - shell/PowerShell, GitHub Actions, Azure
  Pipelines, Task Scheduler, and cron.
- [VS Code and Deployment](job-orchestration/ide-and-deployment.md) - the VS Code extension and
  deployment configuration.
- [Resource Governance](job-orchestration/resource-governance.md) - queuing, hysteresis, and policy
  overrides.
- [Troubleshooting](job-orchestration/troubleshooting.md) - common scheduler, decryption, and
  session issues.
- [DAGs and Advanced Orchestration](job-orchestration/dags.md) - composition, fan-out, dependency
  gating, and conditional branching.
- [Orchestrator Management Portal](job-orchestration/orchestrator-portal.md) - managing the
  Orchestrator from the portal.

## See Also

- [CLI Reference](../reference/cli/README.md) - every `etl-sql` command, generated from the command
  tree (the atomic reference for the commands narrated here).
- [Administrator's Guide](administration.md) - install, secure, back up, and monitor the platform.
- [Portal Administration](portal-admin.md) - portal user, permission, and subscription
  administration.
