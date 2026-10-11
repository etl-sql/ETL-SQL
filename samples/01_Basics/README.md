# 01_Basics: Engine Fundamentals

This directory demonstrates core ETL-SQL language mechanics: session variables, control flow, dynamic filtering, temporal date arithmetic, string/math functions, and in-engine `#temp` tables.

## Samples Overview

| File | Key Concepts Demonstrated |
| :--- | :--- |
| [`Variables_and_State.etlsql`](Variables_and_State.etlsql) | Declaring and assigning scalar session variables (`DECLARE @x INT = 10;`, `SET @x = @x + 1;`). |
| [`Lists_and_Foreach.etlsql`](Lists_and_Foreach.etlsql) | List variables and `FOREACH @item IN @list` iteration. |
| [`Dynamic_Filtering.etlsql`](Dynamic_Filtering.etlsql) | Constructing dynamic `WHERE` clauses and evaluating filtered queries. |
| [`Temporal_Date_Logic.etlsql`](Temporal_Date_Logic.etlsql) | Date arithmetic, `DATEADD`, `DATEDIFF`, and date formatting. |
| [`Function_Library.etlsql`](Function_Library.etlsql) | Built-in string manipulation, mathematical operations, and system functions. |
| [`Data_Preview.etlsql`](Data_Preview.etlsql) | Inspecting schema metadata and data previews via `DESCRIBE` and `TOP`. |
| [`Basic_ETL.etlsql`](Basic_ETL.etlsql) | Multi-column transformations and conditional logic mapped from source to staging. |

## Running These Samples

```bash
dotnet run --project src/ETL-SQL.App -- run samples/01_Basics/Variables_and_State.etlsql
dotnet run --project src/ETL-SQL.App -- run samples/01_Basics/Temporal_Date_Logic.etlsql
```
