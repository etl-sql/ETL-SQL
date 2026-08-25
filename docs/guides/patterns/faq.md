# ETL-SQL Frequently Asked Questions (FAQ)

[« Back to Patterns & Troubleshooting](README.md)

Quick navigation to frequently asked questions and troubleshooting guides across the documentation.

> **Applies to:** every deployment profile (Solo, Team, Enterprise, SaaS).

## 1. General & Architecture

- **What is ETL-SQL?** — See [The Mental Model & Pipeline Thinking](../onboarding/getting-started.md).
- **How do I check the engine version?** — See [`eng.version`](../../reference/eng/version.md) and [System Variables](../../reference/eng/variables.md).
- **Can a script query multiple databases?** — See [Cross-Platform Reconciliation](../../cookbooks/etl/cross-platform-reconciliation.md).

## 2. Security & File Operations

- **Why are file paths required to be absolute?** — See [Zero-Trust Security Boundaries](../../architecture/standards/connectors-standards.md).
- **How do I send an email from a script?** — See [`SEND EMAIL`](../../reference/file-operations/send-email.md).
- **How do I test destructive queries safely before running?** — See [`SET WHAT_IF ON`](../../reference/statements/session-control/config.md).
- **How do I encrypt sensitive files or secrets?** — See [`ENCRYPT FILE`](../../reference/file-operations/encrypt-file.md) and [Secrets Management](../../administration/platform/secrets.md).

## 3. Troubleshooting Guides

For detailed problem diagnosis, step-by-step remedies, and code snippets, see our focused troubleshooting pages:

| Domain | Guide | Key Topics |
| :--- | :--- | :--- |
| **Syntax & Dialects** | [Troubleshooting: Syntax & Dialects](troubleshooting-syntax-and-dialect.md) | `TOP` vs `LIMIT`, `GETDATE()` vs `NOW()`, ANSI `COALESCE`, `WAIT UNTIL` polling. |
| **Connections & Security** | [Troubleshooting: Connections & Security](troubleshooting-connections-and-security.md) | `CREATE CONNECTION` auth conflicts, `ENC:` passwords, `CREATE SETS`, sandbox safe zones. |
| **Reporting & Dashboards** | [Troubleshooting: Report-SQL](troubleshooting-reporting.md) | `RELDATE` casting errors, Tier 2 traps, cascading slicer updates, action bindings. |
| **Performance & Ingestion** | [Troubleshooting: Performance](troubleshooting-performance.md) | Cross-source join pre-filtering, `BULK INSERT` batching, `#temp` table indexes. |

## Related References

- [5-Minute Quickstart](../onboarding/QUICKSTART.md)
- [ETL Recipes Cookbook](../../cookbooks/etl/README.md)
- [Statement Reference](../../reference/statements/README.md)
