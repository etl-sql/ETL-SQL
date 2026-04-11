# ETL-SQL Data Connectors: Pattern-Centric Reference

This document is the authoritative reference for configuring external data source connections. It defines explicit **Connection Patterns** for each provider to remove ambiguity for both human engineers and AI agents.

---

## 1. Universal Connection Modes

Any connector in ETL-SQL can be initialized using one of two modes:

### 1.1 Property Mode (`WITH(...)`)
Recommended for readability, parameterization via variables, and AI agent clarity.
```sql
CREATE CONNECTION my_db ON MSSQL() WITH(SERVER='prod', DATABASE='ERP');
```

### 1.2 Raw String Mode (`ON TYPE('...')`)
A "Pass-Through" mode for native connection strings. Useful for `ENC:` encrypted strings or complex, provider-specific legacy strings.
```sql
CREATE CONNECTION legacy_db ON MSSQL('Server=DW;Database=ERP;Trusted_Connection=True');
```

---

## 2. Relational Database Patterns

### 2.1 Oracle (`ORACLE`)
Oracle connections are partitioned into two mutually exclusive patterns.

| Pattern | Mandatory Properties | Optional Properties |
| :--- | :--- | :--- |
| **TNS Pattern** | `TNS_NAME`, `USER`, `PASS` | `ENCRYPT`, `POOL_SIZE` |
| **Service Pattern** | `HOST`, `PORT`, `SERVICE_NAME`, `USER`, `PASS` | `INSTANCE_NAME`, `ENCRYPT` |

> [!CAUTION]
> **Mutual Exclusivity**: You cannot use `TNS_NAME` and `SERVICE_NAME` in the same connection block.

### 2.2 Microsoft SQL Server (`MSSQL`)
Supports Windows Auth vs. Standard Auth patterns.

| Pattern | Mandatory Properties | Optional Properties |
| :--- | :--- | :--- |
| **Standard Auth** | `SERVER`, `DATABASE`, `USER`, `PASS` | `PORT`, `USE_SSL`, `TIMEOUT` |
| **Windows Auth** | `SERVER`, `DATABASE`, `TRUSTED_CONNECTION=TRUE` | `TRUST_SERVER_CERTIFICATE` |

### 2.3 Postgres / NPSQL (`POSTGRES`)
| Pattern | Mandatory Properties | Optional Properties |
| :--- | :--- | :--- |
| **Standard Auth** | `HOST`, `DATABASE`, `USER`, `PASS` | `PORT` (Def: 5432), `SSL_MODE` |

---

## 3. Flat File & Document Patterns

### 3.1 `FLATFILE` / `CSV` / `TEXT`
| Pattern | Mandatory Properties | Optional Properties |
| :--- | :--- | :--- |
| **Delimited** | `PATH`, `DELIMITER` | `HEADER`, `START_AT`, `COMPRESS` |
| **Fixed-Width** | `PATH`, `FORMAT='FIXED'`, `TEMPLATE` | `START_AT`, `ENCRYPT` |

### 3.2 `EXCEL` / `XLSX`
| Pattern | Mandatory Properties | Optional Properties |
| :--- | :--- | :--- |
| **Sheet Pattern** | `PATH`, `SHEET` | `HEADER`, `RANGE` (e.g., 'A1:C10') |

---

## 4. Remote & Cloud Storage Patterns

### 4.1 `SFTP` / `SSH`
| Pattern | Mandatory Properties | Optional Properties |
| :--- | :--- | :--- |
| **Keyfile Auth** | `HOST`, `USER`, `KEYFILE` | `PORT` (Def: 22), `PASSPHRASE` |
| **Password Auth** | `HOST`, `USER`, `PASS` | `TIMEOUT`, `STRICT_HOST_KEY` |

### 4.2 `AZURE_BLOB`
| Pattern | Mandatory Properties | Optional Properties |
| :--- | :--- | :--- |
| **Account Key** | `ACCOUNT_NAME`, `ACCOUNT_KEY`, `CONTAINER` | `ENDPOINT_SUFFIX` |
| **SAS Pattern** | `ACCOUNT_NAME`, `SAS_TOKEN`, `CONTAINER` | `MANAGED_IDENTITY=TRUE` |

---

## 5. Lifecycle & Introspection Commands

- **`HELP CONNECTION <type>;`**: Displays supported patterns and mandatory properties for a specific provider.
- **`SHOW TABLES [ON <connection>];`**: Lists all tables (or files) in a specific source.
- **`SHOW COLUMNS FOR [<connection>.]<table_name>;`**: Lists schema metadata, data types, and nullability.
- **`TEST CONNECTION <name>;`**: Performs a ping and authentication handshake without side effects.

---
*Refer to [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) for syntax rules and [Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md) for Email/Filesystem automation.*
