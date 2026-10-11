# 10_Kitchen_Sinks: Automated Regression Fixture Decks

> [!NOTE]
> **Learning ETL-SQL?** This folder contains exhaustive, multi-thousand-line test decks used by automated CI suites to verify edge-case coverage across parser, AST, and runtime handlers.
> - For clear, topic-based tutorials, start with [`../00_QuickStart/`](../00_QuickStart/) through [`../07_Real_World/`](../07_Real_World/).
> - For individual visual chart references, use [`../visual_gallery/`](../visual_gallery/) (e.g. `bar_chart.rptsql` rather than the `01_BAR.rptsql` fixture deck).

## Contents

This directory serves three distinct verification roles:

1. **Language Feature Sinks (`*_sink.etlsql`)**:
   Exhaustive scripts verifying every syntax variation of a language feature (e.g., `types_sink.etlsql`, `functions_sink.etlsql`, `cte_setops_sink.etlsql`, `loops_sink.etlsql`).

2. **Visual Regression Decks (`00_Index.rptsql` through `39_CUSTOM_LAYERS.rptsql`)**:
   Historical test deck generating companion `.snapshot.json` and `.report.md` files used to verify rendering parity across browser engines and CI runners.

3. **External Connector Sinks (`postgres_sink.etlsql`, `mssql_sink.etlsql`, `parquet_enc_test.etlsql`)**:
   Deep integration coverage for dialect pass-through and encryption formats.

## Running the Fixtures

```bash
# Run an individual feature sink:
dotnet run --project src/ETL-SQL.App -- run samples/10_Kitchen_Sinks/types_sink.etlsql

# Validate all samples across the repository:
pwsh scripts/Test-AllSamples.ps1
```
