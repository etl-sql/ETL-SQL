# testdata

This directory contains checked-in fixtures used by ETL-SQL samples, report examples, connector examples, and spec-driven ingestion scenarios. These files are intentionally source-controlled when they provide deterministic input or expected output for examples.

## Source control policy

Include files here when they are:

- **Deterministic fixtures** - stable CSV, JSON, XML, Excel, Parquet, Avro, or fixed-width inputs used by samples.
- **Expected outputs** - small generated artifacts needed to prove sample behavior or documented examples.
- **Spec assets** - mapping specs, PDF/XLSX specs, and companion scripts for spec-driven ingestion tests.
- **Sanitized** - synthetic or anonymized data only, with no real customer data, secrets, credentials, private endpoints, or machine-specific paths.

Avoid checking in transient scratch files, local run output, unsanitized partner data, or regenerated large files unless a test or documented sample requires the exact bytes.

## Layout

| Path | Purpose |
| :--- | :--- |
| `Specs/` | Spec-driven ingestion fixtures, mapping specs, PDF/XLSX specs, and companion `.etlsql` scripts. |
| `inbound/` | Inbound partner-feed style inputs used by file movement and real-world samples. |
| `out/` | Expected or sample output files used by demos and regression checks. |
| `generate_sales.etlsql` / `generate_sales.ps1` | Helpers for regenerating synthetic sales data. |
| `sales_report.rptsql` | Report-SQL fixture that reads local test sales data. |
| `test_*` files | Historical sample fixtures for connector, bulk-load, report, stress, and format-specific examples. |
| `Users.parquet` | Parquet fixture for connector and sample coverage. |

## Maintenance notes

Keep fixture data synthetic and reproducible. When replacing a generated fixture, update the script or command that produces it, and verify every sample that reads the file. Test-only fixtures belong under [`../tests/testdata`](../tests/testdata). Large fixtures should stay here only when they cover behavior that small fixtures cannot exercise.
