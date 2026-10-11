# 05_Security_Diagnostics: Quality Gates, WHAT_IF, and Governance

This directory illustrates zero-trust security guardrails, data quality assertions, policy compliance, and engine diagnostics.

## Samples Overview

| File | Feature | Description |
| :--- | :--- | :--- |
| [`WhatIf_Dry_Run.etlsql`](WhatIf_Dry_Run.etlsql) | `SET WHAT_IF ON;` | Safe dry-run execution: validates plans, schemas, and permissions without executing destructive modifications. |
| [`Data_Quality_Rules.etlsql`](Data_Quality_Rules.etlsql) | Data Quality | Declaring inline checks: `ASSERT`, `NULL_PERCENT`, `UNIQUE`, `BETWEEN`. |
| [`Compound_Quality_Rules.etlsql`](Compound_Quality_Rules.etlsql) | Multi-Field Quality | Complex validation rules combining cross-column invariants and threshold limits. |
| [`Data_Quality_Script_Handled.etlsql`](Data_Quality_Script_Handled.etlsql) | Error Handling | Intercepting data quality failures and routing quarantined rows to dead-letter tables. |
| [`assert_table_unit_test.test.etlsql`](assert_table_unit_test.test.etlsql) | Unit Testing | Using `ASSERT TABLE ... EQUALS ...` for automated test assertions against expected datasets. |
| [`Linter_Diagnostics.etlsql`](Linter_Diagnostics.etlsql) | Linter Diagnostics | Examples of parser and security linter warnings (plaintext secrets, wildcard projections). |
| [`SET_Audit.etlsql`](SET_Audit.etlsql) | Audit Trail | Enforcing audit logging levels and session event emission. |
| [`Engine_Statistics.etlsql`](Engine_Statistics.etlsql) | Diagnostics | Querying `eng.metrics`, execution elapsed times, memory peaks, and disk spill statistics. |

## Running These Samples

```bash
dotnet run --project src/ETL-SQL.App -- run samples/05_Security_Diagnostics/WhatIf_Dry_Run.etlsql
dotnet run --project src/ETL-SQL.App -- run samples/05_Security_Diagnostics/Data_Quality_Rules.etlsql
```
