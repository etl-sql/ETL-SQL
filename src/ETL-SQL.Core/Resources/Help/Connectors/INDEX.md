# Connections

Connections link ETL-SQL to external data sources — databases, files, APIs, and protocols.

Syntax:
  CREATE CONNECTION <name> ON <TYPE>(<options>);
  DROP CONNECTION <name>;
  SHOW CONNECTIONS [INTO #table];

Connection types:

  Relational databases:
    MSSQL / SQLSERVER    — Microsoft SQL Server and Azure SQL
    POSTGRES / PG        — PostgreSQL
    ORACLE               — Oracle Database
    ODBC                 — Any ODBC-compatible source

  File formats:
    FLATFILE / CSV       — Delimited or fixed-width text files
    EXCEL / XLSX         — Excel workbooks
    JSON                 — JSON files
    XML                  — XML files
    PARQUET              — Apache Parquet columnar files
    AVRO                 — Apache Avro binary files
    DIRECTORY            — Local or UNC folder listing

  Protocols and services:
    API / HTTP           — REST endpoints
    SFTP / SSH           — Secure file transfer
    FTP / FTPS           — FTP file transfer
    AZURE_BLOB           — Azure Blob Storage
    SMTP                 — Email sending

  Other:
    MOCKDB               — In-memory test database

```sql
CREATE CONNECTION SalesDB ON MSSQL(
  SERVER             = 'sql.corp.local',
  DATABASE           = 'Sales',
  TRUSTED_CONNECTION = ON
);

CREATE CONNECTION ReportCSV ON FLATFILE(
  PATH      = 'C:\reports\output.csv',
  DELIMITER = ','
);

SHOW CONNECTIONS;
DROP CONNECTION ReportCSV;
```

Use HELP CONNECTORS <TYPE> for type-specific options (e.g. HELP CONNECTORS MSSQL).
