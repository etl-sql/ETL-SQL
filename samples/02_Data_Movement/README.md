# 02_Data_Movement: Flat Files and Columnar Formats

This directory covers file ingestion, serialization, and high-performance columnar formats. ETL-SQL treats files as first-class query sources and destinations through connection aliases.

## Samples Overview

| File | Format / Feature | Description |
| :--- | :--- | :--- |
| [`Parquet_Read_Write.etlsql`](Parquet_Read_Write.etlsql) | Apache Parquet | Reading and writing columnar Parquet data with schema preservation. |
| [`generate_parquet.etlsql`](generate_parquet.etlsql) | Parquet Generation | Generating structured rows and materializing them to a `.parquet` file. |
| [`Avro_Read_Write.etlsql`](Avro_Read_Write.etlsql) | Apache Avro | Streaming binary Avro record sets into the engine. |
| [`Fixed_Width_Ingestion.etlsql`](Fixed_Width_Ingestion.etlsql) | Fixed Width | Ingesting legacy fixed-width positional files via column specifications. |
| [`Text_Qualifiers.etlsql`](Text_Qualifiers.etlsql) | CSV Qualifiers | Parsing complex CSV files with escaped quotes, embedded commas, and delimiters. |
| [`Bulk_Insert_Mapping.etlsql`](Bulk_Insert_Mapping.etlsql) | Bulk Insert | Mapping and streaming rows between connections in batches. |
| [`Enhanced_File_IO.etlsql`](Enhanced_File_IO.etlsql) | File Operations | Directory creation, file existence checks, and cleanup operations. |

## Running These Samples

```bash
dotnet run --project src/ETL-SQL.App -- run samples/02_Data_Movement/generate_parquet.etlsql
dotnet run --project src/ETL-SQL.App -- run samples/02_Data_Movement/Text_Qualifiers.etlsql
```
