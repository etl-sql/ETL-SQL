# ETL-SQL Frequently Asked Questions (FAQ)

[« Back to Patterns & Troubleshooting](README.md)

Quick answers to frequently asked questions, organized by category with links to focused troubleshooting guides.

---

> **Applies to:** every deployment profile (Solo, Team, Enterprise, SaaS).

## 1. General & Architecture

#### What is ETL-SQL?
ETL-SQL is a script-first orchestration and reporting engine. It enables you to extract, transform, validate, and publish data across heterogeneous systems (SQL databases, flat files, cloud storage, REST APIs) using plain-text `.etlsql` and `.rptsql` files. See [Thinking in Pipelines](../onboarding/getting-started.md).

#### How do I check the engine version?
Use the `@@VERSION` system variable or query `eng.version`:
```sql
PRINT @@VERSION;
```

---

## 2. Troubleshooting Guides

For detailed problem diagnosis, step-by-step remedies, and code snippets, see our focused troubleshooting pages:

| Domain | Guide | Key Topics |
| :--- | :--- | :--- |
| **Syntax & Dialects** | [Troubleshooting: Syntax & Dialects](troubleshooting-syntax-and-dialect.md) | `TOP` vs `LIMIT`, `GETDATE()` vs `NOW()`, ANSI `COALESCE`, `WAIT UNTIL` polling. |
| **Connections & Security** | [Troubleshooting: Connections & Security](troubleshooting-connections-and-security.md) | `CREATE CONNECTION` auth conflicts, `ENC:` passwords, `CREATE SETS`, sandbox safe zones. |
| **Reporting & Dashboards** | [Troubleshooting: Report-SQL](troubleshooting-reporting.md) | `RELDATE` casting errors, Tier 2 traps, cascading slicer updates, action bindings. |
| **Performance & Ingestion** | [Troubleshooting: Performance](troubleshooting-performance.md) | Cross-source join pre-filtering, `BULK INSERT` batching, `#temp` table indexes. |

---

## 3. Top Quick Answers

#### How do I send an email from a script?
Use the canonical `SEND EMAIL` statement:
```sql
SEND EMAIL 
    TO      'team@company.com'
    FROM    'etl@company.com'
    SUBJECT 'Pipeline Completed'
    BODY    'The daily pipeline ran successfully.'
    AT      smtp_conn;
```

#### Why are file paths required to be absolute?
The Zero-Trust security sandbox requires absolute paths (e.g. `C:\Data\input.csv` or `/data/input.csv`) to prevent path traversal attacks.

#### How do I safely test destructive queries before executing?
Wrap the queries in a dry-run block using `SET WHAT_IF ON`:
```sql
SET WHAT_IF ON;
DELETE FROM prod_db.dbo.Logs WHERE LogDate < '2025-01-01';
SET WHAT_IF OFF;
```

---

## Related Topics

- [5-Minute Quickstart](../onboarding/QUICKSTART.md)
- [ETL Recipes Cookbook](../../cookbooks/etl/README.md)
- [Statement Reference](../../reference/statements/README.md)
